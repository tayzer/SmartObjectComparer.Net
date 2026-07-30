using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Engine;

/// <summary>
/// Infers a payload format from a content type, falling back to the file
/// extension when the content type is absent or unhelpful.
/// </summary>
public static class PayloadFormatDetector
{
    public static PayloadFormat? Detect(string? contentType, string relativePath)
    {
        if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            return PayloadFormat.Json;
        }

        if (contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true)
        {
            return PayloadFormat.Xml;
        }

        string extension = Path.GetExtension(relativePath);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return PayloadFormat.Json;
        }

        if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return PayloadFormat.Xml;
        }

        return null;
    }
}
