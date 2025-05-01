using KellermanSoftware.CompareNetObjects;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;

namespace ComparisonTool.Core.Comparison.Configuration
{
    /// <summary>
    /// Represents a rule for ignoring or configuring comparison for a specific property
    /// </summary>
    public class IgnoreRule
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Path to the property (e.g., "Body.Response.Results[0].Name")
        /// </summary>
        public string PropertyPath { get; set; }

        /// <summary>
        /// Whether to completely ignore this property during comparison
        /// </summary>
        public bool IgnoreCompletely { get; set; }

        /// <summary>
        /// For collections, whether to ignore the order of items
        /// </summary>
        public bool IgnoreCollectionOrder { get; set; }

        /// <summary>
        /// For strings, whether to ignore case when comparing
        /// </summary>
        public bool IgnoreCase { get; set; }
        
        /// <summary>
        /// Constructor for IgnoreRule
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
            if (IgnoreCompletely)
            {
                // Normalize the property path to handle variations in XML deserialization
                string normalizedPath = NormalizePropertyPath(PropertyPath);
                
                _logger.LogDebug("Applying complete ignore for property: {PropertyPath} (normalized: {NormalizedPath})", 
                    PropertyPath, normalizedPath);
                
                // Add the property to be ignored
                config.MembersToIgnore.Add(PropertyPath);
                
                // If the normalized path is different, add it too
                if (normalizedPath != PropertyPath)
                {
                    config.MembersToIgnore.Add(normalizedPath);
                }

                // Also add variations for collection items
                AddCollectionVariations(config, PropertyPath);
                
                // If the property path contains Body.Response, also add a version without it
                if (PropertyPath.Contains("Body.Response."))
                {
                    string withoutBodyResponse = PropertyPath.Replace("Body.Response.", "");
                    _logger.LogDebug("Adding version without Body.Response prefix: {Path}", withoutBodyResponse);
                    config.MembersToIgnore.Add(withoutBodyResponse);
                    AddCollectionVariations(config, withoutBodyResponse);
                }
            }

            // Case insensitivity is a global setting in CompareNetObjects
            if (IgnoreCase)
            {
                _logger.LogDebug("Setting case insensitivity for property: {PropertyPath}", PropertyPath);
                config.CaseSensitive = false;
            }

            // CRITICAL: We NEVER set the global IgnoreCollectionOrder flag
            // Collection order ignoring is handled by the PropertySpecificCollectionOrderComparer
            if (IgnoreCollectionOrder)
            {
                _logger.LogWarning("[IMPORTANT] Property {PropertyPath} has collection order ignoring enabled - this is handled ONLY by PropertySpecificCollectionOrderComparer", 
                    PropertyPath);
                
                // DO NOT set this flag - ever! It would make all collections ignore order.
                // config.IgnoreCollectionOrder = true;  <-- THIS IS BAD. DON'T DO THIS.
                
                // Double-check that global flag is not accidentally set to true
                if (config.IgnoreCollectionOrder)
                {
                    _logger.LogWarning("[WARNING] The global IgnoreCollectionOrder flag is set to TRUE. This will make ALL collections ignore order, regardless of property-specific settings!");
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
                // Split the path into segments
                var segments = propertyPath.Split('.');

                // For each segment, check if it might be a collection
                for (int i = 0; i < segments.Length; i++)
                {
                    string segment = segments[i];

                    // If this is a likely collection name or already has an index
                    bool isCollection = segment.EndsWith("s") || segment.EndsWith("Results") ||
                                      segment.EndsWith("Items") || segment.EndsWith("List") ||
                                      segment.EndsWith("Collection") || segment.EndsWith("Array") ||
                                      segment.Contains("[");

                    if (isCollection)
                    {
                        // Create the path prefix up to this segment
                        string prefix = string.Join(".", segments.Take(i + 1));

                        // Create the path suffix from remaining segments
                        string suffix = i < segments.Length - 1
                            ? "." + string.Join(".", segments.Skip(i + 1))
                            : string.Empty;

                        // If segment already has an index pattern, extract base name
                        string baseName = segment;
                        if (segment.Contains("["))
                        {
                            baseName = segment.Substring(0, segment.IndexOf('['));
                            prefix = i > 0
                                ? string.Join(".", segments.Take(i)) + "." + baseName
                                : baseName;
                        }

                        // Add indexed versions
                        for (int idx = 0; idx < 10; idx++) // Support first 10 indices
                        {
                            string indexedPath = $"{prefix}[{idx}]{suffix}";
                            if (!config.MembersToIgnore.Contains(indexedPath))
                            {
                                _logger.LogDebug("Adding indexed variation: {Path}", indexedPath);
                                config.MembersToIgnore.Add(indexedPath);
                            }
                        }

                        // Add wildcard version
                        string wildcardPath = $"{prefix}[*]{suffix}";
                        if (!config.MembersToIgnore.Contains(wildcardPath))
                        {
                            _logger.LogDebug("Adding wildcard variation: {Path}", wildcardPath);
                            config.MembersToIgnore.Add(wildcardPath);
                        }

                        // Add .Item version
                        string itemPath = $"{prefix}.Item{suffix}";
                        if (!config.MembersToIgnore.Contains(itemPath))
                        {
                            _logger.LogDebug("Adding .Item variation: {Path}", itemPath);
                            config.MembersToIgnore.Add(itemPath);
                        }
                    }
                }

                // Special handling for simple property names within Results collection
                if (!propertyPath.Contains(".") && !propertyPath.Contains("["))
                {
                    // This is a simple property name, could be within collections
                    string propertyName = propertyPath;

                    // Add to common collections
                    for (int idx = 0; idx < 10; idx++)
                    {
                        string resultsPath = $"Results[{idx}].{propertyName}";
                        if (!config.MembersToIgnore.Contains(resultsPath))
                        {
                            _logger.LogDebug("Adding Results collection variation: {Path}", resultsPath);
                            config.MembersToIgnore.Add(resultsPath);
                        }

                        string bodyPath = $"Body.Response.Results[{idx}].{propertyName}";
                        if (!config.MembersToIgnore.Contains(bodyPath))
                        {
                            _logger.LogDebug("Adding Body.Response.Results collection variation: {Path}", bodyPath);
                            config.MembersToIgnore.Add(bodyPath);
                        }
                        
                        // Also add for RelatedItems 
                        string relatedItemsPath = $"RelatedItems[{idx}].{propertyName}";
                        if (!config.MembersToIgnore.Contains(relatedItemsPath))
                        {
                            _logger.LogDebug("Adding RelatedItems collection variation: {Path}", relatedItemsPath);
                            config.MembersToIgnore.Add(relatedItemsPath);
                        }
                        
                        string bodyRelatedItemsPath = $"Body.Response.RelatedItems[{idx}].{propertyName}";
                        if (!config.MembersToIgnore.Contains(bodyRelatedItemsPath))
                        {
                            _logger.LogDebug("Adding Body.Response.RelatedItems collection variation: {Path}", bodyRelatedItemsPath);
                            config.MembersToIgnore.Add(bodyRelatedItemsPath);
                        }
                    }

                    // Wildcard versions
                    config.MembersToIgnore.Add($"Results[*].{propertyName}");
                    config.MembersToIgnore.Add($"Body.Response.Results[*].{propertyName}");
                    config.MembersToIgnore.Add($"RelatedItems[*].{propertyName}");
                    config.MembersToIgnore.Add($"Body.Response.RelatedItems[*].{propertyName}");
                    
                    _logger.LogDebug("Added wildcard versions for Results and RelatedItems collections");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding collection variations for {PropertyPath}", propertyPath);
            }
        }
    }
}