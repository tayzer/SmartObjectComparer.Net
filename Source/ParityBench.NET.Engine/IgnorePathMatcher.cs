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

internal sealed class IgnorePathMatcher
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
        return new Regex($"^{regexPattern}$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture, FocusedRawContentBuilder.MatchRegexTimeout);
    }

    private static Regex BuildWildcardPatternRegex(string pattern)
    {
        string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "($|\\.)";
        return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture, FocusedRawContentBuilder.MatchRegexTimeout);
    }

    private static Regex BuildSmartNamePatternRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, FocusedRawContentBuilder.MatchRegexTimeout);
        }
        catch (ArgumentException)
        {
            string wildcardPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal) + "$";

            return new Regex(wildcardPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture, FocusedRawContentBuilder.MatchRegexTimeout);
        }
    }
}
