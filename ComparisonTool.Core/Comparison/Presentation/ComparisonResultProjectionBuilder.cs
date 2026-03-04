using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Core.Comparison.Presentation;

/// <summary>
/// Builds and filters projection models for comparison result views.
/// </summary>
public static class ComparisonResultProjectionBuilder
{
    /// <summary>
    /// Builds a projection from a comparison result.
    /// </summary>
    public static ComparisonResultProjection Build(MultiFolderComparisonResult? result)
    {
        if (result?.FilePairResults == null)
        {
            return new ComparisonResultProjection();
        }

        var items = result.FilePairResults
            .Select((pair, index) => new ComparisonResultGridItem
            {
                OriginalIndex = index,
                File1Name = pair.File1Name,
                File2Name = pair.File2Name,
                RequestRelativePath = pair.RequestRelativePath,
                AreEqual = pair.AreEqual,
                HasError = pair.HasError,
                ErrorMessage = pair.ErrorMessage ?? string.Empty,
                DifferenceCount = pair.RawTextDifferences?.Count ?? pair.Summary?.TotalDifferenceCount ?? 0,
                Category = pair.Summary?.CommonPatterns?.FirstOrDefault()?.Pattern ?? "Uncategorized",
                HttpStatusCodeA = pair.HttpStatusCodeA,
                HttpStatusCodeB = pair.HttpStatusCodeB,
                PairOutcome = pair.PairOutcome,
            })
            .ToList();

        var groups = items
            .Select(item => item.Category)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(group => group, StringComparer.Ordinal)
            .ToList();

        return new ComparisonResultProjection
        {
            Items = items,
            AvailableGroups = groups,
            EqualCount = items.Count(item => !item.HasError && item.AreEqual),
            DifferentCount = items.Count(item => !item.HasError && !item.AreEqual),
            ErrorCount = items.Count(item => item.HasError),
            StatusCodeMismatchCount = items.Count(item => item.PairOutcome == RequestPairOutcome.StatusCodeMismatch),
            BothNonSuccessCount = items.Count(item => item.PairOutcome == RequestPairOutcome.BothNonSuccess),
            HasHttpStatusData = items.Any(item => item.PairOutcome != null),
        };
    }

    /// <summary>
    /// Filters projected rows by status, category, and optional file-name search text.
    /// </summary>
    public static IEnumerable<ComparisonResultGridItem> Filter(
        IReadOnlyList<ComparisonResultGridItem>? items,
        ComparisonResultStatusFilter statusFilter,
        string? selectedGroupFilter,
        string? fileNameSearchFilter)
    {
        if (items == null)
        {
            return Enumerable.Empty<ComparisonResultGridItem>();
        }

        var filtered = items.AsEnumerable();

        filtered = statusFilter switch
        {
            ComparisonResultStatusFilter.Equal => filtered.Where(item => !item.HasError && item.AreEqual),
            ComparisonResultStatusFilter.Different => filtered.Where(item => !item.HasError && !item.AreEqual),
            ComparisonResultStatusFilter.Error => filtered.Where(item => item.HasError),
            ComparisonResultStatusFilter.StatusMismatch => filtered.Where(item => item.PairOutcome == RequestPairOutcome.StatusCodeMismatch),
            ComparisonResultStatusFilter.NonSuccess => filtered.Where(item => item.PairOutcome == RequestPairOutcome.BothNonSuccess || item.PairOutcome == RequestPairOutcome.OneOrBothFailed),
            _ => filtered,
        };

        if (!string.IsNullOrWhiteSpace(selectedGroupFilter) && !string.Equals(selectedGroupFilter, "All", StringComparison.Ordinal))
        {
            filtered = filtered.Where(item => string.Equals(item.Category, selectedGroupFilter, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(fileNameSearchFilter))
        {
            filtered = filtered.Where(item =>
                item.File1Name.Contains(fileNameSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                item.File2Name.Contains(fileNameSearchFilter, StringComparison.OrdinalIgnoreCase));
        }

        return filtered;
    }
}
