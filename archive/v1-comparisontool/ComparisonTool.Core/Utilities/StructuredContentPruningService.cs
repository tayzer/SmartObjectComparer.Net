using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Builds presentation-only JSON/XML content with ignore-complete fields removed.
/// </summary>
public sealed class StructuredContentPruningService
{
    private static readonly TimeSpan MatchRegexTimeout = TimeSpan.FromSeconds(1);

    private readonly ILogger<StructuredContentPruningService> logger;

    public StructuredContentPruningService(ILogger<StructuredContentPruningService> logger)
    {
        this.logger = logger;
    }

    public FocusedContentResult TryPrune(
        byte[] content,
        string? contentType,
        string? fileName,
        IReadOnlyCollection<string> ignoreCompletePaths)
    {
        if (content.Length == 0 || ignoreCompletePaths.Count == 0)
        {
            return FocusedContentResult.Unchanged();
        }

        var text = Encoding.UTF8.GetString(content);
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        return TryPrune(text, contentType, fileName, ignoreCompletePaths);
    }

    public FocusedContentResult TryPrune(
        string? content,
        string? contentType,
        string? fileName,
        IReadOnlyCollection<string> ignoreCompletePaths)
    {
        if (string.IsNullOrWhiteSpace(content) || ignoreCompletePaths.Count == 0)
        {
            return FocusedContentResult.Unchanged();
        }

        var matcher = new IgnorePathMatcher(BuildMatchPatterns(ignoreCompletePaths));
        return DetectDocumentKind(contentType, fileName, content) switch
        {
            StructuredDocumentKind.Json => TryPruneJson(content, matcher),
            StructuredDocumentKind.Xml => TryPruneXml(content, matcher),
            _ => FocusedContentResult.Unsupported(),
        };
    }

    private FocusedContentResult TryPruneJson(string content, IgnorePathMatcher ignoreMatcher)
    {
        try
        {
            var root = JsonNode.Parse(content);
            if (root == null)
            {
                return FocusedContentResult.Unchanged();
            }

            var removedCount = PruneJsonNode(root, string.Empty, ignoreMatcher);
            if (removedCount == 0)
            {
                return FocusedContentResult.Unchanged();
            }

            return FocusedContentResult.Pruned(
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                removedCount);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Could not prune JSON focused raw content.");
            return FocusedContentResult.Unsupported();
        }
    }

    private int PruneJsonNode(JsonNode node, string path, IgnorePathMatcher ignoreMatcher)
    {
        var removedCount = 0;

        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                var propertyPath = AppendPath(path, property.Key);
                if (ShouldIgnorePath(propertyPath, ignoreMatcher))
                {
                    obj.Remove(property.Key);
                    removedCount++;
                    continue;
                }

                if (property.Value != null)
                {
                    removedCount += PruneJsonNode(property.Value, propertyPath, ignoreMatcher);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                var item = array[index];
                if (item == null)
                {
                    continue;
                }

                removedCount += PruneJsonNode(item, $"{path}[{index}]", ignoreMatcher);
            }
        }

        return removedCount;
    }

    private FocusedContentResult TryPruneXml(string content, IgnorePathMatcher ignoreMatcher)
    {
        try
        {
            var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
            if (document.Root == null)
            {
                return FocusedContentResult.Unchanged();
            }

            var removedCount = PruneXmlChildren(document.Root, new[] { document.Root.Name.LocalName }, ignoreMatcher);
            if (removedCount == 0)
            {
                return FocusedContentResult.Unchanged();
            }

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                OmitXmlDeclaration = document.Declaration == null,
                Indent = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
            };

            var builder = new StringBuilder();
            using var stringWriter = new StringWriter(builder, System.Globalization.CultureInfo.InvariantCulture);
            using var writer = XmlWriter.Create(stringWriter, settings);
            document.Save(writer);
            writer.Flush();

            return FocusedContentResult.Pruned(builder.ToString(), removedCount);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Could not prune XML focused raw content.");
            return FocusedContentResult.Unsupported();
        }
    }

    private int PruneXmlChildren(XElement parent, IReadOnlyCollection<string> parentPaths, IgnorePathMatcher ignoreMatcher)
    {
        var removedCount = 0;
        var children = parent.Elements().ToList();
        var siblingTotals = children
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var siblingIndexes = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var child in children)
        {
            var name = child.Name.LocalName;
            siblingIndexes.TryGetValue(name, out var nextIndex);
            siblingIndexes[name] = nextIndex + 1;

            var childPaths = parentPaths
                .Select(parentPath => AppendPath(parentPath, name))
                .ToList();
            if (siblingTotals[name] > 1)
            {
                childPaths.AddRange(parentPaths.Select(parentPath => $"{AppendPath(parentPath, name)}[{nextIndex}]"));
                childPaths.AddRange(parentPaths.Select(parentPath => $"{AppendPath(parentPath, name)}[*]"));
            }

            if (ShouldIgnoreAny(childPaths, ignoreMatcher))
            {
                child.Remove();
                removedCount++;
                continue;
            }

            removedCount += PruneXmlChildren(child, childPaths, ignoreMatcher);
        }

        return removedCount;
    }

    private static IReadOnlyList<string> BuildMatchPatterns(IEnumerable<string> paths)
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var normalized = path.Trim();
            patterns.Add(normalized);

            var firstDot = normalized.IndexOf('.', StringComparison.Ordinal);
            if (firstDot > 0 && firstDot < normalized.Length - 1)
            {
                patterns.Add(normalized[(firstDot + 1) ..]);
            }
        }

        return patterns.ToList();
    }

    private static bool ShouldIgnoreAny(IEnumerable<string> paths, IgnorePathMatcher ignoreMatcher) =>
        paths.Any(path => ShouldIgnorePath(path, ignoreMatcher));

    private static bool ShouldIgnorePath(string path, IgnorePathMatcher ignoreMatcher)
    {
        if (ignoreMatcher.IsMatch(path))
        {
            return true;
        }

        var firstDot = path.IndexOf('.', StringComparison.Ordinal);
        if (firstDot > 0 && firstDot < path.Length - 1)
        {
            return ignoreMatcher.IsMatch(path[(firstDot + 1) ..]);
        }

        return false;
    }

    private static string AppendPath(string parent, string child) =>
        string.IsNullOrWhiteSpace(parent) ? child : $"{parent}.{child}";

    private static StructuredDocumentKind DetectDocumentKind(string? contentType, string? fileName, string text)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return StructuredDocumentKind.Json;
            }

            if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                return StructuredDocumentKind.Xml;
            }
        }

        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredDocumentKind.Json;
        }

        if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredDocumentKind.Xml;
        }

        var firstNonWhitespace = text.AsSpan().TrimStart();
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

    private sealed class IgnorePathMatcher
    {
        private readonly HashSet<string> exactPaths;
        private readonly List<string> descendantPrefixes;
        private readonly List<string> collectionPrefixes;
        private readonly List<Regex> collectionPatternRegexes;
        private readonly List<Regex> wildcardPatternRegexes;

        public IgnorePathMatcher(IEnumerable<string> ignorePatterns)
        {
            exactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            descendantPrefixes = new List<string>();
            collectionPrefixes = new List<string>();
            collectionPatternRegexes = new List<Regex>();
            wildcardPatternRegexes = new List<Regex>();

            foreach (var pattern in ignorePatterns
                .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
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

            if (exactPaths.Contains(propertyPath))
            {
                return true;
            }

            foreach (var prefix in descendantPrefixes)
            {
                if (propertyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var prefix in collectionPrefixes)
            {
                if (propertyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var regex in collectionPatternRegexes)
            {
                if (regex.IsMatch(propertyPath))
                {
                    return true;
                }
            }

            foreach (var regex in wildcardPatternRegexes)
            {
                if (regex.IsMatch(propertyPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static Regex BuildCollectionPatternRegex(string pattern)
        {
            var tempPattern = pattern.Replace("[*]", "COLLECTION_INDEX_PLACEHOLDER", StringComparison.Ordinal);
            var regexPattern = Regex.Escape(tempPattern)
                .Replace("COLLECTION_INDEX_PLACEHOLDER", @"\[\d+\]", StringComparison.Ordinal);

            return new Regex(
                $"^{regexPattern}$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture,
                MatchRegexTimeout);
        }

        private static Regex BuildWildcardPatternRegex(string pattern)
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "($|\\.)";
            return new Regex(
                regexPattern,
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture,
                MatchRegexTimeout);
        }
    }
}

public sealed class FocusedContentResult
{
    private FocusedContentResult(bool isSupported, bool wasPruned, string content, int removedFieldCount)
    {
        IsSupported = isSupported;
        WasPruned = wasPruned;
        Content = content;
        RemovedFieldCount = removedFieldCount;
    }

    public bool IsSupported { get; }

    public bool WasPruned { get; }

    public string Content { get; }

    public int RemovedFieldCount { get; }

    public static FocusedContentResult Unsupported() => new(false, false, string.Empty, 0);

    public static FocusedContentResult Unchanged() => new(true, false, string.Empty, 0);

    public static FocusedContentResult Pruned(string content, int removedFieldCount) => new(true, true, content, removedFieldCount);
}
