using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComparisonTool.Core.Comparison.Configuration
{
    /// <summary>
    /// Processes smart ignore rules and applies them to comparisons
    /// </summary>
    public class SmartIgnoreProcessor
    {
        private readonly ILogger _logger;

        public SmartIgnoreProcessor(ILogger logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Apply smart ignore rules to comparison configuration
        /// </summary>
        public void ApplyRulesToConfig(List<SmartIgnoreRule> rules, ComparisonConfig config)
        {
            if (rules == null || !rules.Any())
                return;

            var activeRules = rules.Where(r => r.IsEnabled).ToList();
            _logger.LogInformation("Applying {Count} active smart ignore rules to configuration", activeRules.Count);

            // Handle collection ordering rules
            var collectionOrderingRule = activeRules.FirstOrDefault(r => r.Type == SmartIgnoreType.CollectionOrdering);
            if (collectionOrderingRule != null)
            {
                config.IgnoreCollectionOrder = true;
                _logger.LogDebug("Applied collection ordering rule: {Description}", collectionOrderingRule.Description);
            }

            // Note: Property-based rules will be handled during result filtering
            // as they require runtime inspection of property names and types
        }

        /// <summary>
        /// Filter comparison result based on smart ignore rules
        /// </summary>
        public ComparisonResult FilterResult(ComparisonResult result, List<SmartIgnoreRule> rules, Type modelType = null)
        {
            if (result == null || !result.Differences.Any() || rules == null)
                return result;

            var activeRules = rules.Where(r => r.IsEnabled).ToList();
            if (!activeRules.Any())
                return result;

            _logger.LogInformation("Filtering {Count} differences using {RuleCount} smart ignore rules", 
                result.Differences.Count, activeRules.Count);

            var originalCount = result.Differences.Count;
            var filteredDifferences = new List<Difference>();

            foreach (var difference in result.Differences)
            {
                if (!ShouldIgnoreDifference(difference, activeRules, modelType))
                {
                    filteredDifferences.Add(difference);
                }
                else
                {
                    _logger.LogDebug("Filtered out difference for property: {PropertyName}", difference.PropertyName);
                }
            }

            result.Differences.Clear();
            result.Differences.AddRange(filteredDifferences);

            var filteredCount = result.Differences.Count;
            var removedCount = originalCount - filteredCount;

            if (removedCount > 0)
            {
                _logger.LogInformation("Smart filtering removed {RemovedCount} differences from {OriginalCount} total (kept {FilteredCount})", 
                    removedCount, originalCount, filteredCount);
            }

            return result;
        }

        /// <summary>
        /// Check if a difference should be ignored based on smart rules
        /// </summary>
        private bool ShouldIgnoreDifference(Difference difference, List<SmartIgnoreRule> rules, Type modelType)
        {
            var propertyName = GetPropertyName(difference.PropertyName);
            var propertyType = GetPropertyType(difference, modelType);

            foreach (var rule in rules)
            {
                if (MatchesRule(propertyName, propertyType, rule))
                {
                    _logger.LogDebug("Property '{PropertyName}' matches rule: {RuleDescription}", 
                        propertyName, rule.Description);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if a property matches a specific rule
        /// </summary>
        private bool MatchesRule(string propertyName, Type propertyType, SmartIgnoreRule rule)
        {
            switch (rule.Type)
            {
                case SmartIgnoreType.PropertyName:
                    return string.Equals(propertyName, rule.Value, StringComparison.OrdinalIgnoreCase);

                case SmartIgnoreType.NamePattern:
                    return MatchesPattern(propertyName, rule.Value);

                case SmartIgnoreType.PropertyType:
                    return propertyType != null && 
                           string.Equals(propertyType.FullName, rule.Value, StringComparison.OrdinalIgnoreCase);

                case SmartIgnoreType.CollectionOrdering:
                    // Collection ordering is handled at the config level
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Check if a property name matches a wildcard pattern
        /// </summary>
        private bool MatchesPattern(string propertyName, string pattern)
        {
            if (string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(pattern))
                return false;

            try
            {
                // Convert wildcard pattern to regex
                var regexPattern = "^" + Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";

                return Regex.IsMatch(propertyName, regexPattern, RegexOptions.IgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error matching pattern '{Pattern}' against '{PropertyName}'", pattern, propertyName);
                return false;
            }
        }

        /// <summary>
        /// Extract the actual property name from a property path
        /// </summary>
        private string GetPropertyName(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
                return propertyPath;

            // Handle array indices: "Results[0].Name" -> "Name"
            // Handle nested paths: "Body.Response.Name" -> "Name"
            var parts = propertyPath.Split('.');
            var lastPart = parts.Last();

            // Remove array indices if present: "Results[0]" -> "Results"
            var match = Regex.Match(lastPart, @"^([^[\]]+)");
            return match.Success ? match.Groups[1].Value : lastPart;
        }

        /// <summary>
        /// Try to determine the property type from the difference
        /// </summary>
        private Type GetPropertyType(Difference difference, Type modelType)
        {
            try
            {
                // Use the type of the actual values
                var value = difference.Object1Value ?? difference.Object2Value;
                if (value != null)
                {
                    return value.GetType();
                }

                // TODO: Could add more sophisticated type resolution using reflection on modelType
                // For now, this simple approach should handle most cases
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get property information for a given model type (for advanced scenarios)
        /// </summary>
        public List<PropertyInfo> GetModelProperties(Type modelType)
        {
            if (modelType == null)
                return new List<PropertyInfo>();

            try
            {
                return GetAllProperties(modelType).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting properties for type {TypeName}", modelType.Name);
                return new List<PropertyInfo>();
            }
        }

        /// <summary>
        /// Recursively get all properties from a type
        /// </summary>
        private IEnumerable<PropertyInfo> GetAllProperties(Type type, HashSet<Type> visited = null, int depth = 0)
        {
            if (depth > 5 || type == null)
                yield break;

            visited ??= new HashSet<Type>();
            if (visited.Contains(type))
                yield break;

            visited.Add(type);

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                yield return prop;

                // Recursively explore complex types
                if (!IsSimpleType(prop.PropertyType) && !IsCollectionType(prop.PropertyType))
                {
                    foreach (var nestedProp in GetAllProperties(prop.PropertyType, visited, depth + 1))
                    {
                        yield return nestedProp;
                    }
                }
            }
        }

        /// <summary>
        /// Check if a type is a simple/primitive type
        /// </summary>
        private bool IsSimpleType(Type type)
        {
            return type.IsPrimitive ||
                   type == typeof(string) ||
                   type == typeof(DateTime) ||
                   type == typeof(decimal) ||
                   type == typeof(Guid) ||
                   type.IsEnum ||
                   (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>));
        }

        /// <summary>
        /// Check if a type is a collection type
        /// </summary>
        private bool IsCollectionType(Type type)
        {
            return type.IsArray ||
                   (type.IsGenericType && (
                       typeof(IEnumerable<>).IsAssignableFrom(type.GetGenericTypeDefinition()) ||
                       typeof(ICollection<>).IsAssignableFrom(type.GetGenericTypeDefinition()) ||
                       typeof(IList<>).IsAssignableFrom(type.GetGenericTypeDefinition())
                   ));
        }
    }
} 