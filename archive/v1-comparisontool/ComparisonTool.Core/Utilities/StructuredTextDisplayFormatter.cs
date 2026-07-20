using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Formats structured text for human-friendly side-by-side display.
/// Intended for presentation-only scenarios such as static reports.
/// </summary>
public static class StructuredTextDisplayFormatter
{
    /// <summary>
    /// Formats JSON or XML for display when the content can be identified and parsed.
    /// Returns the original text unchanged when the content is not recognized or is invalid.
    /// </summary>
    public static string FormatForDisplay(string? text, string? contentType = null, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        return DetectDocumentKind(contentType, fileName, text) switch
        {
            StructuredDocumentKind.Json => TryFormatJson(text),
            StructuredDocumentKind.Xml => TryFormatXml(text),
            _ => text,
        };
    }

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

    private static string TryFormatJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
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
            var document = XDocument.Parse(text, LoadOptions.None);
            var builder = new StringBuilder();

            using var writer = new StringWriter(builder, CultureInfo.InvariantCulture);
            using var xmlWriter = XmlWriter.Create(writer, new XmlWriterSettings
            {
                OmitXmlDeclaration = document.Declaration == null,
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
            });

            document.Save(xmlWriter);
            xmlWriter.Flush();
            return builder.ToString();
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            return text;
        }
    }

    private enum StructuredDocumentKind
    {
        Unknown,
        Json,
        Xml,
    }
}