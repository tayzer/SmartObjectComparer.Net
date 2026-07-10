using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

internal static class FocusedRawContentBuilder
{
    private static readonly TimeSpan MatchRegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // The ignore-rule set is fixed for the whole run, but TryPrune used to rebuild an
    // IgnorePathMatcher (and recompile every RegexOptions.Compiled pattern in it) on every
    // call - twice per request, once per endpoint. Regex.Compiled construction uses
    // Reflection.Emit; under dozens of concurrent compare workers doing that
    // simultaneously, dynamic codegen contends heavily and construction cost dominates
    // the whole compare phase. Build each distinct matcher once and reuse it.
    private static readonly ConcurrentDictionary<string, IgnorePathMatcher> MatcherCache =
        new ConcurrentDictionary<string, IgnorePathMatcher>(StringComparer.Ordinal);

    public static async Task<RequestPairResult> TryAttachFocusedRawContentAsync(
        RequestPairResult result,
        RunId runId,
        ComparisonOptions comparisonOptions,
        IRunArtifactStore artifactStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(comparisonOptions);
        ArgumentNullException.ThrowIfNull(artifactStore);

        IReadOnlyList<string> ignorePaths = BuildFocusedIgnorePaths(comparisonOptions);

        if (ignorePaths.Count == 0 || result.ResponseA is null || result.ResponseB is null)
        {
            return result;
        }

        FocusedContent? focusedA = await TryBuildFocusedContentAsync(result.ResponseA, ignorePaths, artifactStore, cancellationToken).ConfigureAwait(false);
        FocusedContent? focusedB = await TryBuildFocusedContentAsync(result.ResponseB, ignorePaths, artifactStore, cancellationToken).ConfigureAwait(false);

        if (focusedA is null || focusedB is null || (!focusedA.WasPruned && !focusedB.WasPruned))
        {
            return result;
        }

        ResponseArtifactMetadata focusedResponseA = await SaveFocusedResponseAsync(runId, result.RelativePath, result.ResponseA, focusedA.Content, artifactStore, cancellationToken).ConfigureAwait(false);
        ResponseArtifactMetadata focusedResponseB = await SaveFocusedResponseAsync(runId, result.RelativePath, result.ResponseB, focusedB.Content, artifactStore, cancellationToken).ConfigureAwait(false);

        return result.WithFocusedRawContent(focusedResponseA, focusedResponseB, ignorePaths.Select(ToDisplayIgnorePath));
    }


    private static IReadOnlyList<string> BuildFocusedIgnorePaths(ComparisonOptions comparisonOptions)
    {
        IEnumerable<string> ignoreRulePaths = comparisonOptions.IgnoreRules
            .Where(rule => rule.IgnoreCompletely && !string.IsNullOrWhiteSpace(rule.PropertyPath))
            .Select(rule => rule.PropertyPath.Trim());

        IEnumerable<string> smartPropertyPaths = comparisonOptions.SmartIgnoreRules
            .Where(rule => rule.IsEnabled && rule.Kind == SmartIgnoreRuleKind.PropertyName && !string.IsNullOrWhiteSpace(rule.Value))
            .SelectMany(rule => new[] { rule.Value.Trim(), $"*.{rule.Value.Trim()}" });

        IEnumerable<string> smartNamePatterns = comparisonOptions.SmartIgnoreRules
            .Where(rule => rule.IsEnabled
                && rule.Kind == SmartIgnoreRuleKind.NamePattern
                && !string.IsNullOrWhiteSpace(rule.Value))
            .Select(rule => "regex:" + rule.Value.Trim());

        return ignoreRulePaths
            .Concat(smartPropertyPaths)
            .Concat(smartNamePatterns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private static string ToDisplayIgnorePath(string ignorePath) =>
        ignorePath.StartsWith("regex:", StringComparison.OrdinalIgnoreCase) ? ignorePath["regex:".Length..] : ignorePath;

    private static async Task<FocusedContent?> TryBuildFocusedContentAsync(
        ResponseArtifactMetadata response,
        IReadOnlyCollection<string> ignorePaths,
        IRunArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await artifactStore.OpenReadAsync(response.Artifact, cancellationToken).ConfigureAwait(false);
        using MemoryStream buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        string text = Encoding.UTF8.GetString(buffer.ToArray());
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        FocusedContent? pruned = TryPrune(text, response.ContentType, response.Artifact.ArtifactId, ignorePaths);
        if (pruned is null)
        {
            return null;
        }

        return pruned.WasPruned
            ? pruned
            : new FocusedContent(FormatForDisplay(text, response.ContentType, response.Artifact.ArtifactId), WasPruned: false);
    }

    private static async Task<ResponseArtifactMetadata> SaveFocusedResponseAsync(
        RunId runId,
        string relativePath,
        ResponseArtifactMetadata sourceResponse,
        string content,
        IRunArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Utf8WithoutBom.GetBytes(content);
        await using MemoryStream stream = new MemoryStream(bytes, writable: false);
        RequestItem focusedRequest = new RequestItem($"focused/{relativePath}", sourceResponse.ContentType ?? "text/plain", bytes.Length);
        return await artifactStore.SaveResponseAsync(
            runId,
            sourceResponse.Endpoint,
            focusedRequest,
            sourceResponse.StatusCode,
            sourceResponse.ContentType,
            stream,
            cancellationToken).ConfigureAwait(false);
    }

    private static FocusedContent? TryPrune(
        string content,
        string? contentType,
        string? fileName,
        IReadOnlyCollection<string> ignorePaths)
    {
        if (string.IsNullOrWhiteSpace(content) || ignorePaths.Count == 0)
        {
            return new FocusedContent(content, WasPruned: false);
        }

        IgnorePathMatcher matcher = GetOrCreateMatcher(ignorePaths);
        return DetectDocumentKind(contentType, fileName, content) switch
        {
            StructuredDocumentKind.Json => TryPruneJson(content, matcher),
            StructuredDocumentKind.Xml => TryPruneXml(content, matcher),
            _ => null,
        };
    }

    private static FocusedContent? TryPruneJson(string content, IgnorePathMatcher matcher)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(content);
            if (root is null)
            {
                return new FocusedContent(content, WasPruned: false);
            }

            int removedCount = PruneJsonNode(root, string.Empty, matcher);
            if (removedCount == 0)
            {
                return new FocusedContent(FormatForDisplay(content, "application/json", null), WasPruned: false);
            }

            return new FocusedContent(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), WasPruned: true);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int PruneJsonNode(JsonNode node, string path, IgnorePathMatcher matcher)
    {
        int removedCount = 0;
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj.ToList())
            {
                string propertyPath = AppendPath(path, property.Key);
                if (ShouldIgnorePath(propertyPath, matcher))
                {
                    obj.Remove(property.Key);
                    removedCount++;
                    continue;
                }

                if (property.Value is not null)
                {
                    removedCount += PruneJsonNode(property.Value, propertyPath, matcher);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                JsonNode? item = array[index];
                if (item is not null)
                {
                    removedCount += PruneJsonNode(item, $"{path}[{index}]", matcher);
                }
            }
        }

        return removedCount;
    }

    private static FocusedContent? TryPruneXml(string content, IgnorePathMatcher matcher)
    {
        try
        {
            XDocument document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
            if (document.Root is null)
            {
                return new FocusedContent(content, WasPruned: false);
            }

            int removedCount = PruneXmlChildren(document.Root, new[] { document.Root.Name.LocalName }, matcher);
            if (removedCount == 0)
            {
                return new FocusedContent(FormatForDisplay(content, "application/xml", null), WasPruned: false);
            }

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = Utf8WithoutBom,
                OmitXmlDeclaration = document.Declaration is null,
                Indent = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
            };

            StringBuilder builder = new StringBuilder();
            using StringWriter stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture);
            using XmlWriter writer = XmlWriter.Create(stringWriter, settings);
            document.Save(writer);
            writer.Flush();
            return new FocusedContent(builder.ToString(), WasPruned: true);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            return null;
        }
    }

    private static int PruneXmlChildren(XElement parent, IReadOnlyCollection<string> parentPaths, IgnorePathMatcher matcher)
    {
        int removedCount = 0;
        List<XElement> children = parent.Elements().ToList();
        Dictionary<string, int> siblingTotals = children
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Dictionary<string, int> siblingIndexes = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (XElement child in children)
        {
            string name = child.Name.LocalName;
            siblingIndexes.TryGetValue(name, out int nextIndex);
            siblingIndexes[name] = nextIndex + 1;

            List<string> childPaths = parentPaths.Select(parentPath => AppendPath(parentPath, name)).ToList();
            if (siblingTotals[name] > 1)
            {
                childPaths.AddRange(parentPaths.Select(parentPath => $"{AppendPath(parentPath, name)}[{nextIndex}]"));
                childPaths.AddRange(parentPaths.Select(parentPath => $"{AppendPath(parentPath, name)}[*]"));
            }

            if (childPaths.Any(path => ShouldIgnorePath(path, matcher)))
            {
                child.Remove();
                removedCount++;
                continue;
            }

            removedCount += PruneXmlChildren(child, childPaths, matcher);
        }

        return removedCount;
    }

    private static string FormatForDisplay(string text, string? contentType, string? fileName) =>
        DetectDocumentKind(contentType, fileName, text) switch
        {
            StructuredDocumentKind.Json => TryFormatJson(text),
            StructuredDocumentKind.Xml => TryFormatXml(text),
            _ => text,
        };

    private static string TryFormatJson(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static string TryFormatXml(string text)
    {
        try
        {
            XDocument document = XDocument.Parse(text, LoadOptions.None);
            StringBuilder builder = new StringBuilder();
            using StringWriter stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture);
            using XmlWriter writer = XmlWriter.Create(stringWriter, new XmlWriterSettings
            {
                OmitXmlDeclaration = document.Declaration is null,
                Indent = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
            });

            document.Save(writer);
            writer.Flush();
            return builder.ToString();
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            return text;
        }
    }

    private static IgnorePathMatcher GetOrCreateMatcher(IReadOnlyCollection<string> ignorePaths)
    {
        string cacheKey = string.Join('', ignorePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        return MatcherCache.GetOrAdd(cacheKey, _ => new IgnorePathMatcher(BuildMatchPatterns(ignorePaths)));
    }

    private static IReadOnlyList<string> BuildMatchPatterns(IEnumerable<string> paths)
    {
        HashSet<string> patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string normalized = path.Trim();
            patterns.Add(normalized);
            int firstDot = normalized.IndexOf('.', StringComparison.Ordinal);
            if (firstDot > 0 && firstDot < normalized.Length - 1)
            {
                patterns.Add(normalized[(firstDot + 1)..]);
            }
        }

        return patterns.ToList();
    }

    private static bool ShouldIgnorePath(string path, IgnorePathMatcher matcher)
    {
        if (matcher.IsMatch(path))
        {
            return true;
        }

        int firstDot = path.IndexOf('.', StringComparison.Ordinal);
        return firstDot > 0 && firstDot < path.Length - 1 && matcher.IsMatch(path[(firstDot + 1)..]);
    }

    private static string AppendPath(string parent, string child) =>
        string.IsNullOrWhiteSpace(parent) ? child : $"{parent}.{child}";

    private static StructuredDocumentKind DetectDocumentKind(string? contentType, string? fileName, string text)
    {
        if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            return StructuredDocumentKind.Json;
        }

        if (contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true)
        {
            return StructuredDocumentKind.Xml;
        }

        string extension = Path.GetExtension(fileName ?? string.Empty);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredDocumentKind.Json;
        }

        if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredDocumentKind.Xml;
        }

        ReadOnlySpan<char> firstNonWhitespace = text.AsSpan().TrimStart();
        if (firstNonWhitespace.Length == 0)
        {
            return StructuredDocumentKind.Unknown;
        }

        return firstNonWhitespace[0] switch
        {
            '{' or '[' => StructuredDocumentKind.Json,
            '<' => StructuredDocumentKind.Xml,
            _ => StructuredDocumentKind.Unknown,
        };
    }

    private enum StructuredDocumentKind
    {
        Unknown,
        Json,
        Xml,
    }

    private sealed record FocusedContent(string Content, bool WasPruned);

    private sealed class IgnorePathMatcher
    {
        private readonly HashSet<string> exactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> descendantPrefixes = new List<string>();
        private readonly List<string> collectionPrefixes = new List<string>();
        private readonly List<Regex> collectionPatternRegexes = new List<Regex>();
        private readonly List<Regex> wildcardPatternRegexes = new List<Regex>();

        public IgnorePathMatcher(IEnumerable<string> ignorePatterns)
        {
            foreach (string pattern in ignorePatterns.Where(pattern => !string.IsNullOrWhiteSpace(pattern)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (pattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
                {
                    wildcardPatternRegexes.Add(BuildSmartNamePatternRegex(pattern["regex:".Length..]));
                    continue;
                }

                exactPaths.Add(pattern);
                if (pattern.Contains("[*]", StringComparison.Ordinal))
                {
                    collectionPatternRegexes.Add(BuildCollectionPatternRegex(pattern));
                    continue;
                }

                if (pattern.Contains('*', StringComparison.Ordinal))
                {
                    wildcardPatternRegexes.Add(BuildWildcardPatternRegex(pattern));
                    continue;
                }

                descendantPrefixes.Add(pattern + ".");
                collectionPrefixes.Add(pattern + "[");
            }
        }

        public bool IsMatch(string? propertyPath)
        {
            if (string.IsNullOrWhiteSpace(propertyPath))
            {
                return false;
            }

            return exactPaths.Contains(propertyPath)
                || descendantPrefixes.Any(prefix => propertyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                || collectionPrefixes.Any(prefix => propertyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                || collectionPatternRegexes.Any(regex => regex.IsMatch(propertyPath))
                || wildcardPatternRegexes.Any(regex => regex.IsMatch(propertyPath));
        }

        private static Regex BuildCollectionPatternRegex(string pattern)
        {
            string tempPattern = pattern.Replace("[*]", "COLLECTION_INDEX_PLACEHOLDER", StringComparison.Ordinal);
            string regexPattern = Regex.Escape(tempPattern).Replace("COLLECTION_INDEX_PLACEHOLDER", @"\[\d+\]", StringComparison.Ordinal);
            return new Regex($"^{regexPattern}$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture, MatchRegexTimeout);
        }

        private static Regex BuildWildcardPatternRegex(string pattern)
        {
            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "($|\\.)";
            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture, MatchRegexTimeout);
        }

        private static Regex BuildSmartNamePatternRegex(string pattern)
        {
            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, MatchRegexTimeout);
            }
            catch (ArgumentException)
            {
                string wildcardPattern = "^" + Regex.Escape(pattern)
                    .Replace("\\*", ".*", StringComparison.Ordinal)
                    .Replace("\\?", ".", StringComparison.Ordinal) + "$";

                return new Regex(wildcardPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture, MatchRegexTimeout);
            }
        }
    }
}
