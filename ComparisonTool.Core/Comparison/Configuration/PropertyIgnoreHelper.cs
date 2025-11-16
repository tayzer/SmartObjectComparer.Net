// <copyright file="PropertyIgnoreHelper.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Configuration
{
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
        private static readonly ConcurrentDictionary<string, bool> patternMatchCache = new ();
        private static readonly ConcurrentDictionary<string, Regex> compiledRegexCache = new ();

        // Performance counters for monitoring
        private static long cacheHits = 0;
        private static long cacheMisses = 0;

        /// <summary>
        /// Check if a property should be ignored based on configured patterns
        /// Performance optimized with caching and reduced logging.
        /// </summary>
        /// <returns></returns>
        public static bool ShouldIgnoreProperty(string propertyPath, HashSet<string> ignorePatterns, ILogger logger = null)
        {
            if (string.IsNullOrEmpty(propertyPath) || ignorePatterns == null || !ignorePatterns.Any()) {
                return false;
            }

            logger ??= NullLogger.Instance;

            // Performance optimization: Check cache first
            var cacheKey = $"{propertyPath}|{string.Join(",", ignorePatterns.OrderBy(p => p))}";
            if (patternMatchCache.TryGetValue(cacheKey, out var cachedResult))
            {
                Interlocked.Increment(ref cacheHits);
                return cachedResult;
            }

            Interlocked.Increment(ref cacheMisses);

            // Performance optimization: Skip expensive logging operations in hot path
            // Only log for debugging when explicitly enabled
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace(
                    "Checking if property '{PropertyPath}' matches any of {PatternCount} ignore patterns",
                    propertyPath, ignorePatterns.Count);
            }

            // Performance optimization: Check for exact matches first (fastest path)
            if (ignorePatterns.Contains(propertyPath))
            {
                patternMatchCache.TryAdd(cacheKey, true);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogDebug("Property '{PropertyPath}' found in exact match - WILL BE IGNORED", propertyPath);
                }

                return true;
            }

            // Check pattern matches (more expensive)
            foreach (var pattern in ignorePatterns)
            {
                // Performance: Only log in trace mode to avoid overhead in production
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("Testing pattern '{Pattern}' against property '{PropertyPath}'", pattern, propertyPath);
                }

                if (DoesPropertyMatchPattern(propertyPath, pattern, logger))
                {
                    patternMatchCache.TryAdd(cacheKey, true);
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogDebug("Property '{PropertyPath}' MATCHES pattern '{Pattern}' - WILL BE IGNORED", propertyPath, pattern);
                    }

                    return true;
                }
            }

            patternMatchCache.TryAdd(cacheKey, false);
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
            patternMatchCache.Clear();
            compiledRegexCache.Clear();
            cacheHits = 0;
            cacheMisses = 0;
        }

        /// <summary>
        /// Check if a property path matches a specific ignore pattern.
        /// </summary>
        private static bool DoesPropertyMatchPattern(string propertyPath, string pattern, ILogger logger)
        {
            if (string.IsNullOrEmpty(propertyPath) || string.IsNullOrEmpty(pattern)) {
                return false;
            }

            try
            {
                // Handle exact matches first
                if (string.Equals(propertyPath, pattern, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug("Exact match: '{PropertyPath}' == '{Pattern}'", propertyPath, pattern);
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
                    logger.LogDebug("Prefix match: '{PropertyPath}' starts with '{Pattern}.'", propertyPath, pattern);
                    return true;
                }

                // Handle collection-level ignores: pattern "Collection" should match "Collection[0].Property"
                if (propertyPath.StartsWith(pattern + "[", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug("Collection prefix match: '{PropertyPath}' starts with collection pattern '{Pattern}['", propertyPath, pattern);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error matching property '{PropertyPath}' against pattern '{Pattern}'",
                    propertyPath, pattern);
                return false;
            }
        }

        /// <summary>
        /// Match collection patterns with [*] notation from tree navigator
        /// Performance optimized with compiled regex caching.
        /// </summary>
        private static bool MatchesCollectionPattern(string propertyPath, string pattern, ILogger logger)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Checking collection pattern '{Pattern}' against '{PropertyPath}'", pattern, propertyPath);
            }

            // Performance optimization: Get or create compiled regex
            var regex = compiledRegexCache.GetOrAdd(pattern, p =>
            {
                // Convert the pattern to a regex
                // Pattern with [*] should match both exact paths and sub-properties:
                // - Collection[0].Property (exact)
                // - Collection[1].Property (exact)
                // - Collection[2].Property.SubProperty (sub-property)

                // First replace [*] with a placeholder before escaping
                var tempPattern = p.Replace("[*]", "COLLECTION_INDEX_PLACEHOLDER");

                // Escape regex special characters
                var regexPattern = Regex.Escape(tempPattern);

                // Replace placeholder with regex for any collection index
                regexPattern = regexPattern.Replace("COLLECTION_INDEX_PLACEHOLDER", @"\[\d+\]");

                // PRECISION FIX: Match EXACT property only, not sub-properties
                // This prevents "CallCount" pattern from matching "ComponentName"
                regexPattern = $"^{regexPattern}$";

                // PRECISION DEBUG: Log the generated regex pattern
                logger.LogDebug(
                    "Generated EXACT regex '{RegexPattern}' for pattern '{IgnorePattern}'",
                    regexPattern, p);

                return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            });

            var matches = regex.IsMatch(propertyPath);

            // PRECISION DEBUG: Log the exact match result
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Pattern '{Pattern}' vs Property '{PropertyPath}': {MatchResult}",
                    pattern, propertyPath, matches ? "MATCH (WILL IGNORE)" : "NO MATCH");
            }

            // DEBUG: Always log pattern matching attempts for debugging
            logger.LogTrace(
                "Pattern matching - PropertyPath: '{PropertyPath}' | Pattern: '{Pattern}' | Matches: {Matches}",
propertyPath, pattern, matches);

            if (matches && logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Property '{PropertyPath}' MATCHES collection pattern '{Pattern}'", propertyPath, pattern);
            }

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
                logger.LogDebug("Checking wildcard pattern '{Pattern}' against '{PropertyPath}'", pattern, propertyPath);
            }

            // Performance optimization: Get or create compiled regex
            var regex = compiledRegexCache.GetOrAdd($"wildcard:{pattern}", p =>
            {
                // Convert wildcard pattern to regex
                var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "($|\\.)";

                return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            });

            var matches = regex.IsMatch(propertyPath);

            if (matches && logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Property '{PropertyPath}' MATCHES wildcard pattern '{Pattern}'", propertyPath, pattern);
            }

            return matches;
        }

        /// <summary>
        /// Generate all possible concrete variations of a collection pattern for debugging.
        /// </summary>
        /// <returns></returns>
        public static List<string> GenerateCollectionVariations(string pattern, int maxIndex = 10)
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
        public static Dictionary<string, bool> TestPropertyAgainstPatterns(
            string propertyPath,
            IEnumerable<string> patterns, ILogger logger = null)
            {
            logger ??= NullLogger.Instance;
            var results = new Dictionary<string, bool>();

            foreach (var pattern in patterns)
            {
                var matches = DoesPropertyMatchPattern(propertyPath, pattern, logger);
                results[pattern] = matches;

                logger.LogInformation(
                    "Property '{PropertyPath}' vs Pattern '{Pattern}': {Result}",
                    propertyPath, pattern, matches ? "MATCH" : "NO MATCH");
            }

            return results;
        }
    }
}
