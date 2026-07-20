using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;

namespace ParityBench.NET.Domain.AcceptedDifferences;

public static class AcceptedDifferenceFingerprintBuilder
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex GuidRegex = new Regex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex IsoDateRegex = new Regex(@"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:?\d{2})?)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex LongNumberRegex = new Regex(@"\b\d{5,}\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex HexTokenRegex = new Regex(@"\b[0-9a-f]{16,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    private static readonly string[] DynamicNameTokens =
    {
        "id",
        "guid",
        "key",
        "token",
        "request",
        "session",
        "correlation",
        "trace",
        "timestamp",
        "time",
        "date",
        "created",
        "updated",
        "modified",
    };

    public static AcceptedDifferenceFingerprint Create(ComparisonDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);

        string normalizedPath = StaticReportDifferenceIndexBuilder.NormalizePropertyPath(difference.PropertyPath);
        string category = StaticReportDifferenceIndexBuilder.CategorizeDifference(difference);
        string leafName = GetLeafPropertyName(normalizedPath);
        string valueAPattern = ScrubValue(difference.ValueA, leafName);
        string valueBPattern = ScrubValue(difference.ValueB, leafName);
        string rawFingerprint = string.Create(CultureInfo.InvariantCulture, $"{category}|{normalizedPath}|{valueAPattern}|{valueBPattern}");
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawFingerprint));

        return new AcceptedDifferenceFingerprint(
            Convert.ToHexString(hashBytes),
            normalizedPath,
            category,
            valueAPattern,
            valueBPattern);
    }

    private static string GetLeafPropertyName(string normalizedPath)
    {
        string[] segments = normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string leaf = segments.Length == 0 ? normalizedPath : segments[^1];
        int bracketIndex = leaf.IndexOf('[', StringComparison.Ordinal);
        return bracketIndex >= 0 ? leaf[..bracketIndex] : leaf;
    }

    private static string ScrubValue(string? value, string leafPropertyName)
    {
        if (value is null)
        {
            return "null";
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (bool.TryParse(value, out bool boolValue))
        {
            return boolValue ? "true" : "false";
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return "<datetime>";
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return LooksDynamicByName(leafPropertyName) ? "<number>" : value;
        }

        if (LooksDynamicByName(leafPropertyName))
        {
            return GetDynamicTokenForName(leafPropertyName);
        }

        string scrubbed = GuidRegex.Replace(value, "<guid>");
        scrubbed = IsoDateRegex.Replace(scrubbed, "<datetime>");
        scrubbed = HexTokenRegex.Replace(scrubbed, "<hex>");
        scrubbed = LongNumberRegex.Replace(scrubbed, "<number>");
        return scrubbed.Length > 256 ? scrubbed[..256] : scrubbed;
    }

    private static bool LooksDynamicByName(string leafPropertyName) =>
        !string.IsNullOrWhiteSpace(leafPropertyName) &&
        DynamicNameTokens.Any(token => leafPropertyName.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string GetDynamicTokenForName(string leafPropertyName)
    {
        if (leafPropertyName.Contains("date", StringComparison.OrdinalIgnoreCase) ||
            leafPropertyName.Contains("time", StringComparison.OrdinalIgnoreCase) ||
            leafPropertyName.Contains("timestamp", StringComparison.OrdinalIgnoreCase))
        {
            return "<datetime>";
        }

        if (leafPropertyName.Contains("guid", StringComparison.OrdinalIgnoreCase))
        {
            return "<guid>";
        }

        if (leafPropertyName.Contains("id", StringComparison.OrdinalIgnoreCase) ||
            leafPropertyName.Contains("key", StringComparison.OrdinalIgnoreCase))
        {
            return "<identifier>";
        }

        return "<dynamic>";
    }
}
