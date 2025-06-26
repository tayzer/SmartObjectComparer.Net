using KellermanSoftware.CompareNetObjects;
using KellermanSoftware.CompareNetObjects.TypeComparers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComparisonTool.Core.Comparison.Configuration
{
    /// <summary>
    /// A custom comparer that ignores collection order for specified properties only
    /// Performance optimized - removed expensive debug logging
    /// </summary>
    public class PropertySpecificCollectionOrderComparer : BaseTypeComparer
    {
        private readonly HashSet<string> _propertiesToIgnoreOrder;
        private readonly ILogger _logger;
        
        // Explicitly track if we have rules for Results or RelatedItems (for fast lookup)
        private readonly bool _hasResultsRule;
        private readonly bool _hasRelatedItemsRule;
        
        // Use simple thread-safe tracking without expensive concurrent dictionaries
        private static int _comparisonCount = 0;

        /// <summary>
        /// Constructor that takes a list of property paths where order should be ignored
        /// </summary>
        /// <param name="rootComparer">The root comparer</param>
        /// <param name="propertiesToIgnoreOrder">Property paths where collection order should be ignored</param>
        /// <param name="logger">Optional logger</param>
        public PropertySpecificCollectionOrderComparer(
            RootComparer rootComparer, 
            IEnumerable<string> propertiesToIgnoreOrder,
            ILogger logger = null) : base(rootComparer)
        {
            _propertiesToIgnoreOrder = new HashSet<string>(propertiesToIgnoreOrder ?? Enumerable.Empty<string>());
            _logger = logger ?? NullLogger.Instance;
            
            // Check if we have rules for specific collections using simple IndexOf
            _hasResultsRule = _propertiesToIgnoreOrder.Any(p => 
                p.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0);
                
            _hasRelatedItemsRule = _propertiesToIgnoreOrder.Any(p => 
                p.IndexOf("RelatedItems", StringComparison.OrdinalIgnoreCase) >= 0);
            
            // Simplified logging - only log the essential information
            _logger.LogDebug("PropertySpecificCollectionOrderComparer initialized with {Count} properties, Results rule: {HasResults}, RelatedItems rule: {HasRelatedItems}", 
                _propertiesToIgnoreOrder.Count, _hasResultsRule, _hasRelatedItemsRule);
        }

        /// <summary>
        /// Check if this comparer handles the specified types
        /// </summary>
        public override bool IsTypeMatch(Type type1, Type type2)
        {
            bool isEnumerable1 = type1 != null && typeof(IEnumerable).IsAssignableFrom(type1);
            bool isEnumerable2 = type2 != null && typeof(IEnumerable).IsAssignableFrom(type2);
            bool isString1 = type1 == typeof(string);
            bool isString2 = type2 == typeof(string);
            
            // Make sure this only handles collections, not strings or other non-collections
            return isEnumerable1 && isEnumerable2 && !isString1 && !isString2;
        }

        /// <summary>
        /// Compare the collections, ignoring order for specified properties
        /// </summary>
        public override void CompareType(CompareParms parms)
        {
            // Increment comparison counter for essential tracking only
            int currentCount = System.Threading.Interlocked.Increment(ref _comparisonCount);
            
            // Minimal logging - only for first few comparisons or if debug enabled
            if (currentCount <= 3 || _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Collection comparison #{Count} at path: '{Path}'", currentCount, parms.BreadCrumb ?? "unknown");
            }
            
            // Don't bother with complex logic if the breadcrumb is empty
            if (string.IsNullOrEmpty(parms.BreadCrumb))
            {
                var noPathCollectionComparer = new CollectionComparer(RootComparer);
                noPathCollectionComparer.CompareType(parms);
                return;
            }
            
            // Save original config state - we'll restore this at the end
            bool originalIgnoreCollectionOrder = parms.Config.IgnoreCollectionOrder;

            try 
            {
                // If global ignore order is set, use the default collection comparer
                if (originalIgnoreCollectionOrder) 
                {
                    var defaultCollectionComparer = new CollectionComparer(RootComparer);
                    defaultCollectionComparer.CompareType(parms);
                    return;
                }
                
                // Fast path: Direct path matching for Results/RelatedItems
                bool isResultsCollection = _hasResultsRule && 
                    (parms.BreadCrumb.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0);
                bool isRelatedItemsCollection = _hasRelatedItemsRule && 
                    (parms.BreadCrumb.IndexOf("RelatedItems", StringComparison.OrdinalIgnoreCase) >= 0);
                
                bool shouldIgnoreOrder = false;
                
                if (isResultsCollection || isRelatedItemsCollection)
                {
                    shouldIgnoreOrder = true;
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Fast path match for '{Path}' - ignoring collection order", parms.BreadCrumb);
                    }
                }
                else
                {
                    // Detailed pattern matching (more expensive, but only when needed)
                    shouldIgnoreOrder = _propertiesToIgnoreOrder.Any(pattern => 
                        DoesPathMatchPattern(parms.BreadCrumb, pattern));
                    
                    if (shouldIgnoreOrder && _logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Pattern match for '{Path}' - ignoring collection order", parms.BreadCrumb);
                    }
                }

                if (shouldIgnoreOrder)
                {
                    // Temporarily enable ignore collection order for this comparison
                    parms.Config.IgnoreCollectionOrder = true;
                    var ignoreOrderComparer = new CollectionComparer(RootComparer);
                    ignoreOrderComparer.CompareType(parms);
                }
                else
                {
                    // Use normal collection comparison (preserve order)
                    var normalComparer = new CollectionComparer(RootComparer);
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
        /// Optimized method to check if a path matches a pattern
        /// </summary>
        private bool DoesPathMatchPattern(string path, string pattern)
        {
            // Fast exact match check
            if (string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            // Fast prefix check for sub-properties
            if (path.StartsWith(pattern + ".", StringComparison.OrdinalIgnoreCase))
                return true;

            // Handle wildcard patterns [*] - convert to simple pattern matching
            if (pattern.Contains("[*]"))
            {
                // Simple approach: replace [*] with a regex and check
                try
                {
                    string regexPattern = pattern.Replace("[*]", @"\[\d+\]");
                    return System.Text.RegularExpressions.Regex.IsMatch(path, regexPattern, 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                catch
                {
                    // Fallback to simple contains check if regex fails
                    string basePattern = pattern.Replace("[*]", "");
                    return path.Contains(basePattern, StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        /// <summary>
        /// Get performance statistics for monitoring
        /// </summary>
        public static int GetComparisonCount()
        {
            return _comparisonCount;
        }
    }
} 