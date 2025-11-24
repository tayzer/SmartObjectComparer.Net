// <copyright file="PatternFrequencyAnalyzer.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using ComparisonTool.Core.Comparison.Results;
    using ComparisonTool.Core.Utilities;
    using KellermanSoftware.CompareNetObjects;

    /// <summary>
    /// Analyzes frequency of recurring difference patterns (e.g., missing/added elements, value changes) across all file pairs.
    /// Groups by normalized property path and difference category.
    /// </summary>
    public class PatternFrequencyAnalyzer
    {
        public class PatternFrequencyGroup
        {
            public string NormalizedPath { get; set; } = string.Empty;

            public DifferenceCategory Category
            {
                get; set;
            }

            public int OccurrenceCount
            {
                get; set;
            }

            public int FileCount
            {
                get; set;
            }

            public List<string> AffectedFiles { get; set; } = new ();

            public List<Difference> Examples { get; set; } = new ();
        }

        /// <summary>
        /// Analyze all differences and group by normalized property path and category.
        /// </summary>
        /// <param name="allDifferences">All differences from all file pairs.</param>
        /// <param name="differencesToFilePairMap">Dictionary mapping differences to their file pair identifiers.</param>
        /// <param name="totalFiles">Total number of file pairs analyzed.</param>
        /// <returns></returns>
        public List<PatternFrequencyGroup> Analyze(IEnumerable<Difference> allDifferences, Dictionary<Difference, string> differencesToFilePairMap, int totalFiles)
        {
            var groups = allDifferences
                .GroupBy(d => (NormalizedPath: this.NormalizePropertyPath(d.PropertyName), Category: this.GetDifferenceCategory(d)))
                .Select(g => new PatternFrequencyGroup
                {
                    NormalizedPath = g.Key.NormalizedPath,
                    Category = g.Key.Category,
                    OccurrenceCount = g.Count(),
                    FileCount = g.Select(d => differencesToFilePairMap.ContainsKey(d) ? differencesToFilePairMap[d] : "unknown").Distinct().Count(),
                    AffectedFiles = g.Select(d => differencesToFilePairMap.ContainsKey(d) ? differencesToFilePairMap[d] : "unknown").Distinct().ToList(),
                    Examples = g.Take(3).ToList(),
                })
                .OrderByDescending(g => g.FileCount)
                .ThenByDescending(g => g.OccurrenceCount)
                .ToList();
            return groups;
        }

        // Original overload for backward compatibility
        public List<PatternFrequencyGroup> Analyze(IEnumerable<Difference> allDifferences, int totalFiles)
        {
            // Create an empty mapping if none is provided - this won't be used but maintains API compatibility
            return this.Analyze(allDifferences, new Dictionary<Difference, string>(), totalFiles);
        }

        private string NormalizePropertyPath(string propertyPath)
        {
            return PropertyPathNormalizer.NormalizePropertyPath(propertyPath);
        }

        private DifferenceCategory GetDifferenceCategory(Difference diff)
        {
            // Use the same logic as DifferenceCategorizer.GetDifferenceCategory
            if (diff.PropertyName.Contains("[") && diff.PropertyName.Contains("]"))
            {
                if (diff.Object1Value == null && diff.Object2Value != null)
                {
                    return DifferenceCategory.ItemAdded;
                }
                else if (diff.Object1Value != null && diff.Object2Value == null)
                {
                    return DifferenceCategory.ItemRemoved;
                }
                else
                {
                    return DifferenceCategory.CollectionItemChanged;
                }
            }
            else if (this.IsNumericDifference(diff.Object1Value, diff.Object2Value))
            {
                return DifferenceCategory.NumericValueChanged;
            }
            else if (this.IsDateTimeDifference(diff.Object1Value, diff.Object2Value))
            {
                return DifferenceCategory.DateTimeChanged;
            }
            else if (this.IsStringDifference(diff.Object1Value, diff.Object2Value))
            {
                return DifferenceCategory.TextContentChanged;
            }
            else if (this.IsBooleanDifference(diff.Object1Value, diff.Object2Value))
            {
                return DifferenceCategory.BooleanValueChanged;
            }
            else if (diff.Object1Value == null || diff.Object2Value == null)
            {
                return DifferenceCategory.NullValueChange;
            }
            else
            {
                return DifferenceCategory.Other;
            }
        }

        private bool IsNumericDifference(object value1, object value2)
        {
            return (value1 is int || value1 is long || value1 is float || value1 is double || value1 is decimal)
                && (value2 is int || value2 is long || value2 is float || value2 is double || value2 is decimal);
        }

        private bool IsDateTimeDifference(object value1, object value2)
        {
            return value1 is DateTime && value2 is DateTime;
        }

        private bool IsStringDifference(object value1, object value2)
        {
            return value1 is string && value2 is string;
        }

        private bool IsBooleanDifference(object value1, object value2)
        {
            return value1 is bool && value2 is bool;
        }
    }
}
