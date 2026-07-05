using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Engine;

internal static class ResponseMasker
{
    public static async Task<Stream?> MaskAsync(
        Stream body,
        string? contentType,
        IReadOnlyList<MaskRuleDefinition> maskRules,
        CancellationToken cancellationToken = default)
    {
        if (maskRules.Count == 0)
        {
            return null;
        }

        using MemoryStream original = new MemoryStream();
        await body.CopyToAsync(original, cancellationToken).ConfigureAwait(false);
        byte[] originalBytes = original.ToArray();
        string text = Encoding.UTF8.GetString(originalBytes);

        string? maskedText = IsJson(contentType, text)
            ? TryMaskJson(text, maskRules)
            : IsXml(contentType, text)
                ? TryMaskXml(text, maskRules)
                : null;

        byte[] outputBytes = Encoding.UTF8.GetBytes(maskedText ?? text);
        return new MemoryStream(outputBytes);
    }

    private static string? TryMaskJson(
        string text,
        IReadOnlyList<MaskRuleDefinition> maskRules)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(text);
            if (node is null)
            {
                return null;
            }

            foreach (MaskRuleDefinition rule in maskRules)
            {
                ApplyJsonRule(node, SplitPath(rule.PropertyPath), 0, rule);
            }

            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ApplyJsonRule(
        JsonNode node,
        IReadOnlyList<string> segments,
        int segmentIndex,
        MaskRuleDefinition rule)
    {
        if (segmentIndex >= segments.Count)
        {
            return;
        }

        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (item is not null)
                {
                    ApplyJsonRule(item, segments, segmentIndex, rule);
                }
            }

            return;
        }

        if (node is not JsonObject obj)
        {
            return;
        }

        PathSegment segment = PathSegment.Parse(segments[segmentIndex]);
        if (!obj.TryGetPropertyValue(segment.Name, out JsonNode? child) || child is null)
        {
            return;
        }

        bool isFinalSegment = segmentIndex == segments.Count - 1;
        if (segment.HasWildcard && child is JsonArray childArray)
        {
            foreach (JsonNode? item in childArray)
            {
                if (item is null)
                {
                    continue;
                }

                if (isFinalSegment)
                {
                    MaskArrayValue(childArray, item, rule);
                }
                else
                {
                    ApplyJsonRule(item, segments, segmentIndex + 1, rule);
                }
            }

            return;
        }

        if (segment.Index is int index && child is JsonArray indexedArray)
        {
            if (index < 0 || index >= indexedArray.Count || indexedArray[index] is null)
            {
                return;
            }

            if (isFinalSegment)
            {
                indexedArray[index] = MaskValue(indexedArray[index], rule);
            }
            else
            {
                ApplyJsonRule(indexedArray[index]!, segments, segmentIndex + 1, rule);
            }

            return;
        }

        if (isFinalSegment)
        {
            obj[segment.Name] = MaskValue(child, rule);
            return;
        }

        ApplyJsonRule(child, segments, segmentIndex + 1, rule);
    }

    private static void MaskArrayValue(
        JsonArray array,
        JsonNode item,
        MaskRuleDefinition rule)
    {
        int itemIndex = array.IndexOf(item);
        if (itemIndex >= 0)
        {
            array[itemIndex] = MaskValue(item, rule);
        }
    }

    private static JsonNode MaskValue(JsonNode? value, MaskRuleDefinition rule)
    {
        string text = value switch
        {
            JsonValue jsonValue => jsonValue.ToJsonString().Trim('"'),
            null => string.Empty,
            _ => value.ToJsonString(),
        };

        return MaskText(text, rule);
    }

    private static string? TryMaskXml(
        string text,
        IReadOnlyList<MaskRuleDefinition> maskRules)
    {
        try
        {
            XDocument document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            if (document.Root is null)
            {
                return null;
            }

            foreach (MaskRuleDefinition rule in maskRules)
            {
                IReadOnlyList<string> segments = SplitPath(rule.PropertyPath);
                int startIndex = segments.Count > 0
                    && string.Equals(document.Root.Name.LocalName, PathSegment.Parse(segments[0]).Name, StringComparison.OrdinalIgnoreCase)
                        ? 1
                        : 0;

                ApplyXmlRule(document.Root, segments, startIndex, rule);
            }

            return document.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyXmlRule(
        XElement element,
        IReadOnlyList<string> segments,
        int segmentIndex,
        MaskRuleDefinition rule)
    {
        if (segmentIndex >= segments.Count)
        {
            element.Value = MaskText(element.Value, rule);
            return;
        }

        PathSegment segment = PathSegment.Parse(segments[segmentIndex]);
        IEnumerable<XElement> children = element
            .Elements()
            .Where(child => string.Equals(child.Name.LocalName, segment.Name, StringComparison.OrdinalIgnoreCase));

        if (segment.Index is int index)
        {
            children = children.Skip(index).Take(1);
        }

        foreach (XElement child in children.ToList())
        {
            ApplyXmlRule(child, segments, segmentIndex + 1, rule);
        }
    }

    private static IReadOnlyList<string> SplitPath(string propertyPath) =>
        propertyPath
            .Trim()
            .TrimStart('$')
            .TrimStart('.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string MaskText(string value, MaskRuleDefinition rule)
    {
        int preservedCharacters = Math.Min(rule.PreserveLastCharacters, value.Length);
        int maskedCharacters = value.Length - preservedCharacters;

        return new string(rule.MaskCharacter[0], maskedCharacters) + value[^preservedCharacters..];
    }

    private static bool IsJson(string? contentType, string text) =>
        contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
        || text.TrimStart().StartsWith("{", StringComparison.Ordinal)
        || text.TrimStart().StartsWith("[", StringComparison.Ordinal);

    private static bool IsXml(string? contentType, string text) =>
        contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true
        || text.TrimStart().StartsWith("<", StringComparison.Ordinal);

    private sealed record PathSegment(string Name, bool HasWildcard, int? Index)
    {
        public static PathSegment Parse(string segment)
        {
            int bracketStart = segment.IndexOf('[', StringComparison.Ordinal);
            if (bracketStart < 0)
            {
                return new PathSegment(segment, false, null);
            }

            string name = segment[..bracketStart];
            string indexText = segment[(bracketStart + 1)..].TrimEnd(']');
            return indexText == "*"
                ? new PathSegment(name, true, null)
                : int.TryParse(indexText, out int index)
                    ? new PathSegment(name, false, index)
                    : new PathSegment(name, false, null);
        }
    }
}
