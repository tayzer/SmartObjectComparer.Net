using KellermanSoftware.CompareNetObjects;

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
        /// For numeric values, the tolerance for differences
        /// </summary>
        public double NumericTolerance { get; set; }

        /// <summary>
        /// Applies this rule to the comparison configuration
        /// </summary>
        public void ApplyTo(ComparisonConfig config)
        {
            if (IgnoreCompletely)
            {
                config.MembersToIgnore.Add(PropertyPath);
            }

            // For collections with ignore ordering
            if (IgnoreCollectionOrder && PropertyPath.Contains("["))
            {
                // Set global collection order ignore since the specific collection
                // matching API differs between versions
                config.IgnoreCollectionOrder = true;
            }

            // Case insensitivity is a global setting in CompareNetObjects
            if (IgnoreCase)
            {
                config.CaseSensitive = false;
            }

            // Numeric tolerance
            if (NumericTolerance > 0)
            {
                config.DoublePrecision = NumericTolerance;
            }
        }
    }
}