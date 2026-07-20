using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;
using System.Xml;
using System.Xml.Linq;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Masks configured response fields in persisted request-comparison response files.
/// </summary>
public sealed class ResponseMaskingService
{
    private readonly ILogger<ResponseMaskingService> logger;

    public ResponseMaskingService(ILogger<ResponseMaskingService> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Validates mask rules and throws when they are invalid.
    /// </summary>
    public void ValidateRules(IReadOnlyList<MaskRuleDto> maskRules)
    {
        _ = NormalizeRules(maskRules);
    }

    /// <summary>
    /// Masks response content before it is persisted to disk.
    /// </summary>
    public byte[] MaskContent(
        byte[] content,
        string? contentType,
        string filePath,
        IReadOnlyList<MaskRuleDto> maskRules)
    {
        if (content.Length == 0 || maskRules.Count == 0)
        {
            return content;
        }

        var normalizedRules = NormalizeRules(maskRules);
        if (normalizedRules.Count == 0)
        {
            return content;
        }

        return MaskContentInternal(content, contentType, filePath, normalizedRules);
    }

    private byte[] MaskContentInternal(
        byte[] content,
        string? contentType,
        string filePath,
        IReadOnlyList<NormalizedMaskRule> normalizedRules)
    {
        if (content.Length == 0 || normalizedRules.Count == 0)
        {
            return content;
        }

        var format = DetectFormat(contentType, filePath, content);
        return format switch
        {
            ResponseDocumentFormat.Json => MaskJsonContent(content, contentType, filePath, normalizedRules),
            ResponseDocumentFormat.Xml => MaskXmlContent(content, contentType, filePath, normalizedRules),
            _ => content,
        };
    }

    /// <summary>
    /// Applies mask rules to persisted response files in-place.
    /// </summary>
    public async Task MaskResponsesAsync(
        IReadOnlyList<RequestExecutionResult> executionResults,
        IReadOnlyList<MaskRuleDto> maskRules,
        CancellationToken cancellationToken = default)
    {
        if (executionResults.Count == 0 || maskRules.Count == 0)
        {
            return;
        }

        var normalizedRules = NormalizeRules(maskRules);
        if (normalizedRules.Count == 0)
        {
            return;
        }

        foreach (var executionResult in executionResults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await MaskFileAsync(executionResult.ResponsePathA, executionResult.ContentTypeA, normalizedRules, cancellationToken)
                .ConfigureAwait(false);
            await MaskFileAsync(executionResult.ResponsePathB, executionResult.ContentTypeB, normalizedRules, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static List<NormalizedMaskRule> NormalizeRules(IReadOnlyList<MaskRuleDto> maskRules)
    {
        var normalizedRules = new List<NormalizedMaskRule>(maskRules.Count);

        foreach (var rule in maskRules)
        {
            if (rule is null)
            {
                throw new InvalidOperationException("Mask rules cannot contain null entries.");
            }

            if (string.IsNullOrWhiteSpace(rule.PropertyPath))
            {
                throw new InvalidOperationException("Mask rule propertyPath is required.");
            }

            if (rule.PreserveLastCharacters < 0)
            {
                throw new InvalidOperationException(
                    $"Mask rule '{rule.PropertyPath}' has an invalid preserveLastCharacters value {rule.PreserveLastCharacters}. Values must be zero or greater.");
            }

            var maskCharacter = string.IsNullOrWhiteSpace(rule.MaskCharacter)
                ? "*"
                : rule.MaskCharacter;

            if (maskCharacter.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Mask rule '{rule.PropertyPath}' must specify exactly one maskCharacter.");
            }

            normalizedRules.Add(new NormalizedMaskRule(
                PropertyPathNormalizer.NormalizePropertyPath(rule.PropertyPath),
                rule.PreserveLastCharacters,
                maskCharacter[0]));
        }

        return normalizedRules;
    }

    private async Task MaskFileAsync(
        string? filePath,
        string? contentType,
        IReadOnlyList<NormalizedMaskRule> maskRules,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        var content = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (content.Length == 0)
        {
            return;
        }

        var maskedBytes = MaskContentInternal(content, contentType, filePath, maskRules);
        if (maskedBytes.SequenceEqual(content))
        {
            return;
        }

        await File.WriteAllBytesAsync(filePath, maskedBytes, cancellationToken).ConfigureAwait(false);
    }

    private static ResponseDocumentFormat DetectFormat(string? contentType, string filePath, byte[] content)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (IsJsonContentType(contentType))
            {
                return ResponseDocumentFormat.Json;
            }

            if (IsXmlContentType(contentType))
            {
                return ResponseDocumentFormat.Xml;
            }
        }

        var sniffEncoding = ResolveEncoding(content, contentType, null);
        var text = DecodeText(content, sniffEncoding);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var trimmedText = text.TrimStart();
            if (trimmedText.Length > 0 && (trimmedText[0] == '{' || trimmedText[0] == '['))
            {
                return ResponseDocumentFormat.Json;
            }

            if (trimmedText.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                return ResponseDocumentFormat.Xml;
            }

            if (trimmedText.Length > 0
                && trimmedText[0] == '<'
                && !trimmedText.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
                && !trimmedText.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase))
            {
                return ResponseDocumentFormat.Xml;
            }
        }

        var extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return ResponseDocumentFormat.Json;
        }

        if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return ResponseDocumentFormat.Xml;
        }

        return ResponseDocumentFormat.Unknown;
    }

    private byte[] MaskJsonContent(
        byte[] content,
        string? contentType,
        string filePath,
        IReadOnlyList<NormalizedMaskRule> maskRules)
    {
        var encoding = ResolveEncoding(content, contentType, null);
        var text = DecodeText(content, encoding);
        if (string.IsNullOrWhiteSpace(text))
        {
            return content;
        }

        JsonNode? rootNode;

        try
        {
            rootNode = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Skipping masking for invalid JSON response {FilePath}", filePath);
            return content;
        }

        if (rootNode == null)
        {
            return content;
        }

        var didChange = MaskJsonNode(rootNode, string.Empty, maskRules);
        if (!didChange)
        {
            return content;
        }

        var maskedContent = rootNode.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = ShouldWriteIndentedJson(text),
        });

        return EncodeText(maskedContent, encoding, HasByteOrderMark(content));
    }

    private byte[] MaskXmlContent(
        byte[] content,
        string? contentType,
        string filePath,
        IReadOnlyList<NormalizedMaskRule> maskRules)
    {
        XDocument document;

        try
        {
            using var input = new MemoryStream(content, writable: false);
            document = XDocument.Load(input, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Skipping masking for invalid XML response {FilePath}", filePath);
            return content;
        }

        if (document.Root == null)
        {
            return content;
        }

        var didChange = MaskXmlElement(document.Root, document.Root.Name.LocalName, maskRules);
        if (!didChange)
        {
            return content;
        }

        var outputEncoding = ResolveEncoding(content, contentType, document.Declaration?.Encoding);

        using var stream = new MemoryStream();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = outputEncoding,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            OmitXmlDeclaration = document.Declaration == null,
        });

        document.Save(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static string DecodeText(byte[] content, Encoding encoding)
    {
        var text = encoding.GetString(content);
        return text.Length > 0 && text[0] == '\uFEFF'
            ? text[1..]
            : text;
    }

    private static bool ShouldWriteIndentedJson(string json)
    {
        return json.Contains('\n', StringComparison.Ordinal) || json.Contains('\r', StringComparison.Ordinal);
    }

    private static byte[] EncodeText(string text, Encoding encoding, bool includeByteOrderMark)
    {
        var preamble = includeByteOrderMark ? encoding.GetPreamble() : Array.Empty<byte>();
        var bytes = encoding.GetBytes(text);

        if (preamble.Length == 0)
        {
            return bytes;
        }

        var result = new byte[preamble.Length + bytes.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(bytes, 0, result, preamble.Length, bytes.Length);
        return result;
    }

    private static Encoding ResolveEncoding(byte[] content, string? contentType, string? xmlDeclarationEncoding)
    {
        if (TryGetEncodingFromContentType(contentType, out var contentTypeEncoding))
        {
            return contentTypeEncoding;
        }

        if (!string.IsNullOrWhiteSpace(xmlDeclarationEncoding))
        {
            try
            {
                return Encoding.GetEncoding(xmlDeclarationEncoding);
            }
            catch (ArgumentException)
            {
            }
        }

        if (content.Length >= 4)
        {
            if (content[0] == 0xFF && content[1] == 0xFE && content[2] == 0x00 && content[3] == 0x00)
            {
                return new UTF32Encoding(false, true);
            }

            if (content[0] == 0x00 && content[1] == 0x00 && content[2] == 0xFE && content[3] == 0xFF)
            {
                return new UTF32Encoding(true, true);
            }
        }

        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return new UTF8Encoding(true);
        }

        if (content.Length >= 2)
        {
            if (content[0] == 0xFF && content[1] == 0xFE)
            {
                return Encoding.Unicode;
            }

            if (content[0] == 0xFE && content[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode;
            }
        }

        return new UTF8Encoding(false);
    }

    private static bool TryGetEncodingFromContentType(string? contentType, out Encoding encoding)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && MediaTypeHeaderValue.TryParse(contentType, out var parsed)
            && !string.IsNullOrWhiteSpace(parsed.CharSet))
        {
            try
            {
                encoding = Encoding.GetEncoding(parsed.CharSet);
                return true;
            }
            catch (ArgumentException)
            {
            }
        }

        encoding = new UTF8Encoding(false);
        return false;
    }

    private static bool HasByteOrderMark(byte[] content)
        => content.Length >= 2 && (
            (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
            || (content[0] == 0xFF && content[1] == 0xFE)
            || (content[0] == 0xFE && content[1] == 0xFF)
            || (content.Length >= 4 && content[0] == 0xFF && content[1] == 0xFE && content[2] == 0x00 && content[3] == 0x00)
            || (content.Length >= 4 && content[0] == 0x00 && content[1] == 0x00 && content[2] == 0xFE && content[3] == 0xFF));

    private static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsXmlContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.Contains("xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MaskJsonNode(
        JsonNode node,
        string currentPath,
        IReadOnlyList<NormalizedMaskRule> maskRules)
    {
        var didChange = false;

        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToList())
                {
                    if (property.Key == null || property.Value == null)
                    {
                        continue;
                    }

                    var childPath = string.IsNullOrEmpty(currentPath)
                        ? property.Key
                        : $"{currentPath}.{property.Key}";

                    didChange |= MaskJsonNode(property.Value, childPath, maskRules);
                }

                break;

            case JsonArray jsonArray:
                for (var i = 0; i < jsonArray.Count; i++)
                {
                    var child = jsonArray[i];
                    if (child == null)
                    {
                        continue;
                    }

                    var childPath = string.IsNullOrEmpty(currentPath)
                        ? $"[{i}]"
                        : $"{currentPath}[{i}]";

                    didChange |= MaskJsonNode(child, childPath, maskRules);
                }

                break;

            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var stringValue):
                var matchingRule = FindMatchingRule(currentPath, maskRules);
                if (matchingRule != null)
                {
                    var maskedValue = ApplyMask(stringValue, matchingRule.Value);
                    if (!string.Equals(maskedValue, stringValue, StringComparison.Ordinal))
                    {
                        ReplaceJsonValue(node, maskedValue);
                        didChange = true;
                    }
                }

                break;
        }

        return didChange;
    }

    private static void ReplaceJsonValue(JsonNode node, string maskedValue)
    {
        if (node.Parent is JsonObject parentObject)
        {
            var property = parentObject.First(item => ReferenceEquals(item.Value, node));
            parentObject[property.Key] = JsonValue.Create(maskedValue);
            return;
        }

        if (node.Parent is JsonArray parentArray)
        {
            for (var i = 0; i < parentArray.Count; i++)
            {
                if (ReferenceEquals(parentArray[i], node))
                {
                    parentArray[i] = JsonValue.Create(maskedValue);
                    return;
                }
            }
        }
    }

    private static bool MaskXmlElement(
        XElement element,
        string currentPath,
        IReadOnlyList<NormalizedMaskRule> maskRules)
    {
        var didChange = false;

        if (!element.Elements().Any())
        {
            var matchingRule = FindMatchingRule(currentPath, maskRules);
            if (matchingRule != null && !string.IsNullOrEmpty(element.Value))
            {
                var maskedValue = ApplyMask(element.Value, matchingRule.Value);
                if (!string.Equals(maskedValue, element.Value, StringComparison.Ordinal))
                {
                    element.Value = maskedValue;
                    didChange = true;
                }
            }

            return didChange;
        }

        var children = element.Elements().ToList();
        var siblingCounts = children
            .GroupBy(child => child.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var siblingIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in children)
        {
            siblingIndexes.TryGetValue(child.Name.LocalName, out var index);
            siblingIndexes[child.Name.LocalName] = index + 1;

            var segment = siblingCounts[child.Name.LocalName] > 1
                ? $"{child.Name.LocalName}[{index}]"
                : child.Name.LocalName;
            var childPath = $"{currentPath}.{segment}";

            didChange |= MaskXmlElement(child, childPath, maskRules);
        }

        return didChange;
    }

    private static NormalizedMaskRule? FindMatchingRule(
        string currentPath,
        IReadOnlyList<NormalizedMaskRule> maskRules)
    {
        var normalizedPath = PropertyPathNormalizer.NormalizePropertyPath(currentPath);
        foreach (var rule in maskRules)
        {
            if (PathsMatch(rule.PropertyPath, normalizedPath))
            {
                return rule;
            }
        }

        return null;
    }

    private static bool PathsMatch(string rulePath, string currentPath)
    {
        if (string.Equals(rulePath, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ruleSegments = rulePath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var currentSegments = currentPath.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (ruleSegments.Length != currentSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < ruleSegments.Length; i++)
        {
            var ruleSegment = ruleSegments[i];
            var currentSegment = currentSegments[i];

            if (string.Equals(ruleSegment, currentSegment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ruleSegment.EndsWith("[*]", StringComparison.Ordinal)
                && string.Equals(ruleSegment[..^3], currentSegment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string ApplyMask(string value, NormalizedMaskRule rule)
    {
        if (string.IsNullOrEmpty(value) || rule.PreserveLastCharacters >= value.Length)
        {
            return value;
        }

        var maskedLength = value.Length - rule.PreserveLastCharacters;
        if (rule.PreserveLastCharacters == 0)
        {
            return new string(rule.MaskCharacter, maskedLength);
        }

        return new string(rule.MaskCharacter, maskedLength) + value[^rule.PreserveLastCharacters..];
    }

    private readonly record struct NormalizedMaskRule(
        string PropertyPath,
        int PreserveLastCharacters,
        char MaskCharacter);

    private enum ResponseDocumentFormat
    {
        Unknown,
        Json,
        Xml,
    }
}