using System.Text.RegularExpressions;

namespace ParityBench.NET.Engine;

internal sealed class IgnorePathMatcher
{
    private readonly HashSet<string> exactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> descendantPrefixes = new List<string>();
    private readonly List<string> collectionPrefixes = new List<string>();
    private readonly List<string> collectionPatterns = new List<string>();
    private readonly HashSet<string> leafNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                collectionPatterns.Add(pattern);
                continue;
            }

            if (pattern.StartsWith("*.", StringComparison.Ordinal)
                && pattern.AsSpan(2).IndexOf('*') < 0)
            {
                leafNames.Add(pattern[2..]);
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

        if (exactPaths.Contains(propertyPath) || MatchesLeafName(propertyPath))
        {
            return true;
        }

        foreach (string prefix in descendantPrefixes)
        {
            if (propertyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return true; }
        }

        foreach (string prefix in collectionPrefixes)
        {
            if (propertyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return true; }
        }

        foreach (string pattern in collectionPatterns)
        {
            if (MatchesCollectionWildcard(pattern, propertyPath)) { return true; }
        }

        foreach (Regex regex in wildcardPatternRegexes)
        {
            if (regex.IsMatch(propertyPath)) { return true; }
        }

        return false;
    }

    public bool IsDirectChildMatch(string parentPath, string propertyName)
    {
        if (leafNames.Contains(propertyName))
        {
            return true;
        }

        foreach (string pattern in exactPaths)
        {
            int separatorIndex = pattern.LastIndexOf('.');
            if (separatorIndex < 0)
            {
                if (parentPath.Length == 0 && string.Equals(pattern, propertyName, StringComparison.OrdinalIgnoreCase)) { return true; }
                continue;
            }

            if (pattern.AsSpan(separatorIndex + 1).Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && pattern.AsSpan(0, separatorIndex).Equals(parentPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (string pattern in collectionPatterns)
        {
            int separatorIndex = pattern.LastIndexOf('.');
            if (separatorIndex >= 0
                && pattern.AsSpan(separatorIndex + 1).Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && MatchesCollectionWildcard(pattern.AsSpan(0, separatorIndex), parentPath))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesLeafName(string propertyPath)
    {
        if (leafNames.Count == 0) { return false; }
        ReadOnlySpan<char> leaf = propertyPath.AsSpan(propertyPath.LastIndexOf('.') + 1);
        int collectionIndex = leaf.IndexOf('[');
        if (collectionIndex >= 0) { leaf = leaf[..collectionIndex]; }
        foreach (string name in leafNames)
        {
            if (leaf.Equals(name, StringComparison.OrdinalIgnoreCase)) { return true; }
        }

        return false;
    }

    private static bool MatchesCollectionWildcard(ReadOnlySpan<char> pattern, ReadOnlySpan<char> candidate)
    {
        int patternIndex = 0;
        int candidateIndex = 0;
        while (patternIndex < pattern.Length)
        {
            if (patternIndex + 2 < pattern.Length
                && pattern[patternIndex] == '['
                && pattern[patternIndex + 1] == '*'
                && pattern[patternIndex + 2] == ']')
            {
                if (candidateIndex >= candidate.Length || candidate[candidateIndex++] != '[') { return false; }
                int digitStart = candidateIndex;
                while (candidateIndex < candidate.Length && char.IsAsciiDigit(candidate[candidateIndex])) { candidateIndex++; }
                if (candidateIndex == digitStart || candidateIndex >= candidate.Length || candidate[candidateIndex++] != ']') { return false; }
                patternIndex += 3;
                continue;
            }

            if (candidateIndex >= candidate.Length
                || char.ToUpperInvariant(pattern[patternIndex]) != char.ToUpperInvariant(candidate[candidateIndex]))
            {
                return false;
            }

            patternIndex++;
            candidateIndex++;
        }

        return candidateIndex == candidate.Length;
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
