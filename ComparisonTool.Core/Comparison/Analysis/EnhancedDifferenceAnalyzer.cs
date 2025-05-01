using System.Text.RegularExpressions;
using KellermanSoftware.CompareNetObjects;
using ComparisonTool.Core.Comparison.Results;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison.Analysis
{
    /// <summary>
    /// Enhanced analyzer for XML differences that provides more meaningful categorization
    /// and identifies structured patterns across multiple comparison results.
    /// </summary>
    public class EnhancedDifferenceAnalyzer
    {
        private readonly MultiFolderComparisonResult folderResult;
        private readonly ILogger logger;

        // Common field name patterns to recognize
        private readonly Dictionary<string, Regex> fieldPatterns = new Dictionary<string, Regex>
        {
            { "Identifier", new Regex(@"(^|\.)(id|guid|uuid|key|identifier|code)$", RegexOptions.IgnoreCase) },
            { "Name", new Regex(@"(^|\.)(name|title|label|caption|heading)$", RegexOptions.IgnoreCase) },
            { "Description", new Regex(@"(^|\.)(desc|description|summary|note|comment|text)$", RegexOptions.IgnoreCase) },
            { "Status", new Regex(@"(^|\.)(status|state|condition|flag)$", RegexOptions.IgnoreCase) },
            { "Date", new Regex(@"(^|\.)(date|time|timestamp|created|modified|updated|datetime)$", RegexOptions.IgnoreCase) },
            { "Quantity", new Regex(@"(^|\.)(count|quantity|amount|number|total)$", RegexOptions.IgnoreCase) },
            { "Value", new Regex(@"(^|\.)(value|price|cost|fee|rate|score)$", RegexOptions.IgnoreCase) },
            { "Boolean", new Regex(@"(^|\.)(is|has|can|should|enabled|active|flag)$", RegexOptions.IgnoreCase) }
        };

        // Common XML collection element names
        private readonly HashSet<string> collectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "items", "elements", "list", "collection", "array", "set", "group",
            "records", "entries", "rows", "results", "data", "values"
        };

        /// <summary>
        /// Result of the enhanced analysis
        /// </summary>
        public class EnhancedAnalysisResult
        {
            // Collection elements with recurring missing properties
            public List<StructuralPattern> RecurringMissingElements { get; set; } = new();
            
            // Properties with inconsistent presence
            public List<StructuralPattern> InconsistentProperties { get; set; } = new();
            
            // Structural issues in the XML
            public List<StructuralPattern> StructuralIssues { get; set; } = new();
            
            // Differences by enhanced category
            public Dictionary<EnhancedDifferenceCategory, List<Difference>> DifferencesByCategory { get; set; } = new();
            
            // Differences by XML path pattern
            public Dictionary<string, List<Difference>> DifferencesByXmlPath { get; set; } = new();
            
            // Statistics by category
            public Dictionary<EnhancedDifferenceCategory, int> CategoryCounts { get; set; } = new();
            
            // Patterns with high impact (affect multiple files consistently)
            public List<StructuralPattern> HighImpactPatterns { get; set; } = new();
            
            // Total number of differences analyzed
            public int TotalDifferences { get; set; }
            
            // Total number of file pairs analyzed
            public int TotalFilePairs { get; set; }
            
            // Number of file pairs with differences
            public int FilePairsWithDifferences { get; set; }
        }

        /// <summary>
        /// A structural pattern identified in the differences
        /// </summary>
        public class StructuralPattern
        {
            // The XML path where this pattern occurs
            public string XmlPath { get; set; }
            
            // Human-readable description of the pattern
            public string Description { get; set; }
            
            // How many times this pattern appears
            public int OccurrenceCount { get; set; }
            
            // Number of files where this pattern appears
            public int FileCount { get; set; }
            
            // How consistently this pattern appears across files with differences (0-100%)
            public double Consistency { get; set; }
            
            // Detailed category of this difference
            public EnhancedDifferenceCategory Category { get; set; }
            
            // List of file pairs affected by this pattern
            public List<string> AffectedFiles { get; set; } = new();
            
            // Example differences for this pattern
            public List<Difference> Examples { get; set; } = new();
            
            // Is this a collection-related pattern?
            public bool IsCollectionPattern { get; set; }
            
            // The impact level (High, Medium, Low)
            public string Impact => 
                Consistency > 75 ? "High" : 
                Consistency > 40 ? "Medium" : "Low";
                
            // A helpful explanation for testers
            public string TesterGuidance { get; set; }
            
            // Potential root cause
            public string PotentialRootCause { get; set; }
        }

        public EnhancedDifferenceAnalyzer(MultiFolderComparisonResult folderResult, ILogger logger = null)
        {
            this.folderResult = folderResult;
            this.logger = logger;
        }

        /// <summary>
        /// Analyze differences across all file pairs and identify structural patterns
        /// </summary>
        public EnhancedAnalysisResult AnalyzeWithStructuralPatterns()
        {
            logger?.LogInformation("Starting enhanced difference analysis for {FileCount} file pairs", folderResult.FilePairResults.Count);
            
            var result = new EnhancedAnalysisResult
            {
                TotalFilePairs = folderResult.FilePairResults.Count,
                FilePairsWithDifferences = folderResult.FilePairResults.Count(fp => !fp.AreEqual)
            };
            
            // Initialize category counts
            foreach (EnhancedDifferenceCategory category in Enum.GetValues(typeof(EnhancedDifferenceCategory)))
            {
                result.DifferencesByCategory[category] = new List<Difference>();
                result.CategoryCounts[category] = 0;
            }
            
            // Collect all differences with context
            var allDifferenceContexts = new List<(Difference Diff, string FilePair, FilePairComparisonResult Result)>();
            var xmlPathOccurrences = new Dictionary<string, (int Count, HashSet<string> Files)>();
            
            // First pass: collect all differences with context
            foreach (var filePair in folderResult.FilePairResults)
            {
                if (filePair.AreEqual) continue;
                
                var pairIdentifier = $"{filePair.File1Name} vs {filePair.File2Name}";
                
                foreach (var diff in filePair.Result.Differences)
                {
                    // Add to total count
                    result.TotalDifferences++;
                    
                    // Add to context collection
                    allDifferenceContexts.Add((diff, pairIdentifier, filePair));
                    
                    // Categorize the difference
                    var category = CategorizeEnhanced(diff);
                    result.DifferencesByCategory[category].Add(diff);
                    result.CategoryCounts[category]++;
                    
                    // Normalize the XML path
                    var normalizedPath = NormalizeXmlPath(diff.PropertyName);
                    
                    // Track XML path occurrences
                    if (!result.DifferencesByXmlPath.ContainsKey(normalizedPath))
                        result.DifferencesByXmlPath[normalizedPath] = new List<Difference>();
                    result.DifferencesByXmlPath[normalizedPath].Add(diff);
                    
                    // Update occurrence counts
                    if (!xmlPathOccurrences.ContainsKey(normalizedPath))
                        xmlPathOccurrences[normalizedPath] = (0, new HashSet<string>());
                    xmlPathOccurrences[normalizedPath] = (
                        xmlPathOccurrences[normalizedPath].Count + 1,
                        xmlPathOccurrences[normalizedPath].Files.Add(pairIdentifier) ? 
                            xmlPathOccurrences[normalizedPath].Files : xmlPathOccurrences[normalizedPath].Files
                    );
                }
            }
            
            // Second pass: identify structural patterns
            var structuralPatterns = new Dictionary<string, StructuralPattern>();
            
            // Identify collection property patterns
            IdentifyCollectionPatterns(allDifferenceContexts, structuralPatterns);
            
            // Identify consistently missing properties
            IdentifyMissingPropertyPatterns(allDifferenceContexts, structuralPatterns);
            
            // Identify structural issues
            IdentifyStructuralIssues(allDifferenceContexts, structuralPatterns);
            
            // Calculate consistency and organize patterns
            foreach (var pattern in structuralPatterns.Values)
            {
                // Calculate consistency percentage
                pattern.Consistency = Math.Round((double)pattern.FileCount / result.FilePairsWithDifferences * 100, 1);
                
                // Assign to appropriate category in result
                if (pattern.Category == EnhancedDifferenceCategory.CollectionElementMissing ||
                    pattern.Category == EnhancedDifferenceCategory.CollectionElementExtraProperty)
                {
                    result.RecurringMissingElements.Add(pattern);
                }
                else if (pattern.Category == EnhancedDifferenceCategory.MissingRequiredField ||
                         pattern.Category == EnhancedDifferenceCategory.NullValueChange)
                {
                    result.InconsistentProperties.Add(pattern);
                }
                else if (pattern.Category == EnhancedDifferenceCategory.StructuralMismatch ||
                         pattern.Category == EnhancedDifferenceCategory.SchemaViolation)
                {
                    result.StructuralIssues.Add(pattern);
                }
                
                // Add high-impact patterns
                if (pattern.Consistency > 40 && pattern.FileCount > 1)
                {
                    result.HighImpactPatterns.Add(pattern);
                }
            }
            
            // Sort patterns by consistency and occurrence count
            result.RecurringMissingElements = result.RecurringMissingElements
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.OccurrenceCount)
                .ToList();
                
            result.InconsistentProperties = result.InconsistentProperties
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.OccurrenceCount)
                .ToList();
                
            result.StructuralIssues = result.StructuralIssues
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.OccurrenceCount)
                .ToList();
                
            result.HighImpactPatterns = result.HighImpactPatterns
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.FileCount)
                .ToList();
            
            logger?.LogInformation("Enhanced analysis complete. Found {MissingElements} recurring missing element patterns, {InconsistentProps} inconsistent properties, and {StructuralIssues} structural issues",
                result.RecurringMissingElements.Count,
                result.InconsistentProperties.Count,
                result.StructuralIssues.Count);
                
            return result;
        }
        
        /// <summary>
        /// Identify patterns related to collection elements
        /// </summary>
        private void IdentifyCollectionPatterns(
            List<(Difference Diff, string FilePair, FilePairComparisonResult Result)> differences,
            Dictionary<string, StructuralPattern> patterns)
        {
            // Group differences by collection path
            var collectionDiffs = differences
                .Where(d => d.Diff.PropertyName.Contains("[") && d.Diff.PropertyName.Contains("]"))
                .GroupBy(d => ExtractCollectionPath(d.Diff.PropertyName));
                
            foreach (var collectionGroup in collectionDiffs)
            {
                var collectionPath = collectionGroup.Key;
                var collectionName = ExtractCollectionName(collectionPath);
                
                // Check for missing properties pattern within a collection
                var missingProps = collectionGroup
                    .Where(d => IsPropertyMissing(d.Diff))
                    .GroupBy(d => ExtractPropertyAfterIndex(d.Diff.PropertyName));
                    
                foreach (var propGroup in missingProps)
                {
                    if (propGroup.Count() > 1) // Only interested in recurring patterns
                    {
                        var propertyName = propGroup.Key;
                        var patternKey = $"{collectionPath}.{propertyName}_missing";
                        
                        if (!patterns.ContainsKey(patternKey))
                        {
                            var filesAffected = propGroup.Select(d => d.FilePair).Distinct().ToList();
                            patterns[patternKey] = new StructuralPattern
                            {
                                XmlPath = $"{collectionPath}[*].{propertyName}",
                                Description = $"The property '{propertyName}' is consistently missing in elements of the '{collectionName}' collection",
                                Category = EnhancedDifferenceCategory.CollectionElementMissing,
                                OccurrenceCount = propGroup.Count(),
                                FileCount = filesAffected.Count,
                                AffectedFiles = filesAffected,
                                Examples = propGroup.Take(3).Select(d => d.Diff).ToList(),
                                IsCollectionPattern = true,
                                TesterGuidance = $"Check if '{propertyName}' should always be present in '{collectionName}' items. This appears to be a consistent omission pattern.",
                                PotentialRootCause = "Data mapping issue or conditional logic that's excluding this property"
                            };
                        }
                    }
                }
                
                // Check for collection element count mismatch
                var elementIndices = collectionGroup
                    .Select(d => ExtractArrayIndex(d.Diff.PropertyName))
                    .Distinct()
                    .ToList();
                    
                if (elementIndices.Count > 10) // Arbitrary threshold to suggest a potentially significant collection
                {
                    var patternKey = $"{collectionPath}_count";
                    if (!patterns.ContainsKey(patternKey))
                    {
                        var filesAffected = collectionGroup.Select(d => d.FilePair).Distinct().ToList();
                        patterns[patternKey] = new StructuralPattern
                        {
                            XmlPath = collectionPath,
                            Description = $"The '{collectionName}' collection has {elementIndices.Count} elements with differences",
                            Category = EnhancedDifferenceCategory.CollectionElementCountMismatch,
                            OccurrenceCount = elementIndices.Count,
                            FileCount = filesAffected.Count,
                            AffectedFiles = filesAffected,
                            Examples = collectionGroup.Take(3).Select(d => d.Diff).ToList(),
                            IsCollectionPattern = true,
                            TesterGuidance = $"Check if '{collectionName}' collection should have a consistent number of elements. Large differences might indicate missing or extra data.",
                            PotentialRootCause = "Data filtering applied differently between environments or data source differences"
                        };
                    }
                }
            }
        }
        
        /// <summary>
        /// Identify patterns of consistently missing properties
        /// </summary>
        private void IdentifyMissingPropertyPatterns(
            List<(Difference Diff, string FilePair, FilePairComparisonResult Result)> differences,
            Dictionary<string, StructuralPattern> patterns)
        {
            // Group non-collection differences by parent path
            var nonCollectionDiffs = differences
                .Where(d => !d.Diff.PropertyName.Contains("[") && d.Diff.PropertyName.Contains("."))
                .GroupBy(d => ExtractParentPath(d.Diff.PropertyName));
                
            foreach (var parentGroup in nonCollectionDiffs)
            {
                var parentPath = parentGroup.Key;
                
                // Check for consistent missing properties from this parent
                var missingProps = parentGroup
                    .Where(d => IsPropertyMissing(d.Diff))
                    .GroupBy(d => ExtractLastProperty(d.Diff.PropertyName));
                    
                foreach (var propGroup in missingProps)
                {
                    if (propGroup.Count() > 1) // Only interested in recurring patterns
                    {
                        var propertyName = propGroup.Key;
                        var patternKey = $"{parentPath}.{propertyName}_missing";
                        
                        var fieldType = DetermineFieldType(propertyName);
                        var category = fieldType == "Identifier" ? 
                                          EnhancedDifferenceCategory.IdentifierMismatch :
                                          EnhancedDifferenceCategory.MissingRequiredField;
                        
                        if (!patterns.ContainsKey(patternKey))
                        {
                            var filesAffected = propGroup.Select(d => d.FilePair).Distinct().ToList();
                            patterns[patternKey] = new StructuralPattern
                            {
                                XmlPath = $"{parentPath}.{propertyName}",
                                Description = $"The {fieldType.ToLower()} property '{propertyName}' is consistently missing from '{parentPath}'",
                                Category = category,
                                OccurrenceCount = propGroup.Count(),
                                FileCount = filesAffected.Count,
                                AffectedFiles = filesAffected,
                                Examples = propGroup.Take(3).Select(d => d.Diff).ToList(),
                                IsCollectionPattern = false,
                                TesterGuidance = $"Verify if '{propertyName}' is a required field for '{parentPath}'. This appears to be consistently missing.",
                                PotentialRootCause = fieldType == "Identifier" ? 
                                                     "Identifier generation logic differs between environments" :
                                                     "Required field not being populated by the system under test"
                            };
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Identify structural issues like schema violations
        /// </summary>
        private void IdentifyStructuralIssues(
            List<(Difference Diff, string FilePair, FilePairComparisonResult Result)> differences,
            Dictionary<string, StructuralPattern> patterns)
        {
            // Group by normalized path to find structural patterns
            var pathGroups = differences
                .GroupBy(d => NormalizeXmlPath(d.Diff.PropertyName));
                
            foreach (var pathGroup in pathGroups)
            {
                if (pathGroup.Count() < 3) continue; // Skip rare differences
                
                var normalizedPath = pathGroup.Key;
                
                // Check for type inconsistencies
                var typeMismatches = pathGroup
                    .Where(d => 
                        d.Diff.Object1Value != null && 
                        d.Diff.Object2Value != null && 
                        d.Diff.Object1Value.GetType() != d.Diff.Object2Value.GetType())
                    .ToList();
                    
                if (typeMismatches.Count > 0)
                {
                    var patternKey = $"{normalizedPath}_type_mismatch";
                    if (!patterns.ContainsKey(patternKey))
                    {
                        var filesAffected = typeMismatches.Select(d => d.FilePair).Distinct().ToList();
                        var firstDiff = typeMismatches.First().Diff;
                        
                        patterns[patternKey] = new StructuralPattern
                        {
                            XmlPath = normalizedPath,
                            Description = $"Data type mismatch at '{normalizedPath}' (Expected: {firstDiff.Object1Value?.GetType().Name}, Actual: {firstDiff.Object2Value?.GetType().Name})",
                            Category = EnhancedDifferenceCategory.InconsistentDataType,
                            OccurrenceCount = typeMismatches.Count,
                            FileCount = filesAffected.Count,
                            AffectedFiles = filesAffected,
                            Examples = typeMismatches.Take(3).Select(d => d.Diff).ToList(),
                            IsCollectionPattern = normalizedPath.Contains("[*]"),
                            TesterGuidance = "This is a potential schema violation. The data types don't match between expected and actual responses.",
                            PotentialRootCause = "API contract change or serialization issue"
                        };
                    }
                }
                
                // Check for schema structure changes (unexpected new properties)
                if (pathGroup.Any(d => IsExtraProperty(d.Diff)))
                {
                    var extraProps = pathGroup.Where(d => IsExtraProperty(d.Diff)).ToList();
                    var patternKey = $"{normalizedPath}_extra";
                    
                    if (!patterns.ContainsKey(patternKey) && extraProps.Count > 0)
                    {
                        var filesAffected = extraProps.Select(d => d.FilePair).Distinct().ToList();
                        patterns[patternKey] = new StructuralPattern
                        {
                            XmlPath = normalizedPath,
                            Description = $"Unexpected extra property at '{normalizedPath}'",
                            Category = normalizedPath.Contains("[*]") ? 
                                      EnhancedDifferenceCategory.CollectionElementExtraProperty :
                                      EnhancedDifferenceCategory.StructuralMismatch,
                            OccurrenceCount = extraProps.Count,
                            FileCount = filesAffected.Count,
                            AffectedFiles = filesAffected,
                            Examples = extraProps.Take(3).Select(d => d.Diff).ToList(),
                            IsCollectionPattern = normalizedPath.Contains("[*]"),
                            TesterGuidance = "This property exists in the actual response but not in the expected response. May indicate schema evolution.",
                            PotentialRootCause = "API version mismatch or schema changes"
                        };
                    }
                }
            }
        }
        
        /// <summary>
        /// Normalize an XML path by replacing indices with wildcards
        /// </summary>
        private string NormalizeXmlPath(string xmlPath)
        {
            return Regex.Replace(xmlPath ?? string.Empty, @"\[\d+\]", "[*]");
        }
        
        /// <summary>
        /// Extract the collection path from a property path
        /// </summary>
        private string ExtractCollectionPath(string propertyPath)
        {
            var match = Regex.Match(propertyPath, @"(.+?)(?:\[\d+\])");
            if (match.Success)
                return match.Groups[1].Value;
                
            return propertyPath;
        }
        
        /// <summary>
        /// Extract the collection name from a path
        /// </summary>
        private string ExtractCollectionName(string collectionPath)
        {
            var segments = collectionPath.Split('.');
            return segments.Length > 0 ? segments.Last() : collectionPath;
        }
        
        /// <summary>
        /// Extract the property name that appears after an array index
        /// </summary>
        private string ExtractPropertyAfterIndex(string propertyPath)
        {
            var match = Regex.Match(propertyPath, @"\[\d+\]\.(.+)$");
            if (match.Success)
                return match.Groups[1].Value;
                
            // If no match, return the last segment
            var segments = propertyPath.Split('.');
            return segments.Length > 0 ? segments.Last() : propertyPath;
        }
        
        /// <summary>
        /// Extract just the numeric array index from a property path
        /// </summary>
        private int ExtractArrayIndex(string propertyPath)
        {
            var match = Regex.Match(propertyPath, @"\[(\d+)\]");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int index))
                return index;
                
            return -1;
        }
        
        /// <summary>
        /// Extract the parent path from a property path
        /// </summary>
        private string ExtractParentPath(string propertyPath)
        {
            var lastDotIndex = propertyPath.LastIndexOf('.');
            if (lastDotIndex > 0)
                return propertyPath.Substring(0, lastDotIndex);
                
            return propertyPath;
        }
        
        /// <summary>
        /// Extract the last property name from a path
        /// </summary>
        private string ExtractLastProperty(string propertyPath)
        {
            var lastDotIndex = propertyPath.LastIndexOf('.');
            if (lastDotIndex > 0 && lastDotIndex < propertyPath.Length - 1)
                return propertyPath.Substring(lastDotIndex + 1);
                
            return propertyPath;
        }
        
        /// <summary>
        /// Determine if a property is missing (null in one side but not the other)
        /// </summary>
        private bool IsPropertyMissing(Difference diff)
        {
            return (diff.Object1Value == null && diff.Object2Value != null) || 
                   (diff.Object1Value != null && diff.Object2Value == null);
        }
        
        /// <summary>
        /// Determine if a property exists in the actual but not the expected
        /// </summary>
        private bool IsExtraProperty(Difference diff)
        {
            return diff.Object1Value == null && diff.Object2Value != null;
        }
        
        /// <summary>
        /// Categorize a difference with enhanced categories
        /// </summary>
        private EnhancedDifferenceCategory CategorizeEnhanced(Difference diff)
        {
            // Check for collection differences
            if (diff.PropertyName.Contains("[") && diff.PropertyName.Contains("]"))
            {
                if (diff.Object1Value == null && diff.Object2Value != null)
                    return EnhancedDifferenceCategory.CollectionElementExtraProperty;
                else if (diff.Object1Value != null && diff.Object2Value == null)
                    return EnhancedDifferenceCategory.CollectionElementMissing;
                else if (diff.Object1Value != null && diff.Object2Value != null &&
                         diff.Object1Value.GetType() != diff.Object2Value.GetType())
                    return EnhancedDifferenceCategory.InconsistentDataType;
                else
                    return EnhancedDifferenceCategory.CollectionItemChanged;
            }
            
            // Check for property name patterns
            var propertyName = ExtractLastProperty(diff.PropertyName);
            
            foreach (var pattern in fieldPatterns)
            {
                if (pattern.Value.IsMatch(propertyName))
                {
                    switch (pattern.Key)
                    {
                        case "Identifier":
                            return EnhancedDifferenceCategory.IdentifierMismatch;
                        case "Name":
                        case "Description":
                            return EnhancedDifferenceCategory.NameOrLabelChange;
                        case "Status":
                            return EnhancedDifferenceCategory.StatusValueChange;
                        case "Date":
                            return EnhancedDifferenceCategory.TimestampChange;
                        case "Quantity":
                        case "Value":
                            return EnhancedDifferenceCategory.CalculatedValueChange;
                        case "Boolean":
                            return EnhancedDifferenceCategory.BooleanValueChanged;
                    }
                }
            }
            
            // Check for attribute changes (attributes often in format @attribute)
            if (propertyName.StartsWith("@"))
            {
                if (diff.Object1Value == null || diff.Object2Value == null)
                    return EnhancedDifferenceCategory.XmlAttributeMissing;
                else
                    return EnhancedDifferenceCategory.XmlAttributeValueChanged;
            }
            
            // Check for basic data types
            if (diff.Object1Value != null && diff.Object2Value != null)
            {
                if (diff.Object1Value is string && diff.Object2Value is string)
                    return EnhancedDifferenceCategory.TextContentChanged;
                if ((diff.Object1Value is int || diff.Object1Value is long || diff.Object1Value is float || 
                     diff.Object1Value is double || diff.Object1Value is decimal) &&
                    (diff.Object2Value is int || diff.Object2Value is long || diff.Object2Value is float || 
                     diff.Object2Value is double || diff.Object2Value is decimal))
                    return EnhancedDifferenceCategory.NumericValueChanged;
                if (diff.Object1Value is DateTime && diff.Object2Value is DateTime)
                    return EnhancedDifferenceCategory.DateTimeChanged;
                if (diff.Object1Value is bool && diff.Object2Value is bool)
                    return EnhancedDifferenceCategory.BooleanValueChanged;
                if (diff.Object1Value.GetType() != diff.Object2Value.GetType())
                    return EnhancedDifferenceCategory.InconsistentDataType;
            }
            else if (diff.Object1Value == null || diff.Object2Value == null)
            {
                return EnhancedDifferenceCategory.NullValueChange;
            }
            
            return EnhancedDifferenceCategory.Other;
        }
        
        /// <summary>
        /// Determine the type of field based on its name
        /// </summary>
        private string DetermineFieldType(string propertyName)
        {
            foreach (var pattern in fieldPatterns)
            {
                if (pattern.Value.IsMatch(propertyName))
                {
                    return pattern.Key;
                }
            }
            
            return "Field";
        }
    }
} 