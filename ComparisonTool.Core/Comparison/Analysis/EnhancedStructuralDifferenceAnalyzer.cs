using System.Text;
using System.Text.RegularExpressions;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Utilities;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison.Analysis
{
    /// <summary>
    /// Enhanced analzer that identifies structural patterns in differences,
    /// with improved detection of missing elements, common patterns, and better categorisation for testers reviewing Expected/Actual comparisons.
    /// </summary>
    public class EnhancedStructuralDifferenceAnalyzer
    {
        private readonly MultiFolderComparisonResult folderResults;
        private readonly ILogger logger;

        // TODO: This could be done with adding to DI, as we dont want to hardcode any domain stuff in this tool.
        // Known important properties that should be highlighted when missing
        private readonly HashSet<string> criticalProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "Test"
        };

        // TODO: This could be done with adding to DI, as we dont want to hardcode any domain stuff in this tool.
        // Common parent paths to monitor for missing children
        private readonly HashSet<string> importantParentPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "Body.Response.SearchResult"
        };

        public EnhancedStructuralDifferenceAnalyzer(MultiFolderComparisonResult folderResult, ILogger logger)
        {
             this.folderResults = folderResult;
             this.logger = logger;
        }

        public class StructuralPattern
        {
            public string ParentPath { get; set; }
            public string MissingProperty { get; set; }
            public string FullPattern { get; set; }
            public DifferenceCategory Category { get; set; }
            public int OccurenceCount { get; set; }
            public int FileCount { get; set; }
            public List<string> AffectedFiles { get; set; } = new List<string>();
            public List<Difference> Examples { get; set; } = new List<Difference>();
            /// <summary>
            /// How consistently this pattern appears (0-100%).
            /// </summary>
            public double Consistency { get; set; }
            public bool IsCollectionElement { get; set; }
            public string CollectionName { get; set; }
            public bool IsCriticalProperty { get; set; }
            public string HumanReadableDescription { get; set; }
            public string RecommendAction { get; set; }
        }

        public class FileCoverageAnalysis
        {
            /// <summary>
            /// Files categorized by their primary difference type - each file appears exactly once
            /// </summary>
            public Dictionary<string, List<string>> FilesByCategory { get; set; } = new Dictionary<string, List<string>>();
            
            /// <summary>
            /// Count of files in each category
            /// </summary>
            public Dictionary<string, int> FileCounts { get; set; } = new Dictionary<string, int>();
            
            /// <summary>
            /// Total files processed (should equal sum of all categories)
            /// </summary>
            public int TotalFiles { get; set; }
            
            /// <summary>
            /// Validation: does the sum of categories equal total files?
            /// </summary>
            public bool IsComplete => FileCounts.Values.Sum() == TotalFiles;
        }

        public class EnhancedStructuralAnalysisResult
        {
            /// <summary>
            /// Critical missing elements that testers should focus on.
            /// </summary>
            public List<StructuralPattern> CriticalMissingElements { get; set; } = new List<StructuralPattern>();

            public List<StructuralPattern> ConsistentlyMissingProperties { get; set; } = new List<StructuralPattern>();

            public List<StructuralPattern> MissingCollectionElements { get; set; } = new List<StructuralPattern>();

            public List<StructuralPattern> ElementOrderDifferences { get; set; } = new List<StructuralPattern>();
             
            public List<StructuralPattern> ConsistentValueDifferences { get; set; } = new List<StructuralPattern>();

            public List<StructuralPattern> GeneralValueDifferences { get; set; } = new List<StructuralPattern>();

            /// <summary>
            /// Differences that don't fit into any specific pattern category.
            /// </summary>
            public List<StructuralPattern> UncategorizedDifferences { get; set; } = new List<StructuralPattern>();

            public List<StructuralPattern> AllPatterns { get; set; } = new List<StructuralPattern>();

            // Summary statistics
            public int TotalFilesAnalyzed { get; set; }
            public int FilesWithDifferences { get; set; }
            public int TotalDifferencesFound { get; set; }
            public int CriticalDifferencesFound { get; set; }
            public int UncategorizedDifferencesFound { get; set; }

            // Unique file counts for each category (prevents double-counting)
            public int UniqueValueDifferenceFiles => 
                ConsistentValueDifferences.Concat(GeneralValueDifferences)
                .SelectMany(p => p.AffectedFiles)
                .Distinct()
                .Count();

            public int UniqueMissingPropertyFiles =>
                CriticalMissingElements.Concat(ConsistentlyMissingProperties).Concat(MissingCollectionElements)
                .SelectMany(p => p.AffectedFiles)
                .Distinct()
                .Count();

            public int UniqueUncategorizedFiles =>
                UncategorizedDifferences
                .SelectMany(p => p.AffectedFiles)
                .Distinct()
                .Count();

            public int UniqueOrderDifferenceFiles =>
                ElementOrderDifferences
                .SelectMany(p => p.AffectedFiles)
                .Distinct()
                .Count();

            // Total unique files covered by ALL patterns (should match FilesWithDifferences)
            public int TotalUniqueFilesCovered =>
                AllPatterns
                .SelectMany(p => p.AffectedFiles)
                .Distinct()
                .Count();

            // Add a ValueDifferences list for all value-difference-related categories
            public List<StructuralPattern> ValueDifferences { get; set; } = new List<StructuralPattern>();

            /// <summary>
            /// List of file pairs with differences that are not covered by any pattern (for diagnostics/UI)
            /// </summary>
            public List<string> UnaccountedFilesWithDifferences { get; set; } = new List<string>();
            /// <summary>
            /// Count of unaccounted files with differences
            /// </summary>
            public int UnaccountedFilesWithDifferencesCount => UnaccountedFilesWithDifferences.Count;

            // Mutually exclusive file counts for clear reporting
            /// <summary>
            /// Files that have ONLY value differences (no other categories)
            /// </summary>
            public int ExclusiveValueDifferenceFiles { get; set; }
            /// <summary>
            /// Files that have ONLY missing property differences (no other categories)
            /// </summary>
            public int ExclusiveMissingPropertyFiles { get; set; }
            /// <summary>
            /// Files that have ONLY order differences (no other categories)
            /// </summary>
            public int ExclusiveOrderDifferenceFiles { get; set; }
            /// <summary>
            /// Files that have ONLY uncategorized differences (no other categories)
            /// </summary>
            public int ExclusiveUncategorizedFiles { get; set; }
            /// <summary>
            /// Files that appear in multiple categories
            /// </summary>
            public int MultiCategoryFiles { get; set; }
            /// <summary>
            /// Breakdown of which combinations of categories appear together
            /// </summary>
            public Dictionary<string, int> CategoryCombinations { get; set; } = new Dictionary<string, int>();

            // Value difference subcategory breakdowns
            /// <summary>
            /// Files that have ONLY text content value differences
            /// </summary>
            public int ExclusiveTextContentFiles { get; set; }
            /// <summary>
            /// Files that have ONLY numeric value differences
            /// </summary>
            public int ExclusiveNumericValueFiles { get; set; }
            /// <summary>
            /// Files that have ONLY boolean value differences
            /// </summary>
            public int ExclusiveBooleanValueFiles { get; set; }
            /// <summary>
            /// Files that have ONLY date/time value differences
            /// </summary>
            public int ExclusiveDateTimeFiles { get; set; }
            /// <summary>
            /// Files that have ONLY general value differences
            /// </summary>
            public int ExclusiveGeneralValueFiles { get; set; }
            /// <summary>
            /// Files that appear in multiple value difference subcategories
            /// </summary>
            public int MultiValueCategoryFiles { get; set; }
                    /// <summary>
        /// Dictionary mapping value category combinations to file counts
        /// </summary>
        public Dictionary<string, int> ValueCategoryCombinations { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// File-first classification that ensures complete coverage
        /// </summary>
        public FileCoverageAnalysis FileClassification { get; set; } = new FileCoverageAnalysis();
        }

        public EnhancedStructuralAnalysisResult AnalyzeStructuralPatterns()
        {
            logger?.LogInformation("Starting enhanced structural analysis for {FileCount} file pairs", folderResults.FilePairResults.Count);

            var result = new EnhancedStructuralAnalysisResult()
            {
                TotalFilesAnalyzed = folderResults.FilePairResults.Count,
                FilesWithDifferences = folderResults.FilePairResults.Count(r => r.Summary != null && !r.Summary.AreEqual)
            };

            var allDifferences = new List<(Difference difference, string FilePair, FilePairComparisonResult Result)>();
            var pathSegmentCounts = new Dictionary<string, Dictionary<string, int>>();
            var missingElementTotals = new Dictionary<string, int>();
            var valueDifferences = new Dictionary<string, Dictionary<(string OldValue, string NewValue), int>>();

            // Track which differences have been categorized into patterns
            var categorizedDifferences = new HashSet<Difference>();

            // First pass: collect all differences with file pair context
            foreach (var filePairResult in folderResults.FilePairResults)
            {
                if(filePairResult.Summary == null || filePairResult.Summary.AreEqual) continue;

                var pairIdentifier = $"{filePairResult.File1Name} vs {filePairResult.File2Name}";

                foreach (var difference in filePairResult.Result.Differences)
                {
                    allDifferences.Add((difference, pairIdentifier, filePairResult));
                    result.TotalDifferencesFound++;

                    // Extract parent path for this difference
                    var segments = ExtractPathSegments(difference.PropertyName);
                    
                    // Process both top-level and nested properties
                    if (segments.Count > 1)
                    {
                        // Track occurances of child elements under parents (nested properties)
                        var parentPath = string.Join(".", segments.Take(segments.Count - 1));
                        var childElement = segments.Last();

                        if (!pathSegmentCounts.ContainsKey(parentPath))
                        {
                            pathSegmentCounts[parentPath] = new Dictionary<string, int>();
                        }

                        if (!pathSegmentCounts[parentPath].ContainsKey(childElement))
                        {
                            pathSegmentCounts[parentPath][childElement] = 0;
                        }

                        pathSegmentCounts[parentPath][childElement]++;
                    }
                    else if (segments.Count == 1)
                    {
                        // Track top-level properties under a special "Root" parent
                        var parentPath = "Root";
                        var childElement = segments.First();

                        if (!pathSegmentCounts.ContainsKey(parentPath))
                        {
                            pathSegmentCounts[parentPath] = new Dictionary<string, int>();
                        }

                        if (!pathSegmentCounts[parentPath].ContainsKey(childElement))
                        {
                            pathSegmentCounts[parentPath][childElement] = 0;
                        }

                        pathSegmentCounts[parentPath][childElement]++;
                    }

                    // Track missing elements if this is a null value change (all properties)
                    if (IsPropertyMissing(difference))
                    {
                        var missingPath = difference.PropertyName;

                        if (!missingElementTotals.ContainsKey(missingPath))
                        {
                            missingElementTotals[missingPath] = 0;
                        }

                        missingElementTotals[missingPath]++;

                        // Check if this is a critical property
                        if (IsCriticalProperty(difference.PropertyName))
                        {
                            result.CriticalDifferencesFound++;
                        }
                    }

                    // Track value differences (all properties)
                    if (!IsPropertyMissing(difference) && difference.Object1Value != null &&
                        difference.Object2Value != null)
                    {
                        var normalizedPath = NormalizePropertyPath(difference.PropertyName);
                        var oldValue = difference.Object1Value.ToString() ?? "null";
                        var newValue = difference.Object2Value.ToString() ?? "null";

                        if (!valueDifferences.ContainsKey(normalizedPath))
                        {
                            valueDifferences[normalizedPath] = new Dictionary<(string, string), int>();
                        }

                        var valuePair = (oldValue,  newValue);

                        if (!valueDifferences[normalizedPath].ContainsKey(valuePair))
                        {
                            valueDifferences[normalizedPath][valuePair] = 0;
                        }

                        valueDifferences[normalizedPath][valuePair]++;
                    }
                }
            }

            // Second pass: identify consistent patterns
            var structuralPatterns = new Dictionary<string, StructuralPattern>();

            AnalyzeCriticalMissingProperties(allDifferences, structuralPatterns, categorizedDifferences);
            AnalyzeMissingProperties(allDifferences, structuralPatterns, categorizedDifferences);
            AnalyzeCollectionElements(allDifferences, structuralPatterns, categorizedDifferences);
            AnalyzeOrderDifferences(allDifferences, structuralPatterns, categorizedDifferences);
            AnalyzeValueDifferences(allDifferences, valueDifferences, structuralPatterns, categorizedDifferences);
            AnalyzeGeneralValueDifferences(allDifferences, structuralPatterns, categorizedDifferences);

            // Identify uncategorized differences
            AnalyzeUncategorizedDifferences(allDifferences, categorizedDifferences, structuralPatterns);

            // Calculate consistency percentages and organise patterns
            foreach (var pattern in structuralPatterns.Values)
            {
                pattern.Consistency = Math.Round((double)pattern.FileCount / result.FilesWithDifferences * 100, 1);

                if (pattern.IsCriticalProperty)
                {
                    result.CriticalMissingElements.Add(pattern);
                }
                else switch (pattern.IsCollectionElement)
                {
                    case true when pattern.Category == DifferenceCategory.ItemRemoved:
                        result.MissingCollectionElements.Add(pattern);
                        break;
                    case false when pattern.Category is DifferenceCategory.NullValueChange or DifferenceCategory.ItemRemoved:
                        result.ConsistentlyMissingProperties.Add(pattern);
                        break;
                    case false when pattern.Category is DifferenceCategory.GeneralValueChanged:
                        result.GeneralValueDifferences.Add(pattern);
                        break;
                    case false when pattern.Category is DifferenceCategory.UncategorizedDifference:
                        result.UncategorizedDifferences.Add(pattern);
                        break;
                    default:
                        if (pattern.Category == DifferenceCategory.CollectionItemChanged)
                        {
                            result.ElementOrderDifferences.Add(pattern);
                        }
                        else if (pattern.Category != DifferenceCategory.NullValueChange &&
                                 pattern.Category != DifferenceCategory.ItemRemoved)
                        {
                            result.ConsistentValueDifferences.Add(pattern);
                        }
                        break;
                }
            }

            // Combine all patterns for AllPatterns list
            result.AllPatterns.AddRange(result.CriticalMissingElements);
            result.AllPatterns.AddRange(result.ConsistentlyMissingProperties);
            result.AllPatterns.AddRange(result.MissingCollectionElements);
            result.AllPatterns.AddRange(result.ElementOrderDifferences);
            result.AllPatterns.AddRange(result.ConsistentValueDifferences);
            result.AllPatterns.AddRange(result.GeneralValueDifferences);
            result.AllPatterns.AddRange(result.UncategorizedDifferences);

            // Set uncategorized count
            result.UncategorizedDifferencesFound = result.UncategorizedDifferences.Sum(p => p.OccurenceCount);

            // Calculate mutually exclusive file counts for clear reporting
            CalculateExclusiveFileCounts(result);

            // Calculate unaccounted files (those with differences but not covered by any pattern)
            var allPatternFiles = result.AllPatterns.SelectMany(p => p.AffectedFiles).ToHashSet();
            var filesWithDifferences = folderResults.FilePairResults
                ?.Where(r => r.Summary != null && !r.Summary.AreEqual)
                .Select(r => $"{r.File1Name} vs {r.File2Name}")
                .ToHashSet() ?? new HashSet<string>();
            
            result.UnaccountedFilesWithDifferences = filesWithDifferences.Except(allPatternFiles).ToList();

            logger?.LogInformation("Enhanced structural analysis complete. Found {CriticalCount} critical missing elements, {MissingProps} consistently missing properties, {OrderDiffs} element order differences, and {UncategorizedCount} uncategorized differences",
                result.CriticalMissingElements.Count, result.ConsistentlyMissingProperties.Count, result.ElementOrderDifferences.Count, result.UncategorizedDifferences.Count);

            // Sort by criticality, then consistency
            result.ConsistentValueDifferences = result.ConsistentValueDifferences
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.FileCount)
                .ToList();

            result.GeneralValueDifferences = result.GeneralValueDifferences
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.FileCount)
                .ToList();

            result.UncategorizedDifferences = result.UncategorizedDifferences
                .OrderByDescending(p => p.FileCount)
                .ThenByDescending(p => p.OccurenceCount)
                .ToList();

            // Add a ValueDifferences list for all value-difference-related categories
            result.ValueDifferences = result.AllPatterns
                .Where(p => p.Category == DifferenceCategory.TextContentChanged
                         || p.Category == DifferenceCategory.NumericValueChanged
                         || p.Category == DifferenceCategory.BooleanValueChanged
                         || p.Category == DifferenceCategory.DateTimeChanged
                         || p.Category == DifferenceCategory.ValueChanged
                         || p.Category == DifferenceCategory.GeneralValueChanged)
                .OrderByDescending(p => p.Consistency)
                .ThenByDescending(p => p.FileCount)
                .ToList();

            // Calculate mutually exclusive file counts for clear reporting
            CalculateExclusiveFileCounts(result);

            // Create file classification breakdown for complete coverage
            result.FileClassification = CreateFileClassificationBreakdown();

            return result;
        }

        private void AnalyzeCriticalMissingProperties(
            List<(Difference Difference, string FilePair, FilePairComparisonResult Result)> allDifferences,
            Dictionary<string, StructuralPattern> patterns,
            HashSet<Difference> categorizedDifferences)
        {
            var criticalDifferences = allDifferences
                .Where(c => IsCriticalProperty(c.Difference.PropertyName) && IsPropertyMissing(c.Difference))
                .GroupBy(c => NormalizePropertyPath(c.Difference.PropertyName));

            foreach (var group in criticalDifferences)
            {
                var normalizedPath = group.Key;
                var segments = ExtractPathSegments(normalizedPath);

                if (segments.Count > 0)
                {
                    var parentPath = segments.Count > 1 ? string.Join(".", segments.Take(segments.Count - 1)) : string.Empty;
                    var propertyName = segments.Last();

                    var pattern = $"{normalizedPath}_critical";
                    if (!patterns.ContainsKey(pattern))
                    {
                        var affectedFiles = group.Select(d => d.FilePair).Distinct().ToList();
                        var examples = group.Take(3).Select(d => d.Difference).ToList();

                        var description = GetHumanReadableDescription(normalizedPath, propertyName, true);
                        var action = GetRecommendedAction(normalizedPath, propertyName, true);

                        patterns[pattern] = new StructuralPattern()
                        {
                            ParentPath = parentPath,
                            MissingProperty = propertyName,
                            FullPattern = normalizedPath,
                            Category = DifferenceCategory.NullValueChange,
                            IsCollectionElement = normalizedPath.Contains("[*]"),
                            CollectionName = normalizedPath.Contains("[*]")
                                ? ExtractCollectionName(normalizedPath)
                                : string.Empty,
                            AffectedFiles = affectedFiles,
                            Examples = examples,
                            OccurenceCount = group.Count(),
                            FileCount = affectedFiles.Count,
                            IsCriticalProperty = true,
                            HumanReadableDescription = description,
                            RecommendAction = action
                        };

                        // Mark all differences in this group as categorized
                        foreach (var difference in group)
                        {
                            categorizedDifferences.Add(difference.Difference);
                        }
                    }
                }
            }
        }

        private void AnalyzeMissingProperties(List<(Difference difference, string FilePair, FilePairComparisonResult Result)> allDifferences, Dictionary<string, StructuralPattern> structuralPatterns, HashSet<Difference> categorizedDifferences)
        {
            foreach (var (diff, filePair, _) in allDifferences)
            {
                if (IsPropertyMissing(diff) && !diff.PropertyName.Contains("[") &&
                    !IsCriticalProperty(diff.PropertyName))
                {
                    var segments = ExtractPathSegments(diff.PropertyName);
                    string parentPath;
                    string missingProperty;
                    string pattern;

                    if (segments.Count > 1)
                    {
                        // Nested property
                        parentPath = string.Join(".", segments.Take(segments.Count - 1));
                        missingProperty = segments.Last();
                        pattern = $"{parentPath}.{missingProperty}";
                    }
                    else if (segments.Count == 1)
                    {
                        // Top-level property
                        parentPath = "Root";
                        missingProperty = segments.First();
                        pattern = missingProperty; // Use just the property name for top-level
                    }
                    else
                    {
                        continue; // Skip if no valid segments
                    }

                    if (!structuralPatterns.ContainsKey(pattern))
                    {
                        var description = GetHumanReadableDescription(pattern, missingProperty, false);
                        var action = GetRecommendedAction(pattern, missingProperty, false);

                        structuralPatterns[pattern] = new StructuralPattern()
                        {
                            ParentPath = parentPath,
                            MissingProperty = missingProperty,
                            FullPattern = pattern,
                            Category = DifferenceCategory.NullValueChange,
                            IsCollectionElement = false,
                            AffectedFiles = new List<string>(){filePair},
                            Examples = new List<Difference>(){diff},
                            OccurenceCount = 1,
                            FileCount = 1,
                            IsCriticalProperty = false,
                            HumanReadableDescription = description,
                            RecommendAction = action
                        };

                        // Mark this difference as categorized
                        categorizedDifferences.Add(diff);
                    }
                    else
                    {
                        var existingPattern = structuralPatterns[pattern];
                        existingPattern.OccurenceCount++;
                        if (!existingPattern.AffectedFiles.Contains(filePair))
                        {
                            existingPattern.AffectedFiles.Add(filePair);
                            existingPattern.FileCount++;
                        }

                        if (existingPattern.Examples.Count < 3)
                        {
                            existingPattern.Examples.Add(diff);
                        }

                        // Mark this difference as categorized
                        categorizedDifferences.Add(diff);
                    }
                }
            }
        }

        private void AnalyzeValueDifferences(List<(Difference Difference, string FilePair, FilePairComparisonResult Result)> allDifferences, Dictionary<string, Dictionary<(string OldValue, string NewValue), int>> valueDifferences, Dictionary<string, StructuralPattern> structuralPatterns, HashSet<Difference> categorizedDifferences)
        {
            // Find paths with consistent value changes across multiple files
            foreach (var pathEntry in valueDifferences)
            {
                var path = pathEntry.Key;
                var valueChanges = pathEntry.Value;

                // Find the most common value change for this path
                var mostCommonChange = valueChanges
                    .OrderByDescending(v => v.Value)
                    .FirstOrDefault();

                if (mostCommonChange.Key != default && mostCommonChange.Value > 1)
                {
                    var (oldValue, newValue) = mostCommonChange.Key;

                    // Find all differences matching this path and value change
                    var matchingDiffs = allDifferences
                        .Where(d => NormalizePropertyPath(d.Difference.PropertyName) == path &&
                                    (d.Difference.Object1Value?.ToString() == oldValue ||
                                     (d.Difference.Object1Value == null && oldValue == "null")) &&
                                    (d.Difference.Object2Value?.ToString() == newValue ||
                                     (d.Difference.Object2Value == null && newValue == "null")))
                        .ToList();

                    if (matchingDiffs.Count > 1)
                    {
                        var segments = ExtractPathSegments(path);
                        var propertyName = segments.Last();
                        var parentPath = segments.Count > 1 ? string.Join(".", segments.Take(segments.Count - 1)) : string.Empty;

                        var pattern = $"{path}_{oldValue}_{newValue}";

                        if (!structuralPatterns.ContainsKey(pattern))
                        {
                            var affectedFiles = matchingDiffs.Select(d => d.FilePair).Distinct().ToList();

                            var category = DetermineValueChangeCategory(oldValue, newValue);

                            var description = GenerateValueChangeDescription(propertyName, oldValue, newValue);
                            var action =
                                $"Verify if this value change is expected. This appears in {affectedFiles.Count} files";

                            structuralPatterns[pattern] = new StructuralPattern()
                            {
                                ParentPath = parentPath,
                                MissingProperty = propertyName,
                                FullPattern = path,
                                Category = category,
                                IsCollectionElement = path.Contains("[*]"),
                                CollectionName = path.Contains("[*]") ? ExtractCollectionName(path) : string.Empty,
                                AffectedFiles = affectedFiles,
                                Examples = matchingDiffs.Take(3).Select(d => d.Difference).ToList(),
                                OccurenceCount = matchingDiffs.Count,
                                FileCount = affectedFiles.Count,
                                IsCriticalProperty = false,
                                HumanReadableDescription = description,
                                RecommendAction = action
                            };

                            // Mark all matching differences as categorized
                            foreach (var diff in matchingDiffs)
                            {
                                categorizedDifferences.Add(diff.Difference);
                            }
                        }
                    }
                }
            }
        }

        private void AnalyzeOrderDifferences(List<(Difference Difference, string FilePair, FilePairComparisonResult Result)> allDifferences, Dictionary<string, StructuralPattern> structuralPatterns, HashSet<Difference> categorizedDifferences)
        {
            var fileGroups = allDifferences.GroupBy(d => d.FilePair);

            foreach (var fileGroup in fileGroups)
            {
                var filePair = fileGroup.Key;

                var collectionDiffs = fileGroup
                    .Where(d => d.Difference.PropertyName.Contains("["))
                    .GroupBy(d => ExtractCollectionName(d.Difference.PropertyName));

                foreach (var collectionGroup in collectionDiffs)
                {
                    var collection = collectionGroup.Key;

                    // Only consider as an order issue if:
                    // 1. There are multiple differences in the same collection
                    // 2. The collection has more than one element (check indices)
                    // 3. The differences arent just missing properties
                    var indices = collectionGroup
                        .Select(d => ExtractArrayIndex(d.Difference.PropertyName))
                        .Where(c => c >= 0)
                        .Distinct()
                        .ToList();

                    var hasMultipleIndices = indices.Count > 1;
                    var hasNonMissingDiffs = collectionGroup.Any(c => !IsPropertyMissing(c.Difference));

                    // TODO: this is unique to the domain and should be fixed generally, maybe pass in values like this in DI?
                    // Special case: if we have only one Applicant but its showing as an order difference, likely a false positive.
                    var isSingleApplicant = collection.EndsWith("Applicant") && indices.Count <= 1;

                    if (hasMultipleIndices && hasNonMissingDiffs && !isSingleApplicant)
                    {
                        var pattern = $"{collection}[Order]";

                        if (!structuralPatterns.ContainsKey(pattern))
                        {
                            var description =
                                $"The elements in the '{collection}' collection appear in a different order";
                            var action =
                                $"Check if the order of elements in '{collection}' is significant. If order matters, investigate why the ordering is different.";

                            structuralPatterns[pattern] = new StructuralPattern()
                            {
                                ParentPath = collection,
                                MissingProperty = "[Order]",
                                FullPattern = pattern,
                                Category = DifferenceCategory.CollectionItemChanged,
                                IsCollectionElement = true,
                                AffectedFiles = new List<string>() { filePair },
                                Examples = collectionGroup.Take(3).Select(c => c.Difference).ToList(),
                                OccurenceCount = indices.Count,
                                FileCount = 1,
                                IsCriticalProperty = false,
                                HumanReadableDescription = description,
                                RecommendAction = action
                            };

                            // Mark all differences in this collection group as categorized
                            foreach (var diff in collectionGroup)
                            {
                                categorizedDifferences.Add(diff.Difference);
                            }
                        }
                        else
                        {
                            var existingPattern = structuralPatterns[pattern];
                            existingPattern.OccurenceCount += indices.Count;
                            if (!existingPattern.AffectedFiles.Contains(filePair))
                            {
                                existingPattern.AffectedFiles.Add(filePair);
                                existingPattern.FileCount++;
                            }

                            foreach (var example in collectionGroup.Take(3).Select(c => c.Difference))
                            {
                                if (existingPattern.Examples.Count < 3)
                                {
                                    existingPattern.Examples.Add(example);
                                }
                            }

                            // Mark all differences in this collection group as categorized
                            foreach (var diff in collectionGroup)
                            {
                                categorizedDifferences.Add(diff.Difference);
                            }
                        }
                    }
                }
            }
        }

        private void AnalyzeCollectionElements(List<(Difference Difference, string FilePair, FilePairComparisonResult Result)> allDifferences, Dictionary<string, StructuralPattern> structuralPatterns, HashSet<Difference> categorizedDifferences)
        {
            foreach (var (difference, filePair, _) in allDifferences)
            {
                if (difference.PropertyName.Contains("[") && IsPropertyMissing(difference) &&
                    !IsCriticalProperty(difference.PropertyName))
                {
                    var collection = ExtractCollectionName(difference.PropertyName);
                    var missingProperty = ExtractMissingPropertyName(difference.PropertyName);

                    var pattern = $"{collection}[*].{missingProperty}";
                    if (!structuralPatterns.ContainsKey(pattern))
                    {
                        var description = GetHumanReadableDescription(pattern, missingProperty, false);
                        var action = GetRecommendedAction(pattern, missingProperty, false);

                        structuralPatterns[pattern] = new StructuralPattern()
                        {
                            ParentPath = collection,
                            MissingProperty = missingProperty,
                            FullPattern = pattern,
                            Category = DifferenceCategory.ItemRemoved,
                            IsCollectionElement = true,
                            CollectionName = collection,
                            AffectedFiles = new List<string>() { filePair },
                            Examples = new List<Difference>() { difference },
                            OccurenceCount = 1,
                            FileCount = 1,
                            IsCriticalProperty = false,
                            HumanReadableDescription = description,
                            RecommendAction = action
                        };

                        // Mark this difference as categorized
                        categorizedDifferences.Add(difference);
                    }
                    else
                    {
                        var existingPattern = structuralPatterns[pattern];
                        existingPattern.OccurenceCount++;
                        if (!existingPattern.AffectedFiles.Contains(filePair))
                        {
                            existingPattern.AffectedFiles.Add(filePair);
                            existingPattern.FileCount++;
                        }

                        if (existingPattern.Examples.Count < 3)
                        {
                            existingPattern.Examples.Add(difference);
                        }

                        // Mark this difference as categorized
                        categorizedDifferences.Add(difference);
                    }
                }
            }
        }

        private int ExtractArrayIndex(string differencePropertyName)
        {
            var match = Regex.Match(differencePropertyName, @"\[(\d+)\]");

            if (match.Success && int.TryParse(match.Groups[1].Value, out var index))
            {
                return index;
            }

            return -1;
        }

        private string ExtractMissingPropertyName(string differencePropertyName)
        {
            var match = Regex.Match(differencePropertyName, @"\[\d+\]\.(.+)$");

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            var segments = differencePropertyName.Split('.');

            return segments.Length > 0 ? segments.Last() : differencePropertyName;
        }

        private string NormalizePropertyPath(string differencePropertyName)
        {
            return PropertyPathNormalizer.NormalizePropertyPath(differencePropertyName, logger);
        }

        private DifferenceCategory DetermineValueChangeCategory(string oldValue, string newValue)
        {
            // Check for numeric values
            if (double.TryParse(oldValue, out _) && double.TryParse(newValue, out _))
            {
                return DifferenceCategory.NumericValueChanged;
            }

            // Check for boolean values
            if ((oldValue == "True" || oldValue == "False") && (newValue == "True" || newValue == "False"))
            {
                return DifferenceCategory.BooleanValueChanged;
            }

            // Check for date values
            if ((oldValue.Contains("/") || oldValue.Contains("-")) &&
                (newValue.Contains("/") || newValue.Contains("-")))
            {
                return DifferenceCategory.DateTimeChanged;
            }

            // Default to text content
            return DifferenceCategory.TextContentChanged;
        }

        private string TruncateValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "null";
            }

            if (value.Length > 30)
            {
                return value[..27] + "...";
            }

            return value;
        }

        private string GenerateValueChangeDescription(string propertyName, string oldValue, string newValue)
        {
            var segments = ExtractPathSegments(propertyName);
            var lastSegment = segments.Last();

            // Handle null to value changes (property becoming present)
            if (string.IsNullOrEmpty(oldValue) || oldValue == "null")
            {
                return $"A new property '{lastSegment}' is now present";
            }

            // Handle value to null changes (property becoming absent)
            if (string.IsNullOrEmpty(newValue) || newValue == "null")
            {
                return $"The property '{lastSegment}' is no longer present";
            }

            // Handle regular value-to-value changes (keep existing detailed behavior)
            return $"The value of '{propertyName}' consistently changes from '{TruncateValue(oldValue)}' to '{TruncateValue(newValue)}'";
        }

        private string ExtractCollectionName(string path)
        {
            var match = Regex.Match(path, @"(.+?)(?:\[\d+\]|\[\*\])");

            return match.Success ? match.Groups[1].Value : path;
        }

        private List<string> ExtractPathSegments(string propertyPath)
        {
            var normalizedPath = NormalizePropertyPath(propertyPath);

            var segments = new List<string>();
            var currentSegment = new StringBuilder();

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
            {
                segments.Add(currentSegment.ToString());
            }

            return segments;
        }

        private bool IsCriticalProperty(string propertyPath)
        {
            var normalizedPath = NormalizePropertyPath(propertyPath);

            var segments = ExtractPathSegments(normalizedPath);
            var propertyName = segments.Last();

            if (criticalProperties.Contains(propertyName))
            {
                return true;
            }

            foreach (var criticalProperty in criticalProperties)
            {
                if (normalizedPath.EndsWith("." + criticalProperty, StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.Contains("." + criticalProperty + "."))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsPropertyMissing(Difference diff)
        {
            return (diff.Object1Value == null && diff.Object2Value != null) || (diff.Object1Value != null && diff.Object2Value == null);
        }

        private string GetHumanReadableDescription(string pattern, string propertyName, bool isCritical)
        {
            if (isCritical)
            {
                return $"The critical '{propertyName}' section is missing from the response.";
            }
            else
            {
                if (pattern.Contains("[*]"))
                {
                    var collection = ExtractCollectionName(pattern);
                    return $"The property '{propertyName}' is missing from elements in the '{collection}' collection.";
                }
                else
                {
                    return $"The property '{propertyName}' is missing from the response.";
                }
            }
        }

        private string GetRecommendedAction(string pattern, string propertyName, bool isCritical)
        {
            if (isCritical)
            {
                return $"Verify that the '{propertyName}' section should be present in the response. This is identified as a critical section.";
            }
            else
            {
                if (pattern.Contains("[*]"))
                {
                    var collection = ExtractCollectionName(pattern);
                    return $"Check if '{propertyName}' should be present in all elements of the '{collection}' collection.";
                }
                else
                {
                    return $"Check if '{propertyName}' should be present in the response.";
                }
            }
        }

        private void AnalyzeGeneralValueDifferences(List<(Difference Difference, string FilePair, FilePairComparisonResult Result)> allDifferences, Dictionary<string, StructuralPattern> structuralPatterns, HashSet<Difference> categorizedDifferences)
        {
            // Track general change frequency regardless of specific values
            var generalChangeCounts = new Dictionary<string, Dictionary<string, int>>();

            // First pass: count all value changes by normalized path
            foreach (var (difference, filePair, _) in allDifferences)
            {
                // Only count non-missing value differences
                if (!IsPropertyMissing(difference) && difference.Object1Value != null && difference.Object2Value != null)
                {
                    var normalizedPath = NormalizePropertyPath(difference.PropertyName);

                    if (!generalChangeCounts.ContainsKey(normalizedPath))
                    {
                        generalChangeCounts[normalizedPath] = new Dictionary<string, int>();
                    }

                    if (!generalChangeCounts[normalizedPath].ContainsKey(filePair))
                    {
                        generalChangeCounts[normalizedPath][filePair] = 0;
                    }

                    generalChangeCounts[normalizedPath][filePair]++;
                }
            }

            // Second pass: create patterns for paths with changes (including single-file changes)
            foreach (var pathEntry in generalChangeCounts)
            {
                var path = pathEntry.Key;
                var fileCounts = pathEntry.Value;

                // Process both multi-file and single-file changes
                var segments = ExtractPathSegments(path);
                var propertyName = segments.Last();
                var parentPath = segments.Count > 1 ? string.Join(".", segments.Take(segments.Count - 1)) : string.Empty;

                // Use a pattern key that won't conflict with specific value change patterns
                var pattern = $"{path}_GENERAL_CHANGE";

                if (!structuralPatterns.ContainsKey(pattern))
                {
                    var affectedFiles = fileCounts.Keys.ToList();
                    var totalOccurrences = fileCounts.Values.Sum();

                    // Find all matching differences for this path
                    var matchingDifferences = allDifferences
                        .Where(d => NormalizePropertyPath(d.Difference.PropertyName) == path && 
                                   !IsPropertyMissing(d.Difference))
                        .ToList();

                    // Find examples from the matching differences
                    var examples = matchingDifferences
                        .Take(3)
                        .Select(d => d.Difference)
                        .ToList();

                    string description;
                    string action;

                    if (fileCounts.Count > 1)
                    {
                        // Multi-file changes
                        description = $"The value of '{propertyName}' changes frequently across files (regardless of specific values)";
                        action = $"Review why '{propertyName}' varies across files. This property shows differences in {affectedFiles.Count} files with {totalOccurrences} total changes";
                    }
                    else
                    {
                        // Single-file changes
                        description = $"The value of '{propertyName}' has differences in individual files";
                        action = $"Review the differences in '{propertyName}' for the affected file. This property shows {totalOccurrences} change(s) in 1 file";
                    }

                    structuralPatterns[pattern] = new StructuralPattern()
                    {
                        ParentPath = parentPath,
                        MissingProperty = propertyName,
                        FullPattern = path,
                        Category = DifferenceCategory.GeneralValueChanged,
                        IsCollectionElement = path.Contains("[*]"),
                        CollectionName = path.Contains("[*]") ? ExtractCollectionName(path) : string.Empty,
                        AffectedFiles = affectedFiles,
                        Examples = examples,
                        OccurenceCount = totalOccurrences,
                        FileCount = affectedFiles.Count,
                        IsCriticalProperty = false,
                        HumanReadableDescription = description,
                        RecommendAction = action
                    };

                    // Mark all matching differences as categorized
                    foreach (var diff in matchingDifferences)
                    {
                        categorizedDifferences.Add(diff.Difference);
                    }
                }
            }
        }

        private void AnalyzeUncategorizedDifferences(List<(Difference Difference, string FilePair, FilePairComparisonResult Result)> allDifferences, HashSet<Difference> categorizedDifferences, Dictionary<string, StructuralPattern> structuralPatterns)
        {
            var uncategorizedDifferences = allDifferences
                .Where(d => !categorizedDifferences.Contains(d.Difference))
                .ToList();

            if (uncategorizedDifferences.Count > 0)
            {
                var pattern = "Uncategorized Differences";

                if (!structuralPatterns.ContainsKey(pattern))
                {
                    var affectedFiles = uncategorizedDifferences.Select(d => d.FilePair).Distinct().ToList();
                    var examples = uncategorizedDifferences.Take(3).Select(d => d.Difference).ToList();

                    var description = "Differences that don't fit into any specific pattern category";
                    var action = $"Review these differences as they may indicate issues that need further investigation";

                    structuralPatterns[pattern] = new StructuralPattern()
                    {
                        ParentPath = string.Empty,
                        MissingProperty = string.Empty,
                        FullPattern = pattern,
                        Category = DifferenceCategory.UncategorizedDifference,
                        IsCollectionElement = false,
                        CollectionName = string.Empty,
                        AffectedFiles = affectedFiles,
                        Examples = examples,
                        OccurenceCount = uncategorizedDifferences.Count,
                        FileCount = affectedFiles.Count,
                        IsCriticalProperty = false,
                        HumanReadableDescription = description,
                        RecommendAction = action
                    };
                }
            }
        }

        private void CalculateExclusiveFileCounts(EnhancedStructuralAnalysisResult result)
        {
            // Get all files with differences
            var allFilesWithDifferences = folderResults.FilePairResults
                ?.Where(r => r.Summary != null && !r.Summary.AreEqual)
                .Select(r => $"{r.File1Name} vs {r.File2Name}")
                .ToHashSet() ?? new HashSet<string>();

            // Group files by which categories they appear in
            var fileCategoryMap = new Dictionary<string, HashSet<string>>();
            
            foreach (var file in allFilesWithDifferences)
            {
                fileCategoryMap[file] = new HashSet<string>();
            }

            // Determine which categories each file appears in
            foreach (var pattern in result.AllPatterns)
            {
                string categoryGroup = GetCategoryGroup(pattern.Category);
                foreach (var file in pattern.AffectedFiles)
                {
                    if (fileCategoryMap.ContainsKey(file))
                    {
                        fileCategoryMap[file].Add(categoryGroup);
                    }
                }
            }

            // Calculate exclusive counts
            result.ExclusiveValueDifferenceFiles = fileCategoryMap.Count(f => f.Value.Count == 1 && f.Value.Contains("Value"));
            result.ExclusiveMissingPropertyFiles = fileCategoryMap.Count(f => f.Value.Count == 1 && f.Value.Contains("Missing"));
            result.ExclusiveOrderDifferenceFiles = fileCategoryMap.Count(f => f.Value.Count == 1 && f.Value.Contains("Order"));
            result.ExclusiveUncategorizedFiles = fileCategoryMap.Count(f => f.Value.Count == 1 && f.Value.Contains("Uncategorized"));
            result.MultiCategoryFiles = fileCategoryMap.Count(f => f.Value.Count > 1);

            // Calculate category combinations
            var categoryCombinations = new Dictionary<string, int>();
            foreach (var file in fileCategoryMap.Where(f => f.Value.Count > 1))
            {
                var combination = string.Join(" + ", file.Value.OrderBy(c => c));
                if (!categoryCombinations.ContainsKey(combination))
                {
                    categoryCombinations[combination] = 0;
                }
                categoryCombinations[combination]++;
            }
            result.CategoryCombinations = categoryCombinations;

            // Calculate value subcategory breakdowns
            CalculateValueSubcategoryBreakdowns(result);
        }

        private void CalculateValueSubcategoryBreakdowns(EnhancedStructuralAnalysisResult result)
        {
            // Get files that have value differences (from the main category calculation)
            var valueFiles = result.AllPatterns
                .Where(p => GetCategoryGroup(p.Category) == "Value")
                .SelectMany(p => p.AffectedFiles)
                .Distinct()
                .ToHashSet();

            // Group value files by which value subcategories they appear in
            var valueFileCategoryMap = new Dictionary<string, HashSet<string>>();
            
            foreach (var file in valueFiles)
            {
                valueFileCategoryMap[file] = new HashSet<string>();
            }

            // Determine which value subcategories each file appears in
            foreach (var pattern in result.AllPatterns.Where(p => GetCategoryGroup(p.Category) == "Value"))
            {
                string valueSubcategory = GetValueSubcategory(pattern.Category);
                foreach (var file in pattern.AffectedFiles)
                {
                    if (valueFileCategoryMap.ContainsKey(file))
                    {
                        valueFileCategoryMap[file].Add(valueSubcategory);
                    }
                }
            }

            // Calculate exclusive value subcategory counts
            result.ExclusiveTextContentFiles = valueFileCategoryMap.Count(f => f.Value.Count == 1 && f.Value.Contains("Text"));
            result.ExclusiveNumericValueFiles = valueFileCategoryMap.Count(f => f.Value.Count == 1 && f.Value.Contains("Numeric"));
            result.ExclusiveBooleanValueFiles = valueFileCategoryMap.Count(f => f.Value.Count == 1 && f.Value.Contains("Boolean"));
            result.ExclusiveDateTimeFiles = valueFileCategoryMap.Count(f => f.Value.Count == 1 && f.Value.Contains("DateTime"));
            result.ExclusiveGeneralValueFiles = valueFileCategoryMap.Count(f => f.Value.Count == 1 && f.Value.Contains("General"));
            result.MultiValueCategoryFiles = valueFileCategoryMap.Count(f => f.Value.Count > 1);

            // Calculate value subcategory combinations
            var valueCategoryCombinations = new Dictionary<string, int>();
            foreach (var file in valueFileCategoryMap.Where(f => f.Value.Count > 1))
            {
                var combination = string.Join(" + ", file.Value.OrderBy(c => c));
                if (!valueCategoryCombinations.ContainsKey(combination))
                {
                    valueCategoryCombinations[combination] = 0;
                }
                valueCategoryCombinations[combination]++;
            }
            result.ValueCategoryCombinations = valueCategoryCombinations;
        }

        private string GetValueSubcategory(DifferenceCategory category)
        {
            return category switch
            {
                DifferenceCategory.TextContentChanged => "Text",
                DifferenceCategory.NumericValueChanged => "Numeric",
                DifferenceCategory.BooleanValueChanged => "Boolean",
                DifferenceCategory.DateTimeChanged => "DateTime",
                DifferenceCategory.ValueChanged => "General",
                DifferenceCategory.GeneralValueChanged => "General",
                _ => "Other"
            };
        }

        private string GetCategoryGroup(DifferenceCategory category)
        {
            return category switch
            {
                DifferenceCategory.TextContentChanged => "Value",
                DifferenceCategory.NumericValueChanged => "Value",
                DifferenceCategory.BooleanValueChanged => "Value",
                DifferenceCategory.DateTimeChanged => "Value",
                DifferenceCategory.ValueChanged => "Value",
                DifferenceCategory.GeneralValueChanged => "Value",
                DifferenceCategory.NullValueChange => "Missing",
                DifferenceCategory.ItemRemoved => "Missing",
                DifferenceCategory.ItemAdded => "Missing",
                DifferenceCategory.CollectionItemChanged => "Order",
                DifferenceCategory.UncategorizedDifference => "Uncategorized",
                _ => "Other"
            };
        }

        private FileCoverageAnalysis CreateFileClassificationBreakdown()
        {
            var result = new FileCoverageAnalysis();
            
            // Initialize file lists and counts for each category
            result.FilesByCategory["Value"] = new List<string>();
            result.FilesByCategory["Missing"] = new List<string>();
            result.FilesByCategory["Order"] = new List<string>();
            result.FilesByCategory["Uncategorized"] = new List<string>();
            result.FilesByCategory["Mixed"] = new List<string>();
            
            result.FileCounts["Value"] = 0;
            result.FileCounts["Missing"] = 0;
            result.FileCounts["Order"] = 0;
            result.FileCounts["Uncategorized"] = 0;
            result.FileCounts["Mixed"] = 0;
            
            // Get all files with differences
            var filesWithDifferences = folderResults.FilePairResults
                ?.Where(r => r.Summary != null && !r.Summary.AreEqual)
                .ToList() ?? new List<FilePairComparisonResult>();
                
            result.TotalFiles = filesWithDifferences.Count;
            
            foreach (var filePair in filesWithDifferences)
            {
                var fileName = $"{filePair.File1Name} vs {filePair.File2Name}";
                var primaryCategory = ClassifyFilePrimaryDifferenceType(filePair);
                
                switch (primaryCategory)
                {
                    case "Value":
                        result.FileCounts["Value"]++;
                        result.FilesByCategory["Value"].Add(fileName);
                        break;
                    case "Missing":
                        result.FileCounts["Missing"]++;
                        result.FilesByCategory["Missing"].Add(fileName);
                        break;
                    case "Order":
                        result.FileCounts["Order"]++;
                        result.FilesByCategory["Order"].Add(fileName);
                        break;
                    case "Mixed":
                        result.FileCounts["Mixed"]++;
                        result.FilesByCategory["Mixed"].Add(fileName);
                        break;
                    default:
                        result.FileCounts["Uncategorized"]++;
                        result.FilesByCategory["Uncategorized"].Add(fileName);
                        break;
                }
            }
            
            return result;
        }
        
        private string ClassifyFilePrimaryDifferenceType(FilePairComparisonResult filePair)
        {
            if (filePair.Result?.Differences == null || !filePair.Result.Differences.Any())
                return "Uncategorized";
                
            var differences = filePair.Result.Differences;
            
            // Count different types of differences in this file
            var valueCount = 0;
            var missingCount = 0;
            var orderCount = 0;
            var uncategorizedCount = 0;
            
            foreach (var diff in differences)
            {
                // Determine the type of this specific difference
                if (IsPropertyMissing(diff))
                {
                    missingCount++;
                }
                else if (IsOrderDifference(diff))
                {
                    orderCount++;
                }
                else if (IsValueDifference(diff))
                {
                    valueCount++;
                }
                else
                {
                    uncategorizedCount++;
                }
            }
            
            // Determine primary category based on counts
            var totalDifferences = valueCount + missingCount + orderCount + uncategorizedCount;
            
            // If multiple categories with significant counts (>20% each), it's mixed
            var significantCategories = 0;
            if (valueCount > totalDifferences * 0.2) significantCategories++;
            if (missingCount > totalDifferences * 0.2) significantCategories++;
            if (orderCount > totalDifferences * 0.2) significantCategories++;
            
            if (significantCategories > 1)
                return "Mixed";
                
            // Otherwise, return the category with the most differences
            if (valueCount >= missingCount && valueCount >= orderCount && valueCount >= uncategorizedCount)
                return "Value";
            if (missingCount >= orderCount && missingCount >= uncategorizedCount)
                return "Missing";
            if (orderCount >= uncategorizedCount)
                return "Order";
                
            return "Uncategorized";
        }
        
        private bool IsOrderDifference(Difference diff)
        {
            // Simplified order detection - you can enhance this
            return diff.PropertyName.Contains("[") && 
                   (diff.PropertyName.Contains("Order") || 
                    diff.PropertyName.Contains("Index") ||
                    diff.PropertyName.Contains("Position"));
        }
        
        private bool IsValueDifference(Difference diff)
        {
            // This is a value difference if both objects have values but they're different
            return !IsPropertyMissing(diff) && 
                   diff.Object1Value != null && 
                   diff.Object2Value != null &&
                   !diff.Object1Value.Equals(diff.Object2Value);
        }
    }
}
