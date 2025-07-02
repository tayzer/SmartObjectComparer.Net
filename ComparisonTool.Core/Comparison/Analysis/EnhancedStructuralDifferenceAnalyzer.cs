using System.Text;
using System.Text.RegularExpressions;
using ComparisonTool.Core.Comparison.Results;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison.Analysis
{
    /// <summary>
    /// Enhanced analzer that identifies structural patterns in differences,
    /// with improved detection of missing elements, common patterns, and better categorisation for testers reviewing A/B comparisons.
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
        }


        public EnhancedStructuralAnalysisResult AnalyzeStructuralPatterns()
        {
            logger?.LogInformation("Starting enhanced structural analysis for {FileCount} file pairs", folderResults.FilePairResults.Count);

            var result = new EnhancedStructuralAnalysisResult()
            {
                TotalFilesAnalyzed = folderResults.FilePairResults.Count,
                FilesWithDifferences = folderResults.FilePairResults.Count(r => r.AreEqual == false)
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
                if(filePairResult.AreEqual) continue;

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

            logger?.LogInformation("Enhanced structural analysis complete. Found {CriticalCount} critical missing elements, {MissingProps} consistently missing properties, {OrderDiffs} element order differences, and {UncategorizedCount} uncategorized differences",
                result.CriticalMissingElements.Count, result.ConsistentlyMissingProperties.Count, result.ElementOrderDifferences.Count, result.UncategorizedDifferences.Count);

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
            return Regex.Replace(differencePropertyName ?? string.Empty, @"\[\d+\]", "[*]");
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

            // Second pass: create patterns for paths with changes in multiple files
            foreach (var pathEntry in generalChangeCounts)
            {
                var path = pathEntry.Key;
                var fileCounts = pathEntry.Value;

                // Only consider paths that have changes in multiple files
                if (fileCounts.Count > 1)
                {
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

                        var description = $"The value of '{propertyName}' changes frequently across files (regardless of specific values)";
                        var action = $"Review why '{propertyName}' varies across files. This property shows differences in {affectedFiles.Count} files with {totalOccurrences} total changes";

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
    }
}
