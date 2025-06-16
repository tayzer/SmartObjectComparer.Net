using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComparisonTool.Core.Comparison.Configuration
{
    /// <summary>
    /// Enhanced helper for property ignore pattern matching with full support for tree navigator patterns
    /// </summary>
    public static class PropertyIgnoreHelper
    {
        /// <summary>
        /// Check if a property should be ignored based on configured patterns
        /// </summary>
        public static bool ShouldIgnoreProperty(string propertyPath, HashSet<string> ignorePatterns, ILogger logger = null)
        {
            if (string.IsNullOrEmpty(propertyPath) || ignorePatterns == null || !ignorePatterns.Any())
                return false;

            logger ??= NullLogger.Instance;

            logger.LogInformation("🎯 Checking if property '{PropertyPath}' matches any of {PatternCount} ignore patterns: [{Patterns}]", 
                propertyPath, ignorePatterns.Count, string.Join(", ", ignorePatterns));

            foreach (var pattern in ignorePatterns)
            {
                logger.LogInformation("🔍 Testing pattern: '{Pattern}' against property: '{PropertyPath}'", pattern, propertyPath);
                
                if (DoesPropertyMatchPattern(propertyPath, pattern, logger))
                {
                    logger.LogWarning("✅ MATCH FOUND! Property '{PropertyPath}' MATCHES pattern '{Pattern}' - WILL BE IGNORED", propertyPath, pattern);
                    return true;
                }
                else
                {
                    logger.LogInformation("❌ No match for pattern: '{Pattern}'", pattern);
                }
            }

            logger.LogWarning("❌ Property '{PropertyPath}' does NOT match any ignore patterns - WILL BE KEPT", propertyPath);
            return false;
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
        /// </summary>
        private static bool MatchesCollectionPattern(string propertyPath, string pattern, ILogger logger)
        {
            logger.LogInformation("🔍 Checking collection pattern '{Pattern}' against '{PropertyPath}'", pattern, propertyPath);

            // Convert the pattern to a regex
            // Pattern with [*] should match both exact paths and sub-properties:
            // - Collection[0].Property (exact)
            // - Collection[1].Property (exact)  
            // - Collection[2].Property.SubProperty (sub-property)

            // First replace [*] with a placeholder before escaping
            string tempPattern = pattern.Replace("[*]", "COLLECTION_INDEX_PLACEHOLDER");
            
            // Escape regex special characters 
            string regexPattern = Regex.Escape(tempPattern);
            logger.LogInformation("📝 After escaping: '{RegexPattern}'", regexPattern);
            
            // Replace placeholder with regex for any collection index
            regexPattern = regexPattern.Replace("COLLECTION_INDEX_PLACEHOLDER", @"\[\d+\]");
            logger.LogInformation("🔄 After [*] replacement: '{RegexPattern}'", regexPattern);
            
            // Add pattern for sub-properties: allow the pattern to match as prefix
            regexPattern = $"^{regexPattern}($|\\.)";
            logger.LogInformation("✅ Final regex pattern: '{RegexPattern}'", regexPattern);

            bool matches = Regex.IsMatch(propertyPath, regexPattern, RegexOptions.IgnoreCase);
            
            if (matches)
            {
                logger.LogWarning("✅ Property '{PropertyPath}' MATCHES collection pattern '{Pattern}' - SHOULD BE IGNORED", propertyPath, pattern);
            }
            else
            {
                logger.LogWarning("❌ Property '{PropertyPath}' does NOT match collection pattern '{Pattern}'", propertyPath, pattern);
            }

            return matches;
        }

        /// <summary>
        /// Match wildcard patterns with * notation
        /// </summary>
        private static bool MatchesWildcardPattern(string propertyPath, string pattern, ILogger logger)
        {
            logger.LogDebug("Checking wildcard pattern '{Pattern}' against '{PropertyPath}'", pattern, propertyPath);

            // Convert wildcard pattern to regex
            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "($|\\.)";
            
            logger.LogDebug("Converted wildcard pattern '{Pattern}' to regex '{RegexPattern}'", pattern, regexPattern);

            bool matches = Regex.IsMatch(propertyPath, regexPattern, RegexOptions.IgnoreCase);
            
            if (matches)
            {
                logger.LogDebug("Property '{PropertyPath}' MATCHES wildcard pattern '{Pattern}'", propertyPath, pattern);
            }
            else
            {
                logger.LogDebug("Property '{PropertyPath}' does NOT match wildcard pattern '{Pattern}'", propertyPath, pattern);
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