// <copyright file="PropertySpecificCollectionOrderComparer.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Configuration
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using KellermanSoftware.CompareNetObjects;
    using KellermanSoftware.CompareNetObjects.TypeComparers;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;

    /// <summary>
    /// A custom comparer that ignores collection order for specified properties only
    /// Performance optimized - removed expensive debug logging.
    /// </summary>
    public class PropertySpecificCollectionOrderComparer : BaseTypeComparer
    {
        private readonly HashSet<string> propertiesToIgnoreOrder;
        private readonly ILogger logger;

        // Explicitly track if we have rules for Results or RelatedItems (for fast lookup)
        private readonly bool hasResultsRule;
        private readonly bool hasRelatedItemsRule;

        // Use simple thread-safe tracking without expensive concurrent dictionaries
        private static int comparisonCount = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertySpecificCollectionOrderComparer"/> class.
        /// Constructor that takes a list of property paths where order should be ignored.
        /// </summary>
        /// <param name="rootComparer">The root comparer.</param>
        /// <param name="propertiesToIgnoreOrder">Property paths where collection order should be ignored.</param>
        /// <param name="logger">Optional logger.</param>
        public PropertySpecificCollectionOrderComparer(
            RootComparer rootComparer,
            IEnumerable<string> propertiesToIgnoreOrder,
            ILogger logger = null)
            : base(rootComparer)
            {
            this.propertiesToIgnoreOrder = new HashSet<string>(propertiesToIgnoreOrder ?? Enumerable.Empty<string>());
            this.logger = logger ?? NullLogger.Instance;

            // Check if we have rules for specific collections using simple IndexOf
            this.hasResultsRule = this.propertiesToIgnoreOrder.Any(p =>
                p.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0);

            this.hasRelatedItemsRule = this.propertiesToIgnoreOrder.Any(p =>
                p.IndexOf("RelatedItems", StringComparison.OrdinalIgnoreCase) >= 0);

            // Simplified logging - only log the essential information
            this.logger.LogDebug(
                "PropertySpecificCollectionOrderComparer initialized with {Count} properties, Results rule: {HasResults}, RelatedItems rule: {HasRelatedItems}",
                this.propertiesToIgnoreOrder.Count, this.hasResultsRule, this.hasRelatedItemsRule);
        }

        /// <summary>
        /// Check if this comparer handles the specified types.
        /// </summary>
        /// <returns></returns>
        public override bool IsTypeMatch(Type type1, Type type2)
        {
            var isEnumerable1 = type1 != null && typeof(IEnumerable).IsAssignableFrom(type1);
            var isEnumerable2 = type2 != null && typeof(IEnumerable).IsAssignableFrom(type2);
            var isString1 = type1 == typeof(string);
            var isString2 = type2 == typeof(string);

            // Make sure this only handles collections, not strings or other non-collections
            return isEnumerable1 && isEnumerable2 && !isString1 && !isString2;
        }

        /// <summary>
        /// Compare the collections, ignoring order for specified properties.
        /// </summary>
        public override void CompareType(CompareParms parms)
        {
            // Increment comparison counter for essential tracking only
            var currentCount = System.Threading.Interlocked.Increment(ref comparisonCount);

            // Minimal logging - only for first few comparisons or if debug enabled
            if (currentCount <= 3 || this.logger.IsEnabled(LogLevel.Debug))
            {
                this.logger.LogDebug("Collection comparison #{Count} at path: '{Path}'", currentCount, parms.BreadCrumb ?? "unknown");
            }

            // Don't bother with complex logic if the breadcrumb is empty
            if (string.IsNullOrEmpty(parms.BreadCrumb))
            {
                var noPathCollectionComparer = new CollectionComparer(this.RootComparer);
                noPathCollectionComparer.CompareType(parms);
                return;
            }

            // Save original config state - we'll restore this at the end
            var originalIgnoreCollectionOrder = parms.Config.IgnoreCollectionOrder;

            try
            {
                // If global ignore order is set, use the default collection comparer
                if (originalIgnoreCollectionOrder)
                {
                    var defaultCollectionComparer = new CollectionComparer(this.RootComparer);
                    defaultCollectionComparer.CompareType(parms);
                    return;
                }

                // Fast path: Direct path matching for Results/RelatedItems
                var isResultsCollection = this.hasResultsRule &&
                    (parms.BreadCrumb.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0);
                var isRelatedItemsCollection = this.hasRelatedItemsRule &&
                    (parms.BreadCrumb.IndexOf("RelatedItems", StringComparison.OrdinalIgnoreCase) >= 0);

                var shouldIgnoreOrder = false;

                if (isResultsCollection || isRelatedItemsCollection)
                {
                    shouldIgnoreOrder = true;
                    if (this.logger.IsEnabled(LogLevel.Debug))
                    {
                        this.logger.LogDebug("Fast path match for '{Path}' - ignoring collection order", parms.BreadCrumb);
                    }
                }
                else
                {
                    // Detailed pattern matching (more expensive, but only when needed)
                    shouldIgnoreOrder = this.propertiesToIgnoreOrder.Any(pattern =>
                        this.DoesPathMatchPattern(parms.BreadCrumb, pattern));

                    if (shouldIgnoreOrder && this.logger.IsEnabled(LogLevel.Debug))
                    {
                        this.logger.LogDebug("Pattern match for '{Path}' - ignoring collection order", parms.BreadCrumb);
                    }
                }

                if (shouldIgnoreOrder)
                {
                    // Temporarily enable ignore collection order for this comparison
                    parms.Config.IgnoreCollectionOrder = true;
                    var ignoreOrderComparer = new CollectionComparer(this.RootComparer);
                    ignoreOrderComparer.CompareType(parms);
                }
                else
                {
                    // Use normal collection comparison (preserve order)
                    var normalComparer = new CollectionComparer(this.RootComparer);
                    normalComparer.CompareType(parms);
                }
            }
            finally
            {
                // Always restore original setting
                parms.Config.IgnoreCollectionOrder = originalIgnoreCollectionOrder;
            }
        }

        /// <summary>
        /// Optimized method to check if a path matches a pattern.
        /// </summary>
        private bool DoesPathMatchPattern(string path, string pattern)
        {
            // Fast exact match check
            if (string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            // Fast prefix check for sub-properties
            if (path.StartsWith(pattern + ".", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            // Handle wildcard patterns [*] - convert to simple pattern matching
            if (pattern.Contains("[*]"))
            {
                // Simple approach: replace [*] with a regex and check
                try
                {
                    var regexPattern = pattern.Replace("[*]", @"\[\d+\]");
                    return System.Text.RegularExpressions.Regex.IsMatch(path, regexPattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                catch
                {
                    // Fallback to simple contains check if regex fails
                    var basePattern = pattern.Replace("[*]", string.Empty);
                    return path.Contains(basePattern, StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        /// <summary>
        /// Get performance statistics for monitoring.
        /// </summary>
        /// <returns></returns>
        public static int GetComparisonCount()
        {
            return comparisonCount;
        }
    }
}
