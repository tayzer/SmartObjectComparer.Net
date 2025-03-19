using KellermanSoftware.CompareNetObjects;
using System.Text.RegularExpressions;

namespace ComparisonTool.Core
{
    /// <summary>
    /// Represents a rule for ignoring or configuring comparison for a specific property
    /// </summary>
    public class IgnoreRule
    {
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
        /// Applies this rule to the comparison configuration
        /// </summary>
        public void ApplyTo(ComparisonConfig config)
        {
            if (IgnoreCompletely)
            {
                // Add the property to be ignored
                config.MembersToIgnore.Add(PropertyPath);

                // Also add variations for collection items
                AddCollectionVariations(config, PropertyPath);
            }

            // For collections with ignore ordering
            if (IgnoreCollectionOrder && PropertyPath.Contains("["))
            {
                // Set global collection order ignore since the specific collection
                // matching API differs between versions
                config.IgnoreCollectionOrder = true;
            }
            else if (IgnoreCollectionOrder)
            {
                // Check if this is likely a collection property
                if (PropertyPath.EndsWith("s") || PropertyPath.EndsWith("Results") ||
                    PropertyPath.EndsWith("Items") || PropertyPath.EndsWith("List") ||
                    PropertyPath.EndsWith("Collection") || PropertyPath.EndsWith("Array"))
                {
                    config.IgnoreCollectionOrder = true;
                }
            }

            // Case insensitivity is a global setting in CompareNetObjects
            if (IgnoreCase)
            {
                config.CaseSensitive = false;
            }
        }

        /// <summary>
        /// Add variations of the property path to handle collection items
        /// </summary>
        private void AddCollectionVariations(ComparisonConfig config, string propertyPath)
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
                            config.MembersToIgnore.Add(indexedPath);
                        }
                    }

                    // Add wildcard version
                    string wildcardPath = $"{prefix}[*]{suffix}";
                    if (!config.MembersToIgnore.Contains(wildcardPath))
                    {
                        config.MembersToIgnore.Add(wildcardPath);
                    }

                    // Add .Item version
                    string itemPath = $"{prefix}.Item{suffix}";
                    if (!config.MembersToIgnore.Contains(itemPath))
                    {
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
                        config.MembersToIgnore.Add(resultsPath);

                    string bodyPath = $"Body.Response.Results[{idx}].{propertyName}";
                    if (!config.MembersToIgnore.Contains(bodyPath))
                        config.MembersToIgnore.Add(bodyPath);
                }

                // Wildcard versions
                config.MembersToIgnore.Add($"Results[*].{propertyName}");
                config.MembersToIgnore.Add($"Body.Response.Results[*].{propertyName}");
            }
        }
    }
}