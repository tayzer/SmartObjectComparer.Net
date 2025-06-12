using KellermanSoftware.CompareNetObjects;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Text.Json.Serialization;
using System.Linq;

namespace ComparisonTool.Core.Comparison.Configuration
{
    /// <summary>
    /// Represents a rule for ignoring or configuring comparison for a specific property
    /// </summary>
    public class IgnoreRule
    {
        // NOTE: The logger field is problematic for simple JSON serialization/deserialization
        // as ILogger cannot be easily represented in JSON. 
        // We use NullLogger.Instance as a fallback.
        [JsonIgnore] // Tell serializer to ignore this field completely
        private readonly ILogger _logger;

        /// <summary>
        /// Path to the property (e.g., "Body.Response.Results[0].Name")
        /// </summary>
        public string PropertyPath { get; set; }

        /// <summary>
        /// Whether to completely ignore this property during comparison
        /// </summary>
        public bool IgnoreCompletely { get; set; } = false;

        /// <summary>
        /// For collections, whether to ignore the order of items
        /// </summary>
        public bool IgnoreCollectionOrder { get; set; } = false;
        
        // Parameterless constructor explicitly marked for JSON deserialization
        [JsonConstructor] 
        public IgnoreRule()
        {
            this._logger = NullLogger.Instance; // Assign a default logger
        }
        
        /// <summary>
        /// Constructor for programmatic creation with logger
        /// </summary>
        public IgnoreRule(ILogger logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Applies this rule to the comparison configuration
        /// </summary>
        public void ApplyTo(ComparisonConfig config)
        {
            if (string.IsNullOrEmpty(PropertyPath)) return;

            if (IgnoreCompletely)
            {
                _logger.LogWarning("=== APPLYING IGNORE RULE FOR: {PropertyPath} ===", PropertyPath);
                
                // Normalize the property path to handle variations in XML deserialization
                string normalizedPath = NormalizePropertyPath(PropertyPath);
                
                _logger.LogDebug("Applying complete ignore for property: {PropertyPath} (normalized: {NormalizedPath})", 
                    PropertyPath, normalizedPath);
                
                // Add the property to be ignored
                if (!config.MembersToIgnore.Contains(PropertyPath)) 
                {
                    config.MembersToIgnore.Add(PropertyPath);
                    _logger.LogWarning("Added original path to ignore: {PropertyPath}", PropertyPath);
                }
                
                // If the normalized path is different, add it too
                if (normalizedPath != PropertyPath && !config.MembersToIgnore.Contains(normalizedPath))
                {
                    config.MembersToIgnore.Add(normalizedPath);
                    _logger.LogWarning("Added normalized path to ignore: {NormalizedPath}", normalizedPath);
                }

                // Also add variations for collection items
                AddCollectionVariations(config, PropertyPath);
                
                // Add variations by removing path segments from the beginning (dynamic prefix removal)
                // This makes it domain-agnostic by working with any path structure
                AddDynamicPrefixVariations(config, PropertyPath);
                
                _logger.LogWarning("=== FINAL IGNORE LIST CONTAINS {Count} ENTRIES ===", config.MembersToIgnore.Count);
                foreach (var ignorePath in config.MembersToIgnore.OrderBy(p => p))
                {
                    _logger.LogWarning("  -> {IgnorePath}", ignorePath);
                }
            }
            else
            {
                // If not ignoring completely, ensure it's NOT in MembersToIgnore
                if (config.MembersToIgnore.Contains(PropertyPath))
                    config.MembersToIgnore.Remove(PropertyPath);
                
                // Property-specific collection order is handled by PropertySpecificCollectionOrderComparer
                if (IgnoreCollectionOrder)
                {
                    _logger.LogWarning("[IMPORTANT] Property {PropertyPath} has collection order ignoring enabled - this is handled ONLY by PropertySpecificCollectionOrderComparer", PropertyPath);
                    // No direct config change here; the custom comparer handles it.
                }
            }
        }

        /// <summary>
        /// Normalize a property path to handle variations in XML deserialization
        /// </summary>
        private string NormalizePropertyPath(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
                return propertyPath;
                
            // Replace numeric array indices with [*]
            var normalizedPath = Regex.Replace(propertyPath, @"\[\d+\]", "[*]");
            
            // Remove any XML namespace prefixes (like soap:)
            normalizedPath = Regex.Replace(normalizedPath, @"\w+:", "");
            
            return normalizedPath;
        }

        /// <summary>
        /// Add variations of the property path to handle collection items
        /// </summary>
        private void AddCollectionVariations(ComparisonConfig config, string propertyPath)
        {
            try
            {
                _logger.LogDebug("Creating collection variations for: {PropertyPath}", propertyPath);

                // Handle paths that already contain [*] - convert to numbered indices
                if (propertyPath.Contains("[*]"))
                {
                    _logger.LogDebug("Path contains [*], creating numbered variations");
                    
                    // Create numbered index variations (0-19 to cover more cases)
                    for (int idx = 0; idx < 20; idx++)
                    {
                        string numberedPath = propertyPath.Replace("[*]", $"[{idx}]");
                        if (!config.MembersToIgnore.Contains(numberedPath))
                        {
                            config.MembersToIgnore.Add(numberedPath);
                            _logger.LogDebug("Added numbered variation: {Path}", numberedPath);
                        }
                    }

                    // Add variations without dynamic prefixes (domain-agnostic)
                    AddDynamicPrefixVariationsForPattern(config, propertyPath);

                    // Add pattern-based variations
                    AddPatternBasedVariations(config, propertyPath);
                    
                    // Add System.Collections.IList.Item[*] variations for comparison library collection handling
                    AddSystemCollectionVariations(config, propertyPath);
                }
                else
                {
                    // Handle paths without [*] - use the original complex logic
                    AddComplexCollectionVariations(config, propertyPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding collection variations for {PropertyPath}", propertyPath);
            }
        }

        /// <summary>
        /// Add pattern-based variations for property paths
        /// </summary>
        private void AddPatternBasedVariations(ComparisonConfig config, string propertyPath)
        {
            // Add regex-style patterns that the comparison engine might recognize
            var patterns = new[]
            {
                propertyPath.Replace("[*]", @"\[\d+\]"),  // Regex pattern
                propertyPath.Replace("[*]", "*"),         // Simple wildcard
                propertyPath.Replace("[*]", ".*"),        // Regex wildcard
            };

            foreach (var pattern in patterns)
            {
                if (!config.MembersToIgnore.Contains(pattern))
                {
                    config.MembersToIgnore.Add(pattern);
                    _logger.LogDebug("Added pattern variation: {Pattern}", pattern);
                }
            }
        }

        /// <summary>
        /// Add complex collection variations (original logic)
        /// </summary>
        private void AddComplexCollectionVariations(ComparisonConfig config, string propertyPath)
        {
            // Split the path into segments
            var segments = propertyPath.Split('.');

            // For each segment, check if it might be a collection
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];

                // If this is a likely collection name
                bool isCollection = segment.EndsWith("s") || segment.EndsWith("Results") ||
                                  segment.EndsWith("Items") || segment.EndsWith("List") ||
                                  segment.EndsWith("Collection") || segment.EndsWith("Array");

                if (isCollection)
                {
                    // Create the path prefix up to this segment
                    string prefix = string.Join(".", segments.Take(i + 1));

                    // Create the path suffix from remaining segments
                    string suffix = i < segments.Length - 1
                        ? "." + string.Join(".", segments.Skip(i + 1))
                        : string.Empty;

                    // Add indexed versions
                    for (int idx = 0; idx < 20; idx++)
                    {
                        string indexedPath = $"{prefix}[{idx}]{suffix}";
                        if (!config.MembersToIgnore.Contains(indexedPath))
                        {
                            config.MembersToIgnore.Add(indexedPath);
                            _logger.LogDebug("Adding indexed variation: {Path}", indexedPath);
                        }
                    }

                    // Add wildcard version
                    string wildcardPath = $"{prefix}[*]{suffix}";
                    if (!config.MembersToIgnore.Contains(wildcardPath))
                    {
                        config.MembersToIgnore.Add(wildcardPath);
                        _logger.LogDebug("Adding wildcard variation: {Path}", wildcardPath);
                    }
                }
            }

            // Special handling for simple property names
            if (!propertyPath.Contains(".") && !propertyPath.Contains("["))
            {
                string propertyName = propertyPath;

                // Add to common collections with both numbered and wildcard versions
                var collections = new[] { "Results", "RelatedItems", "Items", "Data", "Elements", "List", "Array" };
                // Use only empty prefix for domain-agnostic approach
                var prefixes = new[] { "" };

                foreach (var prefix in prefixes)
                {
                    foreach (var collection in collections)
                    {
                        // Wildcard version
                        string wildcardPath = $"{prefix}{collection}[*].{propertyName}";
                        if (!config.MembersToIgnore.Contains(wildcardPath))
                        {
                            config.MembersToIgnore.Add(wildcardPath);
                            _logger.LogDebug("Added wildcard collection path: {Path}", wildcardPath);
                        }

                        // Numbered versions
                        for (int idx = 0; idx < 20; idx++)
                        {
                            string numberedPath = $"{prefix}{collection}[{idx}].{propertyName}";
                            if (!config.MembersToIgnore.Contains(numberedPath))
                            {
                                config.MembersToIgnore.Add(numberedPath);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Add System.Collections.IList.Item[*] variations for comparison library collection handling
        /// </summary>
        private void AddSystemCollectionVariations(ComparisonConfig config, string propertyPath)
        {
            if (!propertyPath.Contains("[*]"))
                return;

            _logger.LogDebug("Creating System.Collections variations for: {PropertyPath}", propertyPath);

            // Convert [*] patterns to System.Collections.IList.Item[*] patterns
            // Example: "Body.Response.Results[*].Details" becomes "Body.Response.Results.System.Collections.IList.Item[*].Details"
            
            string systemCollectionPath = propertyPath.Replace("[*]", ".System.Collections.IList.Item[*]");
            
            if (!config.MembersToIgnore.Contains(systemCollectionPath))
            {
                config.MembersToIgnore.Add(systemCollectionPath);
                _logger.LogDebug("Added System.Collections variation: {Path}", systemCollectionPath);
            }

            // Also add numbered variations
            for (int idx = 0; idx < 20; idx++)
            {
                string numberedPath = systemCollectionPath.Replace("[*]", $"[{idx}]");
                if (!config.MembersToIgnore.Contains(numberedPath))
                {
                    config.MembersToIgnore.Add(numberedPath);
                    _logger.LogDebug("Added numbered System.Collections variation: {Path}", numberedPath);
                }
            }

            // Add variations with dynamic prefixes removed (domain-agnostic)
            AddDynamicPrefixVariationsForSystemCollections(config, systemCollectionPath);
        }

        /// <summary>
        /// Add path variations by dynamically removing path segments from the beginning.
        /// This is domain-agnostic and works with any path structure.
        /// </summary>
        private void AddDynamicPrefixVariations(ComparisonConfig config, string propertyPath)
        {
            var segments = propertyPath.Split('.');
            
            // Create variations by removing 1, 2, 3... segments from the beginning
            for (int segmentsToRemove = 1; segmentsToRemove < segments.Length; segmentsToRemove++)
            {
                if (segments.Length > segmentsToRemove)
                {
                    string shortenedPath = string.Join(".", segments.Skip(segmentsToRemove));
                    if (!string.IsNullOrEmpty(shortenedPath) && !config.MembersToIgnore.Contains(shortenedPath))
                    {
                        config.MembersToIgnore.Add(shortenedPath);
                        _logger.LogDebug("Added dynamic prefix variation (removed {Count} segments): {Path}", segmentsToRemove, shortenedPath);
                        
                        // Also generate collection variations for this shortened path
                        AddCollectionVariations(config, shortenedPath);
                    }
                }
            }
        }

        /// <summary>
        /// Add dynamic prefix variations for patterns containing [*].
        /// </summary>
        private void AddDynamicPrefixVariationsForPattern(ComparisonConfig config, string propertyPath)
        {
            var segments = propertyPath.Split('.');
            
            // Create variations by removing 1, 2, 3... segments from the beginning
            for (int segmentsToRemove = 1; segmentsToRemove < segments.Length; segmentsToRemove++)
            {
                if (segments.Length > segmentsToRemove)
                {
                    string shortenedPath = string.Join(".", segments.Skip(segmentsToRemove));
                    if (!string.IsNullOrEmpty(shortenedPath))
                    {
                        // Add numbered variations for this shortened path
                        for (int idx = 0; idx < 20; idx++)
                        {
                            string numberedPath = shortenedPath.Replace("[*]", $"[{idx}]");
                            if (!config.MembersToIgnore.Contains(numberedPath))
                            {
                                config.MembersToIgnore.Add(numberedPath);
                                _logger.LogDebug("Added dynamic prefix variation (removed {Count} segments, numbered): {Path}", segmentsToRemove, numberedPath);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Add dynamic prefix variations for System.Collections paths.
        /// </summary>
        private void AddDynamicPrefixVariationsForSystemCollections(ComparisonConfig config, string systemCollectionPath)
        {
            var segments = systemCollectionPath.Split('.');
            
            // Create variations by removing 1, 2, 3... segments from the beginning  
            for (int segmentsToRemove = 1; segmentsToRemove < segments.Length; segmentsToRemove++)
            {
                if (segments.Length > segmentsToRemove)
                {
                    string shortenedPath = string.Join(".", segments.Skip(segmentsToRemove));
                    if (!string.IsNullOrEmpty(shortenedPath))
                    {
                        if (!config.MembersToIgnore.Contains(shortenedPath))
                        {
                            config.MembersToIgnore.Add(shortenedPath);
                            _logger.LogDebug("Added dynamic System.Collections variation (removed {Count} segments): {Path}", segmentsToRemove, shortenedPath);
                        }

                        // Add numbered variations
                        for (int idx = 0; idx < 20; idx++)
                        {
                            string numberedPath = shortenedPath.Replace("[*]", $"[{idx}]");
                            if (!config.MembersToIgnore.Contains(numberedPath))
                            {
                                config.MembersToIgnore.Add(numberedPath);
                                _logger.LogDebug("Added dynamic System.Collections variation (removed {Count} segments, numbered): {Path}", segmentsToRemove, numberedPath);
                            }
                        }
                    }
                }
            }
        }
    }
}