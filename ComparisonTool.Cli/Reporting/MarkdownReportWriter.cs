using System.Text;
using ComparisonTool.Core.Comparison.Results;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Writes a Markdown-formatted comparison report to a file.
/// When the number of file pairs with differences exceeds the configured page size,
/// overflow pages are written to a details subdirectory and linked from the main report.
/// </summary>
public static class MarkdownReportWriter
{
    private const int MostAffectedFieldsLimit = 15;
    private const int DifferencesPerPairLimit = 50;
    private const int RawTextDiffsPerPairLimit = 20;
    private const int ValueTruncateLength = 60;

    /// <summary>
    /// Writes the comparison result as a Markdown report, with optional pagination.
    /// Returns the number of detail pages written (0 when no pagination occurred).
    /// </summary>
    public static async Task<int> WriteAsync(ReportContext context, string outputPath)
    {
        var result = context.Result;
        var pairs = result.FilePairResults;

        var equalCount = pairs.Count(p => p.AreEqual);
        var diffCount = pairs.Count(p => !p.AreEqual && !p.HasError);
        var errorCount = pairs.Count(p => p.HasError);

        var sb = new StringBuilder();

        sb.AppendLine("# Comparison Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC  ");
        sb.AppendLine($"**Command:** `{context.CommandName}`  ");

        if (!string.IsNullOrEmpty(context.ModelName))
        {
            sb.AppendLine($"**Model:** `{context.ModelName}`  ");
        }

        if (context.CommandName == "folder")
        {
            sb.AppendLine($"**Directory 1:** `{context.Directory1}`  ");
            sb.AppendLine($"**Directory 2:** `{context.Directory2}`  ");
        }
        else if (context.CommandName == "request")
        {
            sb.AppendLine($"**Endpoint A:** `{context.EndpointA}`  ");
            sb.AppendLine($"**Endpoint B:** `{context.EndpointB}`  ");

            if (!string.IsNullOrEmpty(context.JobId))
            {
                sb.AppendLine($"**Job ID:** `{context.JobId}`  ");
            }
        }

        sb.AppendLine($"**Elapsed:** {context.Elapsed.TotalSeconds:F2}s  ");
        sb.AppendLine();

        // Summary table
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Total pairs | {result.TotalPairsCompared} |");
        sb.AppendLine($"| Equal | {equalCount} |");
        sb.AppendLine($"| Different | {diffCount} |");
        sb.AppendLine($"| Errors | {errorCount} |");
        sb.AppendLine($"| All equal | {(result.AllEqual ? "Yes" : "**No**")} |");
        sb.AppendLine();

        // Most affected fields
        sb.AppendLine("## Most Affected Fields");
        sb.AppendLine();

        var mostAffectedFields = context.MostAffectedFields.Fields.Take(MostAffectedFieldsLimit).ToList();
        if (mostAffectedFields.Count == 0)
        {
            sb.AppendLine("No structured field differences were found.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("| Field | Affected Pairs | Occurrences |");
            sb.AppendLine("|-------|----------------|-------------|");

            foreach (var field in mostAffectedFields)
            {
                sb.AppendLine($"| `{EscapeMarkdown(field.FieldPath)}` | {field.AffectedPairCount} | {field.OccurrenceCount} |");
            }

            sb.AppendLine();
        }

        if (context.MostAffectedFields.ExcludedRawTextPairCount > 0)
        {
            sb.AppendLine($"*Excluded raw-text-only pairs: {context.MostAffectedFields.ExcludedRawTextPairCount}*  ");
            sb.AppendLine();
        }

        // File pair results table
        sb.AppendLine("## File Pairs");
        sb.AppendLine();
        sb.AppendLine("| File | Status | Differences | Outcome |");
        sb.AppendLine("|------|--------|-------------|---------|");

        foreach (var pair in pairs.OrderBy(p => p.File1Name, StringComparer.Ordinal))
        {
            var status = pair.HasError ? "Error" : pair.AreEqual ? "Equal" : "Different";
            var diffs = pair.Result?.Differences?.Count ?? pair.RawTextDifferences?.Count ?? 0;
            var outcome = pair.PairOutcome?.ToString() ?? "—";
            sb.AppendLine($"| `{pair.File1Name}` | {status} | {diffs} | {outcome} |");
        }

        sb.AppendLine();

        // Detailed differences — paginated
        var differencePairs = pairs.Where(p => !p.AreEqual && !p.HasError).ToList();
        var pageSize = context.MarkdownPageSize;
        var paginationEnabled = pageSize > 0 && differencePairs.Count > pageSize;
        var firstPageCount = pageSize > 0 ? Math.Min(pageSize, differencePairs.Count) : differencePairs.Count;

        if (differencePairs.Count > 0)
        {
            sb.AppendLine("## Differences");
            sb.AppendLine();

            if (paginationEnabled)
            {
                sb.AppendLine($"*Showing {firstPageCount} of {differencePairs.Count} file pairs with differences (page 1).*");
                sb.AppendLine();
            }

            foreach (var pair in differencePairs.Take(firstPageCount))
            {
                AppendPairDetails(sb, pair);
            }
        }

        // Pagination: write overflow pages and add navigation links
        var detailPageCount = 0;
        string? detailsDirName = null;

        if (paginationEnabled)
        {
            var reportBaseName = Path.GetFileNameWithoutExtension(outputPath);
            detailsDirName = $"{reportBaseName}-details";
            var detailsDirPath = Path.Combine(Path.GetDirectoryName(outputPath)!, detailsDirName);
            Directory.CreateDirectory(detailsDirPath);

            var overflow = differencePairs.Skip(pageSize).ToList();
            var totalPages = (int)Math.Ceiling((double)differencePairs.Count / pageSize);
            detailPageCount = totalPages - 1;

            // Write each overflow page
            for (var pageIndex = 1; pageIndex < totalPages; pageIndex++)
            {
                var pageNumber = pageIndex + 1;
                var pagePairs = overflow
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                var rangeStart = pageIndex * pageSize + 1;
                var rangeEnd = Math.Min(rangeStart + pagePairs.Count - 1, differencePairs.Count);
                var pageFileName = $"page-{pageNumber:D3}.md";
                var pageFilePath = Path.Combine(detailsDirPath, pageFileName);

                var pageSb = new StringBuilder();
                pageSb.AppendLine($"# Comparison Report — Differences (Page {pageNumber} of {totalPages})");
                pageSb.AppendLine();
                pageSb.AppendLine($"*Pairs {rangeStart}–{rangeEnd} of {differencePairs.Count}*  ");
                pageSb.AppendLine();
                pageSb.AppendLine($"[← Back to main report](../{Path.GetFileName(outputPath)})");

                if (pageNumber > 2)
                {
                    pageSb.AppendLine($"  |  [← Previous page](page-{pageNumber - 1:D3}.md)");
                }
                else
                {
                    pageSb.AppendLine($"  |  [← Page 1 (main report)](../{Path.GetFileName(outputPath)})");
                }

                if (pageNumber < totalPages)
                {
                    pageSb.AppendLine($"  |  [Next page →](page-{pageNumber + 1:D3}.md)");
                }

                pageSb.AppendLine();
                pageSb.AppendLine("---");
                pageSb.AppendLine();

                foreach (var pair in pagePairs)
                {
                    AppendPairDetails(pageSb, pair);
                }

                // Footer navigation
                pageSb.AppendLine("---");
                pageSb.AppendLine();
                pageSb.AppendLine($"[← Back to main report](../{Path.GetFileName(outputPath)})");

                if (pageNumber < totalPages)
                {
                    pageSb.AppendLine($"  |  [Next page →](page-{pageNumber + 1:D3}.md)");
                }

                pageSb.AppendLine();

                await File.WriteAllTextAsync(pageFilePath, pageSb.ToString());
            }

            // Add "Continued In" navigation to the main report
            var totalOverflowPages = totalPages - 1;
            sb.AppendLine("## Continued In");
            sb.AppendLine();

            for (var pageIndex = 1; pageIndex <= totalOverflowPages; pageIndex++)
            {
                var pageNumber = pageIndex + 1;
                var rangeStart = pageIndex * pageSize + 1;
                var rangeEnd = Math.Min(rangeStart + pageSize - 1, differencePairs.Count);
                sb.AppendLine($"- [Page {pageNumber} (pairs {rangeStart}–{rangeEnd})]({detailsDirName}/page-{pageNumber:D3}.md)");
            }

            sb.AppendLine();
        }

        // Errors
        var errorPairs = pairs.Where(p => p.HasError).ToList();
        if (errorPairs.Count > 0)
        {
            sb.AppendLine("## Errors");
            sb.AppendLine();

            foreach (var pair in errorPairs)
            {
                sb.AppendLine($"- **{pair.File1Name}**: {EscapeMarkdown(pair.ErrorMessage ?? "Unknown error")}");
            }

            sb.AppendLine();
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString());

        return detailPageCount;
    }

    /// <summary>
    /// Appends the detailed difference section for a single file pair to the string builder.
    /// </summary>
    private static void AppendPairDetails(StringBuilder sb, FilePairComparisonResult pair)
    {
        var outcomeTag = pair.PairOutcome != null ? $" ({pair.PairOutcome})" : string.Empty;
        sb.AppendLine($"### {pair.File1Name}{outcomeTag}");
        sb.AppendLine();

        if (pair.Result?.Differences != null && pair.Result.Differences.Count > 0)
        {
            sb.AppendLine("| Property | Expected | Actual |");
            sb.AppendLine("|----------|----------|--------|");

            foreach (var diff in pair.Result.Differences.Take(DifferencesPerPairLimit))
            {
                var expected = EscapeMarkdown(Truncate(diff.Object1Value, ValueTruncateLength));
                var actual = EscapeMarkdown(Truncate(diff.Object2Value, ValueTruncateLength));
                sb.AppendLine($"| `{diff.PropertyName}` | {expected} | {actual} |");
            }

            if (pair.Result.Differences.Count > DifferencesPerPairLimit)
            {
                sb.AppendLine();
                sb.AppendLine($"*... and {pair.Result.Differences.Count - DifferencesPerPairLimit} more differences*");
            }
        }
        else if (pair.RawTextDifferences != null && pair.RawTextDifferences.Count > 0)
        {
            sb.AppendLine("**Raw text differences:**");
            sb.AppendLine();

            foreach (var diff in pair.RawTextDifferences.Take(RawTextDiffsPerPairLimit))
            {
                sb.AppendLine($"- **{diff.Type}** (line A:{diff.LineNumberA ?? 0}, B:{diff.LineNumberB ?? 0}): {EscapeMarkdown(diff.Description)}");
            }

            if (pair.RawTextDifferences.Count > RawTextDiffsPerPairLimit)
            {
                sb.AppendLine($"- *... and {pair.RawTextDifferences.Count - RawTextDiffsPerPairLimit} more*");
            }
        }

        sb.AppendLine();
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(null)";
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static string EscapeMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("|", "\\|")
            .Replace("\n", " ")
            .Replace("\r", string.Empty);
    }
}
