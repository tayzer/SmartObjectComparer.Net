// <copyright file="SemanticDifferenceAnalyzer.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Analysis
{
    using System.Text;
    using System.Text.RegularExpressions;
    using ComparisonTool.Core.Comparison.Analysis;
    using ComparisonTool.Core.Comparison.Results;
    using ComparisonTool.Core.Utilities;
    using KellermanSoftware.CompareNetObjects;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Analyzes differences to create semantic groupings.
    /// </summary>
    public class SemanticDifferenceAnalyzer
    {
        private readonly MultiFolderComparisonResult folderResult;
        private readonly ComparisonPatternAnalysis patternAnalysis;
        private readonly ILogger logger;

        /// <summary>
        /// Patterns for recognizing common change types.
        /// </summary>
        private readonly Dictionary<string, Func<Difference, bool>> semanticPatterns = new Dictionary<string, Func<Difference, bool>>
        {
            { "Status Changes", diff => IsStatusChange(diff) },
            { "ID Value Changes", diff => IsIdValueChange(diff) },
            { "Timestamp/Date Changes", diff => IsDateTimeChange(diff) },
            { "Score/Value Adjustments", diff => IsScoreValueChange(diff) },
            { "Name/Description Changes", diff => IsNameOrDescriptionChange(diff) },
            { "Collection Order Changes", diff => IsCollectionOrderChange(diff) },
            { "Tag Modifications", diff => IsTagChange(diff) },
        };

        /// <summary>
        /// Common document sections for grouping changes.
        /// </summary>
        private readonly Dictionary<string, HashSet<string>> documentSections = new Dictionary<string, HashSet<string>>
        {
            { "Header Information", new HashSet<string> { "ReportId", "GeneratedOn" } },
            { "Summary Data", new HashSet<string> { "Summary", "TotalResults", "SuccessCount", "FailureCount" } },
            { "Result Details", new HashSet<string> { "Results[", "Score", "Status", "Description" } },
            { "Tags & Categories", new HashSet<string> { "Tags", "Tag" } },
        };

        public SemanticDifferenceAnalyzer(MultiFolderComparisonResult folderResult, ComparisonPatternAnalysis patternAnalysis, ILogger logger = null)
        {
            this.folderResult = folderResult;
            this.patternAnalysis = patternAnalysis;
            this.logger = logger;
        }

        /// <summary>
        /// Generate semantic difference groups from the comparison results.
        /// </summary>
        /// <returns></returns>
        public SemanticDifferenceAnalysis AnalyzeSemanticGroups()
        {
            this.logger?.LogInformation("Starting semantic group analysis for {FileCount} file pairs", this.folderResult.FilePairResults.Count);
            var analysis = new SemanticDifferenceAnalysis
            {
                BaseAnalysis = this.patternAnalysis,
            };

            // 1. Create empty semantic groups based on our patterns
            var semanticGroups = new Dictionary<string, SemanticDifferenceGroup>();
            foreach (var pattern in this.semanticPatterns)
            {
                semanticGroups[pattern.Key] = new SemanticDifferenceGroup
                {
                    GroupName = pattern.Key,
                    SemanticDescription = this.GenerateDescriptionForGroup(pattern.Key),
                };
            }

            // 2. Also create document section groups
            foreach (var section in this.documentSections)
            {
                var key = $"{section.Key} Changes";
                if (!semanticGroups.ContainsKey(key))
                {
                    semanticGroups[key] = new SemanticDifferenceGroup
                    {
                        GroupName = key,
                        SemanticDescription = $"Changes that affect {section.Key.ToLower()}",
                    };
                }
            }

            var filePairIndex = 0;

            // 3. Process each file pair's differences
            foreach (var filePair in this.folderResult.FilePairResults)
            {
                filePairIndex++;
                if (filePair.AreEqual) {
                    continue;
                }

                var pairIdentifier = $"{filePair.File1Name} vs {filePair.File2Name}";
                this.logger?.LogDebug("Analyzing differences for file pair {PairIndex}/{TotalPairs}: {PairIdentifier}", filePairIndex, this.folderResult.FilePairResults.Count, pairIdentifier);

                // 4. Categorize each difference
                var diffIndex = 0;
                foreach (var diff in filePair.Result.Differences)
                {
                    diffIndex++;

                    // 4.1 Try pattern-based categorization first
                    var categorized = false;
                    foreach (var pattern in this.semanticPatterns)
                    {
                        if (pattern.Value(diff))
                        {
                            this.AddDifferenceToGroup(semanticGroups[pattern.Key], diff, pairIdentifier);
                            this.logger?.LogTrace("Categorized difference {DiffIndex} in {PairIdentifier} as '{PatternKey}'", diffIndex, pairIdentifier, pattern.Key);
                            categorized = true;
                            break;
                        }
                    }

                    // 4.2 If not categorized, try document section categorization
                    if (!categorized)
                    {
                        foreach (var section in this.documentSections)
                        {
                            if (this.IsInDocumentSection(diff, section.Value))
                            {
                                var key = $"{section.Key} Changes";
                                this.AddDifferenceToGroup(semanticGroups[key], diff, pairIdentifier);
                                this.logger?.LogTrace("Categorized difference {DiffIndex} in {PairIdentifier} as document section '{SectionKey}'", diffIndex, pairIdentifier, section.Key);
                                categorized = true;
                                break;
                            }
                        }
                    }

                    // 4.3 If still not categorized, look for specific value patterns
                    if (!categorized)
                    {
                        var valueBasedGroup = this.CategorizeByValuePattern(diff);
                        if (!string.IsNullOrEmpty(valueBasedGroup))
                        {
                            if (!semanticGroups.ContainsKey(valueBasedGroup))
                            {
                                semanticGroups[valueBasedGroup] = new SemanticDifferenceGroup
                                {
                                    GroupName = valueBasedGroup,
                                    SemanticDescription = $"Changes related to {valueBasedGroup.ToLower()}",
                                };
                            }

                            this.AddDifferenceToGroup(semanticGroups[valueBasedGroup], diff, pairIdentifier);
                            this.logger?.LogTrace("Categorized difference {DiffIndex} in {PairIdentifier} as value pattern '{ValuePattern}'", diffIndex, pairIdentifier, valueBasedGroup);
                        }
                        else
                        {
                            this.logger?.LogTrace("Difference {DiffIndex} in {PairIdentifier} could not be categorized", diffIndex, pairIdentifier);
                        }
                    }
                }
            }

            // 5. Calculate confidence levels and add non-empty groups to the analysis
            foreach (var group in semanticGroups.Values)
            {
                if (group.Differences.Count > 0)
                {
                    var baseConfidence = 50;
                    baseConfidence += 5 * Math.Min(10, group.RelatedProperties.Count);
                    baseConfidence += 5 * Math.Min(5, group.AffectedFiles.Count);
                    group.ConfidenceLevel = Math.Min(100, Math.Max(0, baseConfidence));
                    analysis.SemanticGroups.Add(group);
                    this.logger?.LogDebug("Added semantic group '{GroupName}' with {DifferenceCount} differences and confidence {Confidence}", group.GroupName, group.DifferenceCount, group.ConfidenceLevel);
                }
            }

            // 6. Sort by confidence and difference count
            analysis.SemanticGroups = analysis.SemanticGroups
                .OrderByDescending(g => g.ConfidenceLevel)
                .ThenByDescending(g => g.DifferenceCount)
                .ToList();

            this.logger?.LogInformation("Semantic group analysis complete. {GroupCount} groups created, {TotalCategorized} differences categorized.", analysis.SemanticGroups.Count, analysis.CategorizedDifferences);
            return analysis;
        }

        private void AddDifferenceToGroup(SemanticDifferenceGroup group, Difference diff, string fileIdentifier)
        {
            group.Differences.Add(diff);
            group.AffectedFiles.Add(fileIdentifier);
            group.RelatedProperties.Add(this.NormalizePropertyPath(diff.PropertyName));
        }

        private string NormalizePropertyPath(string propertyPath)
        {
            return PropertyPathNormalizer.NormalizePropertyPath(propertyPath);
        }

        private bool IsInDocumentSection(Difference diff, HashSet<string> sectionKeys)
        {
            return sectionKeys.Any(key => diff.PropertyName.Contains(key));
        }

        private string CategorizeByValuePattern(Difference diff)
        {
            // Analyze specific value patterns in the differences
            // This could be extended with more sophisticated pattern recognition
            var oldValue = diff.Object1Value?.ToString();
            var newValue = diff.Object2Value?.ToString();

            if (oldValue != null && newValue != null)
            {
                // Check for GUID/UUID replacements
                if (IsGuidFormat(oldValue) && IsGuidFormat(newValue)) {
                    return "Identifier Replacements";
                }

                // Check for URL/Path changes
                if ((oldValue.Contains("://") || oldValue.Contains("/")) &&
                    (newValue.Contains("://") || newValue.Contains("/"))) {
                    return "URL/Path Changes";
                }

                // Check for version changes
                if (Regex.IsMatch(oldValue, @"\\d+\\.\\d+") && Regex.IsMatch(newValue, @"\\d+\\.\\d+")) {
                    return "Version Changes";
                }
            }

            return null;
        }

        private static bool IsGuidFormat(string value)
        {
            return Regex.IsMatch(value, @"[0-9a-fA-F]{8}[-]?([0-9a-fA-F]{4}[-]?){3}[0-9a-fA-F]{12}");
        }

        private static bool IsStatusChange(Difference diff)
        {
            return diff.PropertyName.EndsWith(".Status") ||
                   diff.PropertyName.Contains(".Status.") ||
                   diff.PropertyName.EndsWith("Status");
        }

        private static bool IsIdValueChange(Difference diff)
        {
            return diff.PropertyName.EndsWith(".Id") ||
                   diff.PropertyName.EndsWith("ReportId") ||
                   diff.PropertyName.Contains(".Id.") ||
                   diff.PropertyName.EndsWith("Id");
        }

        private static bool IsDateTimeChange(Difference diff)
        {
            return diff.PropertyName.Contains("Date") ||
                   diff.PropertyName.Contains("Time") ||
                   diff.PropertyName.Contains("Generated") ||
                   diff.Object1Value is DateTime ||
                   diff.Object2Value is DateTime;
        }

        private static bool IsScoreValueChange(Difference diff)
        {
            return diff.PropertyName.Contains("Score") ||
                   diff.PropertyName.Contains("Value") ||
                   diff.PropertyName.Contains("Amount") ||
                   diff.PropertyName.Contains("Count") ||
                   ((diff.Object1Value is double || diff.Object1Value is int || diff.Object1Value is decimal) &&
                   (diff.Object2Value is double || diff.Object2Value is int || diff.Object2Value is decimal));
        }

        private static bool IsNameOrDescriptionChange(Difference diff)
        {
            return diff.PropertyName.Contains("Name") ||
                   diff.PropertyName.Contains("Description") ||
                   diff.PropertyName.Contains("Title") ||
                   diff.PropertyName.Contains("Label");
        }

        private static bool IsCollectionOrderChange(Difference diff)
        {
            // Detect collection order changes - this is complex as it may
            // require context from multiple differences
            return diff.PropertyName.Contains("[") && diff.PropertyName.Contains("]") &&

                   // Same values in different positions
                   diff.Object1Value != null && diff.Object2Value != null &&
                   diff.Object1Value.ToString() == diff.Object2Value.ToString();
        }

        private static bool IsTagChange(Difference diff)
        {
            return diff.PropertyName.Contains("Tag") ||
                   diff.PropertyName.Contains("Category") ||
                   diff.PropertyName.Contains("Label");
        }

        private string GenerateDescriptionForGroup(string groupName)
        {
            return groupName switch {
                "Status Changes" => "Changes to status values such as Success, Warning, Error",
                "ID Value Changes" => "Changes to identifier values",
                "Timestamp/Date Changes" => "Changes to dates, times, or timestamps",
                "Score/Value Adjustments" => "Changes to numeric scores, counts, or measurements",
                "Name/Description Changes" => "Changes to names, descriptions, or text content",
                "Collection Order Changes" => "Changes in the order of items within collections",
                "Tag Modifications" => "Changes to tags, categories, or labels",
                _ => $"Changes related to {groupName.ToLower()}"
            };
        }
    }
}
