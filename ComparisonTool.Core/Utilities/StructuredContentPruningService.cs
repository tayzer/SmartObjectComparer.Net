using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using ComparisonTool.Core.Comparison.Configuration;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Builds presentation-only JSON/XML content with ignore-complete fields removed.
/// </summary>
public sealed class StructuredContentPruningService
{
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

        var patterns = BuildMatchPatterns(ignoreCompletePaths);
        return DetectDocumentKind(contentType, fileName, content) switch
        {
            StructuredDocumentKind.Json => TryPruneJson(content, patterns),
            StructuredDocumentKind.Xml => TryPruneXml(content, patterns),
            _ => FocusedContentResult.Unsupported(),
        };
    }

    private FocusedContentResult TryPruneJson(string content, IReadOnlyCollection<string> ignorePatterns)
    {
        try
        {
            var root = JsonNode.Parse(content);
            if (root == null)
            {
                return FocusedContentResult.Unchanged();
            }

            var removedCount = PruneJsonNode(root, string.Empty, ignorePatterns);
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

    private int PruneJsonNode(JsonNode node, string path, IReadOnlyCollection<string> ignorePatterns)
    {
        var removedCount = 0;

        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                var propertyPath = AppendPath(path, property.Key);
                if (ShouldIgnoreAny(BuildPathCandidates(propertyPath), ignorePatterns))
                {
                    obj.Remove(property.Key);
                    removedCount++;
                    continue;
                }

                if (property.Value != null)
                {
                    removedCount += PruneJsonNode(property.Value, propertyPath, ignorePatterns);
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

                removedCount += PruneJsonNode(item, $"{path}[{index}]", ignorePatterns);
            }
        }

        return removedCount;
    }

    private FocusedContentResult TryPruneXml(string content, IReadOnlyCollection<string> ignorePatterns)
    {
        try
        {
            var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
            if (document.Root == null)
            {
                return FocusedContentResult.Unchanged();
            }

            var removedCount = PruneXmlChildren(document.Root, new[] { document.Root.Name.LocalName }, ignorePatterns);
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

    private int PruneXmlChildren(XElement parent, IReadOnlyCollection<string> parentPaths, IReadOnlyCollection<string> ignorePatterns)
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

            var candidates = childPaths.SelectMany(BuildPathCandidates).ToList();
            if (ShouldIgnoreAny(candidates, ignorePatterns))
            {
                child.Remove();
                removedCount++;
                continue;
            }

            removedCount += PruneXmlChildren(child, childPaths, ignorePatterns);
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

    private static IEnumerable<string> BuildPathCandidates(string path)
    {
        yield return path;

        var firstDot = path.IndexOf('.', StringComparison.Ordinal);
        if (firstDot > 0 && firstDot < path.Length - 1)
        {
            yield return path[(firstDot + 1) ..];
        }
    }

    private static bool ShouldIgnoreAny(IEnumerable<string> candidates, IReadOnlyCollection<string> ignorePatterns) =>
        candidates.Any(candidate => PropertyIgnoreHelper.ShouldIgnoreProperty(candidate, ignorePatterns));

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