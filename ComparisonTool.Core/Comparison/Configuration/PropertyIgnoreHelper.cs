using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComparisonTool.Core.Comparison.Configuration
{
    /// <summary>
    /// Enhanced helper for property ignore pattern matching with full support for tree navigator patterns
    /// Performance optimized with caching and reduced logging
    /// </summary>
    public static class PropertyIgnoreHelper
    {
        // Performance optimization: Cache pattern matching results
        private static readonly ConcurrentDictionary<string, bool> _patternMatchCache = new();
        private static readonly ConcurrentDictionary<string, Regex> _compiledRegexCache = new();
        
        // Performance counters for monitoring
        private static long _cacheHits = 0;
        private static long _cacheMisses = 0;
        /// <summary>
        /// Check if a property should be ignored based on configured patterns
        /// Performance optimized with caching and reduced logging
        /// </summary>
        public static bool ShouldIgnoreProperty(string propertyPath, HashSet<string> ignorePatterns, ILogger logger = null)
        {
            if (string.IsNullOrEmpty(propertyPath) || ignorePatterns == null || !ignorePatterns.Any())
                return false;

            logger ??= NullLogger.Instance;

            // Performance optimization: Check cache first
            var cacheKey = $"{propertyPath}|{string.Join(",", ignorePatterns.OrderBy(p => p))}";
            if (_patternMatchCache.TryGetValue(cacheKey, out var cachedResult))
            {
                Interlocked.Increment(ref _cacheHits);
                return cachedResult;
            }

            Interlocked.Increment(ref _cacheMisses);

            // Reduced logging - only log at debug level unless matches found
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Checking if property '{PropertyPath}' matches any of {PatternCount} ignore patterns", 
                    propertyPath, ignorePatterns.Count);
            }

            // Performance optimization: Check for exact matches first (fastest path)
            if (ignorePatterns.Contains(propertyPath))
            {
                _patternMatchCache.TryAdd(cacheKey, true);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogDebug("Property '{PropertyPath}' found in exact match - WILL BE IGNORED", propertyPath);
                }
                return true;
            }

            // Check pattern matches (more expensive)
            foreach (var pattern in ignorePatterns)
            {
                // Reduced logging for performance
                logger.LogTrace("Testing pattern '{Pattern}' against property '{PropertyPath}'", pattern, propertyPath);
                
                if (DoesPropertyMatchPattern(propertyPath, pattern, logger))
                {
                    _patternMatchCache.TryAdd(cacheKey, true);
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogDebug("Property '{PropertyPath}' MATCHES pattern '{Pattern}' - WILL BE IGNORED", propertyPath, pattern);
                    }
                    return true;
                }
            }

            _patternMatchCache.TryAdd(cacheKey, false);
            return false;
        }

        /// <summary>
        /// Get cache statistics for monitoring performance
        /// </summary>
        public static (long hits, long misses, double hitRatio) GetCacheStats()
        {
            var hits = _cacheHits;
            var misses = _cacheMisses;
            var total = hits + misses;
            var hitRatio = total > 0 ? (double)hits / total : 0.0;
            return (hits, misses, hitRatio);
        }

        /// <summary>
        /// Clear the pattern matching cache (useful for testing or memory management)
        /// </summary>
        public static void ClearCache()
        {
            _patternMatchCache.Clear();
            _compiledRegexCache.Clear();
            _cacheHits = 0;
            _cacheMisses = 0;
        }

        /// <summary>
        /// Check if a property path matches a specific ignore pattern
        /// </summary>
        private static bool DoesPropertyMatchPattern(string propertyPath, string pattern, ILogger logger)
        {
            if (string.IsNullOrEmpty(propertyPath) || string.IsNullOrEmpty(pattern))
                return false;

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
        /// Performance optimized with compiled regex caching
        /// </summary>
        private static bool MatchesCollectionPattern(string propertyPath, string pattern, ILogger logger)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Checking collection pattern '{Pattern}' against '{PropertyPath}'", pattern, propertyPath);
            }

            // Performance optimization: Get or create compiled regex
            var regex = _compiledRegexCache.GetOrAdd(pattern, p => 
            {
                // Convert the pattern to a regex
                // Pattern with [*] should match both exact paths and sub-properties:
                // - Collection[0].Property (exact)
                // - Collection[1].Property (exact)  
                // - Collection[2].Property.SubProperty (sub-property)

                // First replace [*] with a placeholder before escaping
                string tempPattern = p.Replace("[*]", "COLLECTION_INDEX_PLACEHOLDER");
                
                // Escape regex special characters 
                string regexPattern = Regex.Escape(tempPattern);
                
                // Replace placeholder with regex for any collection index
                regexPattern = regexPattern.Replace("COLLECTION_INDEX_PLACEHOLDER", @"\[\d+\]");
                
                // Add pattern for sub-properties: allow the pattern to match as prefix
                regexPattern = $"^{regexPattern}($|\\.)";

                // DEBUG: Log the generated regex pattern
                logger.LogWarning("DEBUG: Generated regex pattern '{RegexPattern}' for ignore pattern '{IgnorePattern}'", 
                    regexPattern, p);

                return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            });

            bool matches = regex.IsMatch(propertyPath);
            
            // DEBUG: Always log pattern matching attempts for debugging
                            logger.LogTrace("Pattern matching - PropertyPath: '{PropertyPath}' | Pattern: '{Pattern}' | Matches: {Matches}", 
                propertyPath, pattern, matches);
            
            if (matches && logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Property '{PropertyPath}' MATCHES collection pattern '{Pattern}'", propertyPath, pattern);
            }

            return matches;
        }

        /// <summary>
        /// Match wildcard patterns with * notation
        /// Performance optimized with compiled regex caching
        /// </summary>
        private static bool MatchesWildcardPattern(string propertyPath, string pattern, ILogger logger)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Checking wildcard pattern '{Pattern}' against '{PropertyPath}'", pattern, propertyPath);
            }

            // Performance optimization: Get or create compiled regex
            var regex = _compiledRegexCache.GetOrAdd($"wildcard:{pattern}", p => 
            {
                // Convert wildcard pattern to regex
                string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "($|\\.)";
                
                return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            });

            bool matches = regex.IsMatch(propertyPath);
            
            if (matches && logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Property '{PropertyPath}' MATCHES wildcard pattern '{Pattern}'", propertyPath, pattern);
            }

            return matches;
        }

        /// <summary>
        /// Generate all possible concrete variations of a collection pattern for debugging
        /// </summary>
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
            for (int i = 0; i < maxIndex; i++)
            {
                variations.Add(pattern.Replace("[*]", $"[{i}]"));
            }

            return variations;
        }

        /// <summary>
        /// Test a property path against multiple patterns for debugging
        /// </summary>
        public static Dictionary<string, bool> TestPropertyAgainstPatterns(string propertyPath, 
            IEnumerable<string> patterns, ILogger logger = null)
        {
            logger ??= NullLogger.Instance;
            var results = new Dictionary<string, bool>();

            foreach (var pattern in patterns)
            {
                bool matches = DoesPropertyMatchPattern(propertyPath, pattern, logger);
                results[pattern] = matches;
                
                logger.LogInformation("Property '{PropertyPath}' vs Pattern '{Pattern}': {Result}", 
                    propertyPath, pattern, matches ? "MATCH" : "NO MATCH");
            }

            return results;
        }


    }
} 