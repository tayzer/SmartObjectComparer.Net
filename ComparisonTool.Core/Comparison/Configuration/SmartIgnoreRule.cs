using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComparisonTool.Core.Comparison.Configuration
{
    /// <summary>
    /// Smart ignore rule that works across any domain model
    /// </summary>
    public class SmartIgnoreRule
    {
        [JsonIgnore]
        private readonly ILogger _logger;

        /// <summary>
        /// Type of ignore rule
        /// </summary>
        public SmartIgnoreType Type { get; set; }

        /// <summary>
        /// Value associated with the rule (property name, type name, pattern, etc.)
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Whether this rule is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Description of what this rule does
        /// </summary>
        public string Description { get; set; } = string.Empty;

        [JsonConstructor]
        public SmartIgnoreRule()
        {
            _logger = NullLogger.Instance;
        }

        public SmartIgnoreRule(ILogger logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Create a rule to ignore properties by exact name
        /// </summary>
        public static SmartIgnoreRule ByPropertyName(string propertyName, string description = null)
        {
            return new SmartIgnoreRule
            {
                Type = SmartIgnoreType.PropertyName,
                Value = propertyName,
                Description = description ?? $"Ignore all '{propertyName}' properties"
            };
        }

        /// <summary>
        /// Create a rule to ignore properties by name pattern (supports wildcards)
        /// </summary>
        public static SmartIgnoreRule ByNamePattern(string pattern, string description = null)
        {
            return new SmartIgnoreRule
            {
                Type = SmartIgnoreType.NamePattern,
                Value = pattern,
                Description = description ?? $"Ignore properties matching '{pattern}'"
            };
        }

        /// <summary>
        /// Create a rule to ignore properties by type
        /// </summary>
        public static SmartIgnoreRule ByPropertyType(Type type, string description = null)
        {
            return new SmartIgnoreRule
            {
                Type = SmartIgnoreType.PropertyType,
                Value = type.FullName,
                Description = description ?? $"Ignore all {type.Name} properties"
            };
        }

        /// <summary>
        /// Create a rule to ignore collection ordering
        /// </summary>
        public static SmartIgnoreRule IgnoreCollectionOrdering(string description = "Ignore collection item ordering")
        {
            return new SmartIgnoreRule
            {
                Type = SmartIgnoreType.CollectionOrdering,
                Value = "true",
                Description = description
            };
        }
    }

    /// <summary>
    /// Types of smart ignore rules
    /// </summary>
    public enum SmartIgnoreType
    {
        /// <summary>
        /// Ignore properties with exact name match
        /// </summary>
        PropertyName,

        /// <summary>
        /// Ignore properties matching a name pattern (with wildcards)
        /// </summary>
        NamePattern,

        /// <summary>
        /// Ignore properties of a specific type
        /// </summary>
        PropertyType,

        /// <summary>
        /// Ignore collection ordering globally
        /// </summary>
        CollectionOrdering
    }

    /// <summary>
    /// Predefined ignore rule presets for common scenarios
    /// </summary>
    public static class SmartIgnorePresets
    {
        /// <summary>
        /// Common ID and key fields
        /// </summary>
        public static List<SmartIgnoreRule> IgnoreIdFields => new()
        {
            SmartIgnoreRule.ByPropertyName("Id", "Ignore ID fields"),
            SmartIgnoreRule.ByPropertyName("Guid", "Ignore GUID fields"),
            SmartIgnoreRule.ByPropertyName("Key", "Ignore key fields"),
            SmartIgnoreRule.ByNamePattern("*Id", "Ignore properties ending with 'Id'"),
            SmartIgnoreRule.ByNamePattern("*Guid", "Ignore properties ending with 'Guid'"),
            SmartIgnoreRule.ByNamePattern("*Key", "Ignore properties ending with 'Key'")
        };

        /// <summary>
        /// Common timestamp and audit fields
        /// </summary>
        public static List<SmartIgnoreRule> IgnoreTimestamps => new()
        {
            SmartIgnoreRule.ByPropertyType(typeof(DateTime), "Ignore DateTime fields"),
            SmartIgnoreRule.ByPropertyName("Timestamp", "Ignore timestamp fields"),
            SmartIgnoreRule.ByPropertyName("CreatedDate", "Ignore creation timestamps"),
            SmartIgnoreRule.ByPropertyName("ModifiedDate", "Ignore modification timestamps"),
            SmartIgnoreRule.ByPropertyName("UpdatedDate", "Ignore update timestamps"),
            SmartIgnoreRule.ByPropertyName("LastModified", "Ignore last modified timestamps"),
            SmartIgnoreRule.ByNamePattern("*Date", "Ignore properties ending with 'Date'"),
            SmartIgnoreRule.ByNamePattern("*Time", "Ignore properties ending with 'Time'"),
            SmartIgnoreRule.ByNamePattern("*Timestamp", "Ignore properties ending with 'Timestamp'")
        };

        /// <summary>
        /// System metadata fields
        /// </summary>
        public static List<SmartIgnoreRule> IgnoreMetadata => new()
        {
            SmartIgnoreRule.ByPropertyName("Version", "Ignore version fields"),
            SmartIgnoreRule.ByPropertyName("ETag", "Ignore ETag fields"),
            SmartIgnoreRule.ByPropertyName("RequestId", "Ignore request ID fields"),
            SmartIgnoreRule.ByPropertyName("SessionId", "Ignore session ID fields"),
            SmartIgnoreRule.ByPropertyName("CorrelationId", "Ignore correlation ID fields"),
            SmartIgnoreRule.ByNamePattern("*Version", "Ignore properties ending with 'Version'"),
            SmartIgnoreRule.ByNamePattern("*Etag", "Ignore properties ending with 'Etag'"),
            SmartIgnoreRule.ByNamePattern("Request*", "Ignore properties starting with 'Request'")
        };

        /// <summary>
        /// Complete functional comparison preset (ignores technical fields, focuses on business data)
        /// </summary>
        public static List<SmartIgnoreRule> FunctionalComparison => new List<SmartIgnoreRule>
        {
            SmartIgnoreRule.IgnoreCollectionOrdering("Collections can be in any order")
        }
        .Concat(IgnoreIdFields)
        .Concat(IgnoreTimestamps)
        .Concat(IgnoreMetadata)
        .ToList();

        /// <summary>
        /// Get all available presets
        /// </summary>
        public static Dictionary<string, List<SmartIgnoreRule>> AllPresets => new()
        {
            { "ID Fields", IgnoreIdFields },
            { "Timestamps", IgnoreTimestamps },
            { "Metadata", IgnoreMetadata },
            { "Functional Comparison", FunctionalComparison }
        };
    }
} 