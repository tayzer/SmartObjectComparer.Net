using System.Text;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Writes a Markdown-formatted comparison report to a file.
/// </summary>
public static class MarkdownReportWriter
{
    /// <summary>
    /// Writes the comparison result as a Markdown report.
    /// </summary>
    public static async Task WriteAsync(ReportContext context, string outputPath)
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

        // Detailed differences
        var differencePairs = pairs.Where(p => !p.AreEqual && !p.HasError).ToList();
        if (differencePairs.Count > 0)
        {
            sb.AppendLine("## Differences");
            sb.AppendLine();

            foreach (var pair in differencePairs.Take(50))
            {
                var outcomeTag = pair.PairOutcome != null ? $" ({pair.PairOutcome})" : string.Empty;
                sb.AppendLine($"### {pair.File1Name}{outcomeTag}");
                sb.AppendLine();

                if (pair.Result?.Differences != null && pair.Result.Differences.Count > 0)
                {
                    sb.AppendLine("| Property | Expected | Actual |");
                    sb.AppendLine("|----------|----------|--------|");

                    foreach (var diff in pair.Result.Differences.Take(50))
                    {
                        var expected = EscapeMarkdown(Truncate(diff.Object1Value, 60));
                        var actual = EscapeMarkdown(Truncate(diff.Object2Value, 60));
                        sb.AppendLine($"| `{diff.PropertyName}` | {expected} | {actual} |");
                    }

                    if (pair.Result.Differences.Count > 50)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"*... and {pair.Result.Differences.Count - 50} more differences*");
                    }
                }
                else if (pair.RawTextDifferences != null && pair.RawTextDifferences.Count > 0)
                {
                    sb.AppendLine("**Raw text differences:**");
                    sb.AppendLine();

                    foreach (var diff in pair.RawTextDifferences.Take(20))
                    {
                        sb.AppendLine($"- **{diff.Type}** (line A:{diff.LineNumberA ?? 0}, B:{diff.LineNumberB ?? 0}): {EscapeMarkdown(diff.Description)}");
                    }

                    if (pair.RawTextDifferences.Count > 20)
                    {
                        sb.AppendLine($"- *... and {pair.RawTextDifferences.Count - 20} more*");
                    }
                }

                sb.AppendLine();
            }

            if (differencePairs.Count > 50)
            {
                sb.AppendLine($"*... {differencePairs.Count - 50} more file pairs with differences omitted.*");
                sb.AppendLine();
            }
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
