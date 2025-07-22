using System.Text.RegularExpressions;
using KellermanSoftware.CompareNetObjects;
using ComparisonTool.Core.Comparison.Results;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison.Analysis
{
    /// <summary>
    /// Specialized analyzer that identifies structural patterns in differences,
    /// focusing on recurring missing fields, collection item patterns, and hierarchical structures.
    /// </summary>
    public class StructuralDifferenceAnalyzer
    {
        private readonly MultiFolderComparisonResult folderResult;
        private readonly ILogger logger;

        /// <summary>
        /// Represents a structured difference pattern with hierarchical context
        /// </summary>
        public class StructuralPattern
        {
            public string ParentPath { get; set; }
            public string MissingProperty { get; set; }
            public string FullPattern { get; set; }
            public DifferenceCategory Category { get; set; }
            public int OccurrenceCount { get; set; }
            public int FileCount { get; set; }
            public List<string> AffectedFiles { get; set; } = new();
            public List<Difference> Examples { get; set; } = new();
            public double Consistency { get; set; } // How consistently this pattern appears (0-100%)
            public bool IsCollectionElement { get; set; }
            public string CollectionName { get; set; }
        }

        /// <summary>
        /// Result of structural analysis
        /// </summary>
        public class StructuralAnalysisResult
        {
            // Parent-child relationships found in differences
            public List<StructuralPattern> HierarchicalPatterns { get; set; } = new();
            
            // Collections with missing elements
            public List<StructuralPattern> MissingCollectionElements { get; set; } = new();
            
            // Properties consistently missing from the same parent
            public List<StructuralPattern> ConsistentlyMissingProperties { get; set; } = new();
            
            // Elements that appear in different order
            public List<StructuralPattern> ElementOrderDifferences { get; set; } = new();
            
            // All patterns combined for easy iteration
            public List<StructuralPattern> AllPatterns { get; set; } = new();
        }

        public StructuralDifferenceAnalyzer(MultiFolderComparisonResult folderResult, ILogger logger = null)
        {
            this.folderResult = folderResult;
            this.logger = logger;
        }

        /// <summary>
        /// Analyze structural patterns in differences across all file pairs
        /// </summary>
        public StructuralAnalysisResult AnalyzeStructuralPatterns()
        {
            logger?.LogInformation("Starting structural pattern analysis for {FileCount} file pairs", folderResult.FilePairResults.Count);
            
            var result = new StructuralAnalysisResult();
            var allDifferences = new List<(Difference Diff, string FilePair)>();
            var pathSegmentCounts = new Dictionary<string, Dictionary<string, int>>();
            var missingElementTotals = new Dictionary<string, int>();
            
            // First pass: collect all differences with file pair context
            foreach (var filePair in folderResult.FilePairResults)
            {
                if (filePair.AreEqual) continue;
                
                var pairIdentifier = $"{filePair.File1Name} vs {filePair.File2Name}";
                
                foreach (var diff in filePair.Result.Differences)
                {
                    allDifferences.Add((diff, pairIdentifier));
                    
                    // Extract parent path for this difference
                    var segments = ExtractPathSegments(diff.PropertyName);
                    if (segments.Count > 1)
                    {
                        // Track occurrences of child elements under parents
                        var parentPath = string.Join(".", segments.Take(segments.Count - 1));
                        var childElement = segments.Last();
                        
                        if (!pathSegmentCounts.ContainsKey(parentPath))
                            pathSegmentCounts[parentPath] = new Dictionary<string, int>();
                            
                        if (!pathSegmentCounts[parentPath].ContainsKey(childElement))
                            pathSegmentCounts[parentPath][childElement] = 0;
                            
                        pathSegmentCounts[parentPath][childElement]++;
                        
                        // Track missing elements if this is a null value change
                        if (IsPropertyMissing(diff))
                        {
                            var missingPath = diff.PropertyName;
                            if (!missingElementTotals.ContainsKey(missingPath))
                                missingElementTotals[missingPath] = 0;
                                
                            missingElementTotals[missingPath]++;
                        }
                    }
                }
            }
            
            // Second pass: identify consistent patterns
            var structuralPatterns = new Dictionary<string, StructuralPattern>();

            // Analyze each difference in context
            foreach (var (diff, filePair) in allDifferences)
            {
                // Identify collection elements with consistent missing properties
                if (diff.PropertyName.Contains("[") && IsPropertyMissing(diff))
                {
                    var collection = ExtractCollectionName(diff.PropertyName);
                    var missingProperty = ExtractMissingPropertyName(diff.PropertyName);
                    
                    var pattern = $"{collection}.{missingProperty}";
                    if (!structuralPatterns.ContainsKey(pattern))
                    {
                        structuralPatterns[pattern] = new StructuralPattern
                        {
                            ParentPath = collection,
                            MissingProperty = missingProperty,
                            FullPattern = pattern,
                            Category = DifferenceCategory.ItemRemoved,
                            IsCollectionElement = true,
                            CollectionName = collection,
                            AffectedFiles = new List<string> { filePair },
                            Examples = new List<Difference> { diff },
                            OccurrenceCount = 1,
                            FileCount = 1
                        };
                    }
                    else
                    {
                        var existingPattern = structuralPatterns[pattern];
                        existingPattern.OccurrenceCount++;
                        if (!existingPattern.AffectedFiles.Contains(filePair))
                        {
                            existingPattern.AffectedFiles.Add(filePair);
                            existingPattern.FileCount++;
                        }
                        if (existingPattern.Examples.Count < 3)
                            existingPattern.Examples.Add(diff);
                    }
                }
                // Identify consistently missing properties from objects
                else if (IsPropertyMissing(diff))
                {
                    var segments = ExtractPathSegments(diff.PropertyName);
                    if (segments.Count > 1)
                    {
                        var parentPath = string.Join(".", segments.Take(segments.Count - 1));
                        var missingProperty = segments.Last();
                        
                        var pattern = $"{parentPath}.{missingProperty}";
                        if (!structuralPatterns.ContainsKey(pattern))
                        {
                            structuralPatterns[pattern] = new StructuralPattern
                            {
                                ParentPath = parentPath,
                                MissingProperty = missingProperty,
                                FullPattern = pattern,
                                Category = DifferenceCategory.NullValueChange,
                                IsCollectionElement = false,
                                AffectedFiles = new List<string> { filePair },
                                Examples = new List<Difference> { diff },
                                OccurrenceCount = 1,
                                FileCount = 1
                            };
                        }
                        else
                        {
                            var existingPattern = structuralPatterns[pattern];
                            existingPattern.OccurrenceCount++;
                            if (!existingPattern.AffectedFiles.Contains(filePair))
                            {
                                existingPattern.AffectedFiles.Add(filePair);
                                existingPattern.FileCount++;
                            }
                            if (existingPattern.Examples.Count < 3)
                                existingPattern.Examples.Add(diff);
                        }
                    }
                }
                // Identify element order differences
                else if (IsOrderDifference(diff))
                {
                    var collection = ExtractCollectionName(diff.PropertyName);
                    
                    var pattern = $"{collection}[Order]";
                    if (!structuralPatterns.ContainsKey(pattern))
                    {
                        structuralPatterns[pattern] = new StructuralPattern
                        {
                            ParentPath = collection,
                            MissingProperty = "[Order]",
                            FullPattern = pattern,
                            Category = DifferenceCategory.CollectionItemChanged,
                            IsCollectionElement = true,
                            CollectionName = collection,
                            AffectedFiles = new List<string> { filePair },
                            Examples = new List<Difference> { diff },
                            OccurrenceCount = 1,
                            FileCount = 1
                        };
                    }
                    else
                    {
                        var existingPattern = structuralPatterns[pattern];
                        existingPattern.OccurrenceCount++;
                        if (!existingPattern.AffectedFiles.Contains(filePair))
                        {
                            existingPattern.AffectedFiles.Add(filePair);
                            existingPattern.FileCount++;
                        }
                        if (existingPattern.Examples.Count < 3)
                            existingPattern.Examples.Add(diff);
                    }
                }
            }
            
            // Calculate consistency percentages and organize patterns
            foreach (var pattern in structuralPatterns.Values)
            {
                // Calculate consistency across files
                pattern.Consistency = Math.Round((double)pattern.FileCount / folderResult.FilePairResults.Count * 100, 1);
                
                // Add to appropriate categories
                if (pattern.IsCollectionElement && pattern.Category == DifferenceCategory.ItemRemoved)
                {
                    result.MissingCollectionElements.Add(pattern);
                }
                else if (!pattern.IsCollectionElement && pattern.Category == DifferenceCategory.NullValueChange)
                {
                    result.ConsistentlyMissingProperties.Add(pattern);
                }
                else if (pattern.IsCollectionElement && pattern.Category == DifferenceCategory.CollectionItemChanged)
                {
                    result.ElementOrderDifferences.Add(pattern);
                }
                
                // Add to hierarchical patterns
                result.HierarchicalPatterns.Add(pattern);
            }
            
            // Sort each category by consistency and occurrence count
            result.MissingCollectionElements = result.MissingCollectionElements
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.OccurrenceCount)
                .ToList();
                
            result.ConsistentlyMissingProperties = result.ConsistentlyMissingProperties
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.OccurrenceCount)
                .ToList();
                
            result.ElementOrderDifferences = result.ElementOrderDifferences
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.OccurrenceCount)
                .ToList();
                
            result.HierarchicalPatterns = result.HierarchicalPatterns
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.OccurrenceCount)
                .ToList();
                
            // Create a unified list of all patterns
            result.AllPatterns = result.HierarchicalPatterns;
            
            logger?.LogInformation("Structural analysis complete. Found {MissingElements} missing collection element patterns, {MissingProps} consistently missing properties, and {OrderDiffs} element order differences",
                result.MissingCollectionElements.Count,
                result.ConsistentlyMissingProperties.Count,
                result.ElementOrderDifferences.Count);
                
            return result;
        }
        
        /// <summary>
        /// Extract path segments from a property path
        /// </summary>
        private List<string> ExtractPathSegments(string propertyPath)
        {
            // Normalize array indices
            var normalizedPath = NormalizePropertyPath(propertyPath);
            
            // Split by dots, but preserve array notation
            var segments = new List<string>();
            var currentSegment = new System.Text.StringBuilder();
            
            foreach (var c in normalizedPath)
            {
                if (c == '.' && !currentSegment.ToString().Contains("["))
                {
                    if (currentSegment.Length > 0)
                    {
                        segments.Add(currentSegment.ToString());
                        currentSegment.Clear();
                    }
                }
                else
                {
                    currentSegment.Append(c);
                }
            }
            
            if (currentSegment.Length > 0)
                segments.Add(currentSegment.ToString());
                
            return segments;
        }
        
        /// <summary>
        /// Extract the collection name from a property path with array index
        /// </summary>
        private string ExtractCollectionName(string propertyPath)
        {
            var match = Regex.Match(propertyPath, @"(.+?)(?:\[\d+\])");
            if (match.Success)
                return match.Groups[1].Value;
                
            return propertyPath;
        }
        
        /// <summary>
        /// Extract the property name that is missing from a collection item
        /// </summary>
        private string ExtractMissingPropertyName(string propertyPath)
        {
            var match = Regex.Match(propertyPath, @"\[\d+\]\.(.+)$");
            if (match.Success)
                return match.Groups[1].Value;
                
            // If no match, return the last segment
            var segments = propertyPath.Split('.');
            return segments.Length > 0 ? segments.Last() : propertyPath;
        }
        
        /// <summary>
        /// Normalize property path by replacing specific indices with [*]
        /// </summary>
        private string NormalizePropertyPath(string propertyPath)
        {
            var normalized = Regex.Replace(propertyPath ?? string.Empty, @"\[\d+\]", "[*]");
            
            // Normalize System.Collections paths to standard array notation
            // Convert System.Collections.IList.Item[*] to [*]
            normalized = Regex.Replace(normalized, @"\.System\.Collections\.IList\.Item\[", "[");
            normalized = Regex.Replace(normalized, @"\.System\.Collections\.Generic\.IList`1\.Item\[", "[");
            
            return normalized;
        }
        
        /// <summary>
        /// Check if a difference represents a missing property (null in one but not the other)
        /// </summary>
        private bool IsPropertyMissing(Difference diff)
        {
            return (diff.Object1Value == null && diff.Object2Value != null) || 
                   (diff.Object1Value != null && diff.Object2Value == null);
        }
        
        /// <summary>
        /// Check if a difference likely represents an element order difference
        /// </summary>
        private bool IsOrderDifference(Difference diff)
        {
            // Check if it's a collection item with same types but different values
            if (diff.PropertyName.Contains("[") && 
                diff.Object1Value != null && 
                diff.Object2Value != null &&
                diff.Object1Value.GetType() == diff.Object2Value.GetType())
            {
                // If we have similar values that differ slightly, it might be an order issue
                if (diff.Object1Value is string str1 && diff.Object2Value is string str2)
                {
                    if (str1.Length > 0 && str2.Length > 0 && 
                        (str1.Contains(str2.Substring(0, Math.Min(5, str2.Length))) || 
                         str2.Contains(str1.Substring(0, Math.Min(5, str1.Length)))))
                    {
                        return true;
                    }
                }
                
                // Try to handle numeric values of various types
                double? num1 = GetNumericValue(diff.Object1Value);
                double? num2 = GetNumericValue(diff.Object2Value);
                
                if (num1.HasValue && num2.HasValue)
                {
                    return Math.Abs(num1.Value - num2.Value) <= 10;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Attempt to convert a value to a numeric value
        /// </summary>
        private double? GetNumericValue(object value)
        {
            if (value == null) return null;
            
            // Direct numeric types
            if (value is int intVal) return intVal;
            if (value is long longVal) return longVal;
            if (value is double doubleVal) return doubleVal;
            if (value is float floatVal) return floatVal;
            if (value is decimal decimalVal) return (double)decimalVal;
            
            // Try to parse as numeric
            string strValue = value.ToString();
            if (double.TryParse(strValue, out double result))
            {
                return result;
            }
            
            return null;
        }
    }
} 