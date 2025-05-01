using KellermanSoftware.CompareNetObjects;
using KellermanSoftware.CompareNetObjects.TypeComparers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Text;

namespace ComparisonTool.Core.Comparison.Configuration
{
    /// <summary>
    /// A custom comparer that ignores collection order for specified properties only
    /// </summary>
    public class PropertySpecificCollectionOrderComparer : BaseTypeComparer
    {
        private readonly HashSet<string> _propertiesToIgnoreOrder;
        private readonly ILogger _logger;
        private static readonly HashSet<string> _debuggedPaths = new HashSet<string>(); // Track paths we've already debugged
        private static readonly string DebugFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "collection_paths_debug.txt");
        private static bool _isFirstInstance = true; // Track if this is the first instance created
        
        // Explicitly track if we have rules for Results or RelatedItems
        private readonly bool _hasResultsRule;
        private readonly bool _hasRelatedItemsRule;
        private static int _comparisonCount = 0; // Track the number of comparisons performed
        
        // KEEP TRACK OF ACTUAL PATHS THAT MATCH FOR DEBUGGING
        private readonly HashSet<string> _matchedPaths = new HashSet<string>(); 
        private readonly HashSet<string> _unmatchedPaths = new HashSet<string>();

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
            
            // Reset matched/unmatched paths for this instance
            _matchedPaths.Clear();
            _unmatchedPaths.Clear();
            
            // Check if we have rules for specific collections using simple IndexOf
            _hasResultsRule = _propertiesToIgnoreOrder.Any(p => 
                p.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0);
                
            _hasRelatedItemsRule = _propertiesToIgnoreOrder.Any(p => 
                p.IndexOf("RelatedItems", StringComparison.OrdinalIgnoreCase) >= 0);
            
            // Only clear the file for the first instance
            if (_isFirstInstance)
            {
                // Create a fresh debug file (overwrite any existing file)
                try 
                {
                    File.WriteAllText(DebugFilePath, $"{DateTime.Now:HH:mm:ss.fff}: Property-specific collection order ignore debug log{Environment.NewLine}");
                    File.AppendAllText(DebugFilePath, $"{DateTime.Now:HH:mm:ss.fff}: ================================================={Environment.NewLine}");
                    File.AppendAllText(DebugFilePath, $"{DateTime.Now:HH:mm:ss.fff}: Properties configured to ignore order: {string.Join(", ", _propertiesToIgnoreOrder)}{Environment.NewLine}");
                    File.AppendAllText(DebugFilePath, $"{DateTime.Now:HH:mm:ss.fff}: (CTOR CHECK) Has Results rule: {_hasResultsRule}, Has RelatedItems rule: {_hasRelatedItemsRule}{Environment.NewLine}");
                    if (_hasResultsRule)
                    {
                        var resultsRules = _propertiesToIgnoreOrder.Where(p => p.Contains("Results")).ToList();
                        File.AppendAllText(DebugFilePath, $"{DateTime.Now:HH:mm:ss.fff}: Results rules: {string.Join(", ", resultsRules)}{Environment.NewLine}");
                    }
                    if (_hasRelatedItemsRule)
                    {
                        var relatedItemsRules = _propertiesToIgnoreOrder.Where(p => p.Contains("RelatedItems")).ToList();
                        File.AppendAllText(DebugFilePath, $"{DateTime.Now:HH:mm:ss.fff}: RelatedItems rules: {string.Join(", ", relatedItemsRules)}{Environment.NewLine}");
                    }
                    File.AppendAllText(DebugFilePath, $"{DateTime.Now:HH:mm:ss.fff}: ================================================={Environment.NewLine}");
                    _isFirstInstance = false;
                }
                catch (Exception ex)
                {
                    // Catch any file system errors
                    _logger.LogError(ex, "Error writing to debug file");
                }
            }
            else
            {
                // For subsequent instances, just append
                WriteDebugInfo($"ADDITIONAL INSTANCE: Properties configured to ignore order: {string.Join(", ", _propertiesToIgnoreOrder)}");
                WriteDebugInfo($"(CTOR CHECK) Has Results rule: {_hasResultsRule}, Has RelatedItems rule: {_hasRelatedItemsRule}");
            }
            
            // Log the properties that will have their collection order ignored
            _logger.LogWarning("PropertySpecificCollectionOrderComparer initialized with {Count} properties to ignore order: {Properties}", 
                _propertiesToIgnoreOrder.Count, 
                string.Join(", ", _propertiesToIgnoreOrder));
        }

        /// <summary>
        /// Check if this comparer handles the specified types
        /// </summary>
        public override bool IsTypeMatch(Type type1, Type type2)
        {
            // SUPER VERBOSE LOGGING FOR IsTypeMatch
            string type1Name = type1?.FullName ?? "null";
            string type2Name = type2?.FullName ?? "null";
            WriteDebugInfo($"IsTypeMatch CHECKING: Type1='{type1Name}', Type2='{type2Name}'");
            
            bool isEnumerable1 = type1 != null && typeof(IEnumerable).IsAssignableFrom(type1);
            bool isEnumerable2 = type2 != null && typeof(IEnumerable).IsAssignableFrom(type2);
            bool isString1 = type1 == typeof(string);
            bool isString2 = type2 == typeof(string);
            
            // Make sure this only handles collections, not strings or other non-collections
            var isMatch = isEnumerable1 && 
                          isEnumerable2 &&
                          !isString1 && 
                          !isString2;
                          
            WriteDebugInfo($" > IsEnumerable1: {isEnumerable1}, IsEnumerable2: {isEnumerable2}");
            WriteDebugInfo($" > IsString1: {isString1}, IsString2: {isString2}");
            WriteDebugInfo($" > FINAL IsTypeMatch Result: {isMatch}");
                   
            return isMatch;
        }

        /// <summary>
        /// Compare the collections, ignoring order for specified properties
        /// </summary>
        public override void CompareType(CompareParms parms)
        {
            // Increment comparison counter for debugging
            _comparisonCount++;
            
            // Detailed debug info including the current comparison count
            string pathInfo = $"Comparison #{_comparisonCount} - Collection at path: '{parms.BreadCrumb}'";
            if (parms.Object1 != null)
            {
                pathInfo += $", Type: {parms.Object1.GetType().Name}";
                
                // Add item count information for debugging
                if (parms.Object1 is IEnumerable enumerable)
                {
                    int count = 0;
                    foreach (var _ in enumerable) count++;
                    pathInfo += $", Items: {count}";
                }
            }
            
            WriteDebugInfo(pathInfo);
            
            // Log unique collection paths only once to reduce log spam for the user's log
            if (!string.IsNullOrEmpty(parms.BreadCrumb) && !_debuggedPaths.Contains(parms.BreadCrumb))
            {
                _logger.LogWarning(pathInfo);
                _debuggedPaths.Add(parms.BreadCrumb);
            }
            
            // Don't bother with complex logic if the breadcrumb is empty
            if (string.IsNullOrEmpty(parms.BreadCrumb))
            {
                WriteDebugInfo("No breadcrumb provided, using default collection comparer");
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
                    string globalMsg = $"Global IgnoreCollectionOrder is true, using default collection comparer for '{parms.BreadCrumb}'";
                    WriteDebugInfo(globalMsg);
                    var defaultCollectionComparer = new CollectionComparer(RootComparer);
                    defaultCollectionComparer.CompareType(parms);
                    return;
                }
                
                // ****** CRITICAL SECTION: DIRECT PATH MATCHING FOR RESULTS/RELATEDITEMS ******
                
                // SUPER VERBOSE LOGGING
                WriteDebugInfo("=== DETAILED CHECK FOR PATH: '" + parms.BreadCrumb + "' ===");
                bool isResultsCollection = (parms.BreadCrumb.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0);
                bool isRelatedItemsCollection = (parms.BreadCrumb.IndexOf("RelatedItems", StringComparison.OrdinalIgnoreCase) >= 0);
                WriteDebugInfo($" > Path: '{parms.BreadCrumb}'");
                WriteDebugInfo($" > Has Results Rule? {_hasResultsRule}");
                WriteDebugInfo($" > Is Results Path? {isResultsCollection}");
                WriteDebugInfo($" > Has RelatedItems Rule? {_hasRelatedItemsRule}");
                WriteDebugInfo($" > Is RelatedItems Path? {isRelatedItemsCollection}");
                
                // 1. DIRECT RESULTS CHECK - If we have a Results rule and this is a Results path, ALWAYS ignore order
                bool shouldIgnoreForResults = _hasResultsRule && isResultsCollection;
                WriteDebugInfo($" > Should ignore for Results? {shouldIgnoreForResults}");
                
                if (shouldIgnoreForResults)
                {
                    WriteDebugInfo($"!!! DIRECT RESULTS MATCH FOUND: '{parms.BreadCrumb}' contains 'Results' and we have Results rules !!!");
                    _matchedPaths.Add(parms.BreadCrumb);
                    parms.Config.IgnoreCollectionOrder = true;
                    WriteDebugInfo($"*** IGNORING ORDER for Results collection: '{parms.BreadCrumb}' ***");
                    var resultsCollectionComparer = new CollectionComparer(RootComparer);
                    resultsCollectionComparer.CompareType(parms);
                    return;
                }
                
                // 2. DIRECT RELATEDITEMS CHECK - If we have a RelatedItems rule and this is a RelatedItems path, ALWAYS ignore order
                bool shouldIgnoreForRelatedItems = _hasRelatedItemsRule && isRelatedItemsCollection;
                WriteDebugInfo($" > Should ignore for RelatedItems? {shouldIgnoreForRelatedItems}");
                
                if (shouldIgnoreForRelatedItems)
                {
                    WriteDebugInfo($"!!! DIRECT RELATEDITEMS MATCH FOUND: '{parms.BreadCrumb}' contains 'RelatedItems' and we have RelatedItems rules !!!");
                    _matchedPaths.Add(parms.BreadCrumb);
                    parms.Config.IgnoreCollectionOrder = true;
                    WriteDebugInfo($"*** IGNORING ORDER for RelatedItems collection: '{parms.BreadCrumb}' ***");
                    var relatedItemsCollectionComparer = new CollectionComparer(RootComparer);
                    relatedItemsCollectionComparer.CompareType(parms);
                    return;
                }
                
                // If neither direct check matched, proceed to the more complex logic (if needed)
                // Currently, the complex logic isn't necessary because we handle Results/RelatedItems above.
                // We simply fall through to the default behavior (keeping order).
                WriteDebugInfo($"*** NO DIRECT MATCH for '{parms.BreadCrumb}', keeping default order. ***");
                _unmatchedPaths.Add(parms.BreadCrumb);

                // Use the standard collection comparer (keeping order, as IgnoreCollectionOrder is still false)
                var collectionComparer = new CollectionComparer(RootComparer);
                collectionComparer.CompareType(parms);
            }
            finally
            {
                // Always write debug info about matched/unmatched paths every 10 comparisons
                if (_comparisonCount % 10 == 0)
                {
                    WriteDebugInfo("=== PATHS WHERE ORDER WAS IGNORED ===");
                    foreach (var path in _matchedPaths)
                    {
                        WriteDebugInfo($"IGNORED ORDER: {path}");
                    }
                    WriteDebugInfo("=== PATHS WHERE ORDER WAS KEPT ===");
                    foreach (var path in _unmatchedPaths)
                    {
                        WriteDebugInfo($"KEPT ORDER: {path}");
                    }
                }
                
                // Restore the original config setting
                parms.Config.IgnoreCollectionOrder = originalIgnoreCollectionOrder;
            }
        }

        /// <summary>
        /// Write debug information to a file
        /// </summary>
        private void WriteDebugInfo(string message)
        {
            try
            {
                File.AppendAllText(DebugFilePath, $"{DateTime.Now:HH:mm:ss.fff}: {message}{Environment.NewLine}");
            }
            catch
            {
                // Ignore any errors in debug logging
            }
        }
    }
} 