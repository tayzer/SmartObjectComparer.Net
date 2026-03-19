using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Utilities;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.AcceptedDifferences;

/// <summary>
/// Builds stable fingerprints for differences while masking dynamic values.
/// </summary>
public sealed class AcceptedDifferenceFingerprintBuilder
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex GuidRegex = new(
        @"\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex IsoDateRegex = new(
        @"\b\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:?\d{2})?)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex LongNumberRegex = new(
        @"\b\d{5,}\b",
        RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex HexTokenRegex = new(
        @"\b[0-9a-f]{16,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);
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

    private readonly ILogger<AcceptedDifferenceFingerprintBuilder> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcceptedDifferenceFingerprintBuilder"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public AcceptedDifferenceFingerprintBuilder(ILogger<AcceptedDifferenceFingerprintBuilder> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Creates a stable fingerprint for a difference.
    /// </summary>
    /// <param name="difference">The difference to fingerprint.</param>
    /// <returns>The fingerprint descriptor.</returns>
    public AcceptedDifferenceFingerprint Create(Difference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);

        var normalizedPath = PropertyPathNormalizer.NormalizePropertyPath(difference.PropertyName ?? string.Empty, logger);
        var category = DetermineCategory(difference);
        var leafPropertyName = GetLeafPropertyName(normalizedPath);
        var expectedValuePattern = ScrubValue(difference.Object1Value, leafPropertyName);
        var actualValuePattern = ScrubValue(difference.Object2Value, leafPropertyName);
        var rawFingerprint = string.Create(
            CultureInfo.InvariantCulture,
            $"{category}|{normalizedPath}|{expectedValuePattern}|{actualValuePattern}");

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawFingerprint));

        return new AcceptedDifferenceFingerprint
        {
            Fingerprint = Convert.ToHexString(hashBytes),
            NormalizedPropertyPath = normalizedPath,
            Category = category,
            ExpectedValuePattern = expectedValuePattern,
            ActualValuePattern = actualValuePattern,
        };
    }

    private static DifferenceCategory DetermineCategory(Difference difference)
    {
        if (difference.Object1Value == null || difference.Object2Value == null)
        {
            return DifferenceCategory.NullValueChange;
        }

        var value1 = Convert.ToString(difference.Object1Value, CultureInfo.InvariantCulture);
        var value2 = Convert.ToString(difference.Object2Value, CultureInfo.InvariantCulture);

        if (IsNumericDifference(value1, value2))
        {
            return DifferenceCategory.NumericValueChanged;
        }

        if (LooksLikeDateTime(value1) && LooksLikeDateTime(value2))
        {
            return DifferenceCategory.DateTimeChanged;
        }

        if (bool.TryParse(value1, out _) && bool.TryParse(value2, out _))
        {
            return DifferenceCategory.BooleanValueChanged;
        }

        if (!string.IsNullOrEmpty(value1) || !string.IsNullOrEmpty(value2))
        {
            return DifferenceCategory.TextContentChanged;
        }

        if (!string.IsNullOrEmpty(difference.PropertyName) && difference.PropertyName.Contains('[', StringComparison.Ordinal) && difference.PropertyName.Contains(']', StringComparison.Ordinal))
        {
            if (Regex.IsMatch(difference.PropertyName, @"\[(\d+|\*)\]$", RegexOptions.None, RegexTimeout))
            {
                return DifferenceCategory.CollectionItemChanged;
            }
        }

        return DifferenceCategory.ValueChanged;
    }

    private static bool IsNumericDifference(string? value1, string? value2) =>
        decimal.TryParse(value1, NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
        decimal.TryParse(value2, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    private static bool LooksLikeDateTime(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
         DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));

    private static string GetLeafPropertyName(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return string.Empty;
        }

        var segments = normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var leaf = segments.Length == 0 ? normalizedPath : segments[^1];
        var bracketIndex = leaf.IndexOf('[', StringComparison.Ordinal);
        return bracketIndex >= 0 ? leaf[..bracketIndex] : leaf;
    }

    private static string ScrubValue(object? value, string leafPropertyName)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is DateTime or DateTimeOffset)
        {
            return "<datetime>";
        }

        if (value is bool boolValue)
        {
            return boolValue ? "true" : "false";
        }

        if (IsNumericValue(value))
        {
            return LooksDynamicByName(leafPropertyName)
                ? "<number>"
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (LooksDynamicByName(leafPropertyName))
        {
            return GetDynamicTokenForName(leafPropertyName);
        }

        var scrubbed = GuidRegex.Replace(text, "<guid>");
        scrubbed = IsoDateRegex.Replace(scrubbed, "<datetime>");
        scrubbed = HexTokenRegex.Replace(scrubbed, "<hex>");
        scrubbed = LongNumberRegex.Replace(scrubbed, "<number>");

        if (decimal.TryParse(scrubbed, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return scrubbed;
        }

        return scrubbed.Length > 256 ? scrubbed[..256] : scrubbed;
    }

    private static bool IsNumericValue(object value) => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool LooksDynamicByName(string leafPropertyName)
    {
        if (string.IsNullOrWhiteSpace(leafPropertyName))
        {
            return false;
        }

        return DynamicNameTokens.Any(token => leafPropertyName.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

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