// <copyright file="PropertyIgnoreHelper.cs" company="PlaceholderCompany">



namespace ComparisonTool.Core.Comparison.Configuration;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Enhanced helper for property ignore pattern matching with full support for tree navigator patterns
/// Performance optimized with caching and reduced logging.
/// </summary>
public static class PropertyIgnoreHelper
{
    // Performance optimization: Cache pattern matching results
    private static readonly ConcurrentDictionary<string, bool> PatternMatchCache = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Regex> CompiledRegexCache = new ConcurrentDictionary<string, Regex>(StringComparer.Ordinal);
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Performance counters for monitoring
    private static long cacheHits = 0;
    private static long cacheMisses = 0;

    /// <summary>
    /// Check if a property should be ignored based on configured patterns
    /// Performance optimized with caching and reduced logging.
    /// </summary>
    /// <returns></returns>
    public static bool ShouldIgnoreProperty(string propertyPath, IReadOnlyCollection<string> ignorePatterns, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(propertyPath) || ignorePatterns == null || ignorePatterns.Count == 0)
        {
            return false;
        }

        logger ??= NullLogger.Instance;

        // Performance optimization: Check cache first
        var cacheKey = CreateCacheKey(propertyPath, ignorePatterns);
        if (TryGetCachedResult(cacheKey, out var cachedResult))
        {
            return cachedResult;
        }

        Interlocked.Increment(ref cacheMisses);
        LogPropertyCheck(logger, propertyPath, ignorePatterns.Count);

        if (IsExactMatch(propertyPath, ignorePatterns, logger))
        {
            PatternMatchCache.TryAdd(cacheKey, true);
            return true;
        }

        if (IsPatternMatch(propertyPath, ignorePatterns, logger))
        {
            PatternMatchCache.TryAdd(cacheKey, true);
            return true;
        }

        PatternMatchCache.TryAdd(cacheKey, false);
        return false;
    }

    /// <summary>
    /// Get cache statistics for monitoring performance.
    /// </summary>
    /// <returns></returns>
    public static (long hits, long misses, double hitRatio) GetCacheStats()
    {
        var hits = cacheHits;
        var misses = cacheMisses;
        var total = hits + misses;
        var hitRatio = total > 0 ? (double)hits / total : 0.0;
        return (hits, misses, hitRatio);
    }

    /// <summary>
    /// Clear the pattern matching cache (useful for testing or memory management).
    /// </summary>
    public static void ClearCache()
    {
        PatternMatchCache.Clear();
        CompiledRegexCache.Clear();
        cacheHits = 0;
        cacheMisses = 0;
    }

    /// <summary>
    /// Generate all possible concrete variations of a collection pattern for debugging.
    /// </summary>
    /// <returns></returns>
    public static IReadOnlyList<string> GenerateCollectionVariations(string pattern, int maxIndex = 10)
    {
        var variations = new List<string>();

        if (!pattern.Contains("[*]"))
        {
            variations.Add(pattern);
            return variations;
        }

        // Add the original pattern
        variations.Add(pattern);

        // Generate numbered variations
        for (var i = 0; i < maxIndex; i++)
        {
            variations.Add(pattern.Replace("[*]", $"[{i}]"));
        }

        return variations;
    }

    /// <summary>
    /// Test a property path against multiple patterns for debugging.
    /// </summary>
    /// <returns></returns>
    public static IReadOnlyDictionary<string, bool> TestPropertyAgainstPatterns(
        string propertyPath,
        IEnumerable<string> patterns,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var results = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var pattern in patterns)
        {
            var matches = DoesPropertyMatchPattern(propertyPath, pattern, logger);
            results[pattern] = matches;

            logger.LogInformation(
                "Property '{PropertyPath}' vs Pattern '{Pattern}': {Result}",
                propertyPath,
                pattern,
                matches ? "MATCH" : "NO MATCH");
        }

        return results;
    }

    /// <summary>
    /// Check if a property path matches a specific ignore pattern.
    /// </summary>
    private static bool DoesPropertyMatchPattern(string propertyPath, string pattern, ILogger logger)
    {
        if (string.IsNullOrEmpty(propertyPath) || string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        try
        {
            // Handle exact matches first
            if (string.Equals(propertyPath, pattern, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug(
                    "Exact match: '{PropertyPath}' == '{Pattern}'",
                    propertyPath,
                    pattern);
                return true;
            }

            // Handle [*] collection notation patterns
            if (pattern.Contains("[*]"))
            {
                return MatchesCollectionPattern(propertyPath, pattern, logger);
            }

            // Handle wildcard patterns (for backwards compatibility)
            if (pattern.Contains("*"))
            {
                return MatchesWildcardPattern(propertyPath, pattern, logger);
            }

            // Handle prefix matching for sub-properties
            // e.g., pattern "Body.Response" should match "Body.Response.Something"
            if (propertyPath.StartsWith(pattern + ".", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug(
                    "Prefix match: '{PropertyPath}' starts with '{Pattern}.'",
                    propertyPath,
                    pattern);
                return true;
            }

            // Handle collection-level ignores: pattern "Collection" should match "Collection[0].Property"
            if (propertyPath.StartsWith(pattern + "[", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug(
                    "Collection prefix match: '{PropertyPath}' starts with collection pattern '{Pattern}['",
                    propertyPath,
                    pattern);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Error matching property '{PropertyPath}' against pattern '{Pattern}'",
                propertyPath,
                pattern);
            return false;
        }
    }

    /// <summary>
    /// Match collection patterns with [*] notation from tree navigator
    /// Performance optimized with compiled regex caching.
    /// </summary>
    private static bool MatchesCollectionPattern(string propertyPath, string pattern, ILogger logger)
    {
        LogCollectionPatternCheck(logger, pattern, propertyPath);

        var regex = GetOrCreateCollectionRegex(pattern, logger);

        var matches = regex.IsMatch(propertyPath);

        LogCollectionPatternResult(logger, pattern, propertyPath, matches);

        return matches;
    }

    /// <summary>
    /// Match wildcard patterns with * notation
    /// Performance optimized with compiled regex caching.
    /// </summary>
    private static bool MatchesWildcardPattern(string propertyPath, string pattern, ILogger logger)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Checking wildcard pattern '{Pattern}' against '{PropertyPath}'",
                pattern,
                propertyPath);
        }

        // Performance optimization: Get or create compiled regex
        var regex = CompiledRegexCache.GetOrAdd($"wildcard:{pattern}", p =>
        {
            // Convert wildcard pattern to regex
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "($|\\.)";

            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture, RegexTimeout);
        });

        var matches = regex.IsMatch(propertyPath);

        if (matches && logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Property '{PropertyPath}' MATCHES wildcard pattern '{Pattern}'",
                propertyPath,
                pattern);
        }

        return matches;
    }

    private static string CreateCacheKey(string propertyPath, IEnumerable<string> ignorePatterns) => $"{propertyPath}|{string.Join(",", ignorePatterns.OrderBy(p => p, StringComparer.Ordinal))}";

    private static bool TryGetCachedResult(string cacheKey, out bool cachedResult)
    {
        if (PatternMatchCache.TryGetValue(cacheKey, out cachedResult))
        {
            Interlocked.Increment(ref cacheHits);
            return true;
        }

        return false;
    }

    private static void LogPropertyCheck(ILogger logger, string propertyPath, int patternCount)
    {
        if (!logger.IsEnabled(LogLevel.Trace))
        {
            return;
        }

        logger.LogTrace(
            "Checking if property '{PropertyPath}' matches any of {PatternCount} ignore patterns",
            propertyPath,
            patternCount);
    }

    private static bool IsExactMatch(string propertyPath, IReadOnlyCollection<string> ignorePatterns, ILogger logger)
    {
        if (!ignorePatterns.Contains(propertyPath, StringComparer.Ordinal))
        {
            return false;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogDebug(
                "Property '{PropertyPath}' found in exact match - WILL BE IGNORED",
                propertyPath);
        }

        return true;
    }

    private static bool IsPatternMatch(string propertyPath, IEnumerable<string> ignorePatterns, ILogger logger)
    {
        foreach (var pattern in ignorePatterns)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace(
                    "Testing pattern '{Pattern}' against property '{PropertyPath}'",
                    pattern,
                    propertyPath);
            }

            if (DoesPropertyMatchPattern(propertyPath, pattern, logger))
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogDebug(
                        "Property '{PropertyPath}' MATCHES pattern '{Pattern}' - WILL BE IGNORED",
                        propertyPath,
                        pattern);
                }

                return true;
            }
        }

        return false;
    }

    private static void LogCollectionPatternCheck(ILogger logger, string pattern, string propertyPath)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        logger.LogDebug(
            "Checking collection pattern '{Pattern}' against '{PropertyPath}'",
            pattern,
            propertyPath);
    }

    private static Regex GetOrCreateCollectionRegex(string pattern, ILogger logger) =>
        CompiledRegexCache.GetOrAdd(pattern, p =>
        {
            var regexPattern = BuildCollectionRegexPattern(p);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Generated EXACT regex '{RegexPattern}' for pattern '{IgnorePattern}'",
                    regexPattern,
                    p);
            }

            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture, RegexTimeout);
        });

    private static string BuildCollectionRegexPattern(string pattern)
    {
        var tempPattern = pattern.Replace("[*]", "COLLECTION_INDEX_PLACEHOLDER");
        var regexPattern = Regex.Escape(tempPattern);
        regexPattern = regexPattern.Replace("COLLECTION_INDEX_PLACEHOLDER", @"\[\d+\]");

        return $"^{regexPattern}$";
    }

    private static void LogCollectionPatternResult(ILogger logger, string pattern, string propertyPath, bool matches)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Pattern '{Pattern}' vs Property '{PropertyPath}': {MatchResult}",
                pattern,
                propertyPath,
                matches ? "MATCH (WILL IGNORE)" : "NO MATCH");
        }

        logger.LogTrace(
            "Pattern matching - PropertyPath: '{PropertyPath}' | Pattern: '{Pattern}' | Matches: {Matches}",
            propertyPath,
            pattern,
            matches);

        if (matches && logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Property '{PropertyPath}' MATCHES collection pattern '{Pattern}'",
                propertyPath,
                pattern);
        }
    }
}
