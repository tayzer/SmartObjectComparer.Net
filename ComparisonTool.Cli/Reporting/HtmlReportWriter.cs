using System.Text;
using ComparisonTool.Core.Comparison.Presentation;
using ComparisonTool.Core.Comparison.Results;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Writes interactive HTML reports for comparison results.
/// </summary>
public static class HtmlReportWriter
{
    private const int StructuredDifferencesLimit = 150;
    private const int RawTextDifferencesLimit = 150;
    private const int MostAffectedFieldsLimit = 30;

    /// <summary>
    /// Writes an HTML report and returns output metadata.
    /// </summary>
    public static async Task<HtmlReportWriteResult> WriteAsync(ReportContext context, string outputPath)
    {
        return context.HtmlMode == HtmlReportMode.StaticSite
            ? await WriteStaticSiteAsync(context, outputPath)
            : await WriteSingleFileAsync(context, outputPath);
    }

    private static async Task<HtmlReportWriteResult> WriteSingleFileAsync(ReportContext context, string outputPath)
    {
        var resolvedPath = EnsureHtmlExtension(outputPath);
        var html = BuildSingleFileHtml(context);
        await File.WriteAllTextAsync(resolvedPath, html);

        return new HtmlReportWriteResult
        {
            PrimaryPath = resolvedPath,
            DetailPageCount = 0,
        };
    }

    private static async Task<HtmlReportWriteResult> WriteStaticSiteAsync(ReportContext context, string outputPath)
    {
        var siteDirectory = outputPath;
        Directory.CreateDirectory(siteDirectory);

        var indexPath = Path.Combine(siteDirectory, "index.html");
        var pairsDirectory = Path.Combine(siteDirectory, "pairs");
        Directory.CreateDirectory(pairsDirectory);

        var result = context.Result;
        for (var pairIndex = 0; pairIndex < result.FilePairResults.Count; pairIndex++)
        {
            var pair = result.FilePairResults[pairIndex];
            var pairFileName = GetPairFileName(pairIndex);
            var pairPath = Path.Combine(pairsDirectory, pairFileName);
            var pairHtml = BuildPairPageHtml(context, pair, pairIndex);
            await File.WriteAllTextAsync(pairPath, pairHtml);
        }

        var indexHtml = BuildStaticSiteIndexHtml(context);
        await File.WriteAllTextAsync(indexPath, indexHtml);

        return new HtmlReportWriteResult
        {
            PrimaryPath = indexPath,
            DetailPageCount = result.FilePairResults.Count,
        };
    }

    private static string BuildSingleFileHtml(ReportContext context)
    {
        var result = context.Result;
        var projection = ComparisonResultProjectionBuilder.Build(result);
        var sb = new StringBuilder();
        AppendDocumentHeader(sb, "Request Comparison Report", includeScrollScript: true);
        AppendSummarySection(sb, context, projection);
        AppendCommonDifferencesSection(sb, context);
        AppendFilterSection(sb, projection);
        AppendPairsTableSection(sb, projection, staticSite: false);
        AppendSingleFilePairNavigation(sb, projection);

        sb.AppendLine("<section id=\"details\">\n<h2>Detailed Differences</h2>");
        for (var pairIndex = 0; pairIndex < result.FilePairResults.Count; pairIndex++)
        {
            AppendPairDetailsSection(sb, result.FilePairResults[pairIndex], pairIndex, staticSite: false);
        }

        sb.AppendLine("</section>");
        AppendFilterScript(sb, staticSite: false);
        AppendDocumentFooter(sb);

        return sb.ToString();
    }

    private static string BuildStaticSiteIndexHtml(ReportContext context)
    {
        var result = context.Result;
        var projection = ComparisonResultProjectionBuilder.Build(result);
        var sb = new StringBuilder();
        AppendDocumentHeader(sb, "Request Comparison Report", includeScrollScript: false);
        AppendSummarySection(sb, context, projection);
        AppendCommonDifferencesSection(sb, context);
        AppendFilterSection(sb, projection);
        AppendPairsTableSection(sb, projection, staticSite: true);
        AppendFilterScript(sb, staticSite: true);
        AppendDocumentFooter(sb);
        return sb.ToString();
    }

    private static string BuildPairPageHtml(ReportContext context, FilePairComparisonResult pair, int pairIndex)
    {
        var title = $"Pair {pairIndex + 1}: {GetPairTitle(pair)}";
        var sb = new StringBuilder();
        AppendDocumentHeader(sb, title, includeScrollScript: false);
        sb.AppendLine("<p><a href=\"../index.html\">&larr; Back to report index</a></p>");
        AppendPairDetailsSection(sb, pair, pairIndex, staticSite: true);
        AppendDocumentFooter(sb);
        return sb.ToString();
    }

    private static void AppendDocumentHeader(StringBuilder sb, string title, bool includeScrollScript)
    {
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine($"  <title>{EscapeHtml(title)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f7f9fb;color:#1f2937}");
        sb.AppendLine("    main{max-width:1400px;margin:0 auto;padding:24px}");
        sb.AppendLine("    h1,h2,h3{margin-top:0}");
        sb.AppendLine("    .grid{display:grid;grid-template-columns:repeat(5,minmax(120px,1fr));gap:12px}");
        sb.AppendLine("    .card{background:#fff;border:1px solid #dbe2ea;border-radius:8px;padding:12px}");
        sb.AppendLine("    .muted{color:#5f6b7a}");
        sb.AppendLine("    table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #dbe2ea}");
        sb.AppendLine("    th,td{padding:8px 10px;border-bottom:1px solid #e8edf2;text-align:left;vertical-align:top}");
        sb.AppendLine("    th{background:#f0f4f8;position:sticky;top:0;z-index:1}");
        sb.AppendLine("    tr.status-Equal td{background:#f0fdf4}");
        sb.AppendLine("    tr.status-Different td{background:#fff7ed}");
        sb.AppendLine("    tr.status-Error td{background:#fef2f2}");
        sb.AppendLine("    .chip{display:inline-block;padding:2px 8px;border-radius:999px;border:1px solid #c5d0db;font-size:12px;background:#f9fbfd}");
        sb.AppendLine("    .controls{display:flex;flex-wrap:wrap;gap:8px;margin:16px 0}");
        sb.AppendLine("    input,select{padding:8px 10px;border:1px solid #c5d0db;border-radius:6px;background:#fff}");
        sb.AppendLine("    details{background:#fff;border:1px solid #dbe2ea;border-radius:8px;padding:10px;margin:10px 0}");
        sb.AppendLine("    .pair-nav{columns:2;column-gap:18px}");
        sb.AppendLine("    .pair-nav a{display:block;padding:2px 0}");
        sb.AppendLine("    code{font-family:Cascadia Mono,Consolas,monospace;font-size:12px}");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<main>");
        sb.AppendLine($"<h1>{EscapeHtml(title)}</h1>");

        if (includeScrollScript)
        {
            sb.AppendLine("<script>function scrollToPair(id){var e=document.getElementById(id);if(e){e.scrollIntoView({behavior:'smooth',block:'start'});}}</script>");
        }
    }

    private static void AppendSummarySection(StringBuilder sb, ReportContext context, ComparisonResultProjection projection)
    {
        var result = context.Result;

        sb.AppendLine("<section>");
        sb.AppendLine($"<p class=\"muted\">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");
        sb.AppendLine("<div class=\"grid\">");
        sb.AppendLine($"<div class=\"card\"><strong>Total</strong><div>{result.TotalPairsCompared}</div></div>");
        sb.AppendLine($"<div class=\"card\"><strong>Equal</strong><div>{projection.EqualCount}</div></div>");
        sb.AppendLine($"<div class=\"card\"><strong>Different</strong><div>{projection.DifferentCount}</div></div>");
        sb.AppendLine($"<div class=\"card\"><strong>Errors</strong><div>{projection.ErrorCount}</div></div>");
        sb.AppendLine($"<div class=\"card\"><strong>Status Mismatch</strong><div>{projection.StatusCodeMismatchCount}</div><div class=\"muted\">Both Non-Success: {projection.BothNonSuccessCount}</div></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<p class=\"muted\">Command: <code>" + EscapeHtml(context.CommandName) + "</code></p>");
        if (!string.IsNullOrWhiteSpace(context.EndpointA) && !string.IsNullOrWhiteSpace(context.EndpointB))
        {
            sb.AppendLine("<p class=\"muted\">Endpoint A: <code>" + EscapeHtml(context.EndpointA) + "</code><br/>Endpoint B: <code>" + EscapeHtml(context.EndpointB) + "</code></p>");
        }

        if (!string.IsNullOrWhiteSpace(context.JobId))
        {
            sb.AppendLine("<p class=\"muted\">Job ID: <code>" + EscapeHtml(context.JobId) + "</code></p>");
        }

        sb.AppendLine("</section>");
    }

    private static void AppendCommonDifferencesSection(StringBuilder sb, ReportContext context)
    {
        sb.AppendLine("<section>");
        sb.AppendLine("<h2>Common Differences</h2>");

        var fields = context.MostAffectedFields.Fields.Take(MostAffectedFieldsLimit).ToList();
        if (fields.Count == 0)
        {
            sb.AppendLine("<p class=\"muted\">No structured field differences were found.</p>");
            sb.AppendLine("</section>");
            return;
        }

        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th>Field</th><th>Affected Pairs</th><th>Occurrences</th></tr></thead>");
        sb.AppendLine("<tbody>");
        foreach (var field in fields)
        {
            sb.AppendLine($"<tr><td><code>{EscapeHtml(field.FieldPath)}</code></td><td>{field.AffectedPairCount}</td><td>{field.OccurrenceCount}</td></tr>");
        }

        sb.AppendLine("</tbody></table>");
        if (context.MostAffectedFields.ExcludedRawTextPairCount > 0)
        {
            sb.AppendLine($"<p class=\"muted\">Excluded raw-text-only pairs: {context.MostAffectedFields.ExcludedRawTextPairCount}</p>");
        }

        sb.AppendLine("</section>");
    }

    private static void AppendFilterSection(StringBuilder sb, ComparisonResultProjection projection)
    {
        sb.AppendLine("<section>");
        sb.AppendLine("<h2>Request Comparison Results</h2>");
        sb.AppendLine("<div class=\"controls\">");
        sb.AppendLine("<input id=\"searchInput\" type=\"text\" placeholder=\"Search by file name...\" />");
        sb.AppendLine("<select id=\"statusFilter\"><option value=\"All\">All Statuses</option><option value=\"Equal\">Equal</option><option value=\"Different\">Different</option><option value=\"Error\">Error</option>");

        if (projection.HasHttpStatusData)
        {
            sb.AppendLine("<option value=\"StatusMismatch\">Status Mismatch</option><option value=\"NonSuccess\">Non-Success</option>");
        }

        sb.AppendLine("</select>");
        sb.AppendLine("<select id=\"groupFilter\"><option value=\"All\">All Categories</option>");
        foreach (var group in projection.AvailableGroups)
        {
            sb.AppendLine($"<option value=\"{EscapeHtml(group)}\">{EscapeHtml(group)}</option>");
        }

        sb.AppendLine("</select>");
        sb.AppendLine("</div>");
        sb.AppendLine("</section>");
    }

    private static void AppendPairsTableSection(StringBuilder sb, ComparisonResultProjection projection, bool staticSite)
    {
        sb.AppendLine("<section>");
        sb.AppendLine("<table id=\"pairsTable\">");
        sb.AppendLine("<thead><tr><th>#</th><th>File</th><th>Status</th><th>Outcome</th><th>HTTP</th><th>Differences</th><th>Category</th><th>Action</th></tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var item in projection.Items)
        {
            var status = GetStatus(item);
            var outcome = item.PairOutcome?.ToString() ?? "None";
            var httpStatus = item.HttpStatusCodeA.HasValue && item.HttpStatusCodeB.HasValue
                ? $"A:{item.HttpStatusCodeA.Value} / B:{item.HttpStatusCodeB.Value}"
                : "-";
            var title = EscapeHtml(GetPairTitle(item));
            var action = staticSite
                ? $"<a href=\"pairs/{GetPairFileName(item.OriginalIndex)}\">Open</a>"
                : $"<a href=\"#pair-{item.OriginalIndex + 1}\" onclick=\"scrollToPair('pair-{item.OriginalIndex + 1}')\">View</a>";

            sb.AppendLine($"<tr class=\"status-{status}\" data-file=\"{title}\" data-status=\"{status}\" data-category=\"{EscapeHtml(item.Category)}\" data-outcome=\"{outcome}\">" +
                          $"<td>{item.OriginalIndex + 1}</td><td>{title}</td><td><span class=\"chip\">{status}</span></td><td>{EscapeHtml(outcome)}</td><td>{EscapeHtml(httpStatus)}</td><td>{item.DifferenceCount}</td><td>{EscapeHtml(item.Category)}</td><td>{action}</td></tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</section>");
    }

    private static void AppendSingleFilePairNavigation(StringBuilder sb, ComparisonResultProjection projection)
    {
        sb.AppendLine("<section>");
        sb.AppendLine("<h2>Per-File Navigation</h2>");
        sb.AppendLine("<div class=\"pair-nav\">");

        foreach (var item in projection.Items)
        {
            var label = EscapeHtml($"#{item.OriginalIndex + 1} {GetPairTitle(item)}");
            sb.AppendLine($"<a href=\"#pair-{item.OriginalIndex + 1}\">{label}</a>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("</section>");
    }

    private static void AppendPairDetailsSection(StringBuilder sb, FilePairComparisonResult pair, int pairIndex, bool staticSite)
    {
        var title = EscapeHtml(GetPairTitle(pair));
        var status = EscapeHtml(GetStatus(pair));
        var outcome = EscapeHtml(pair.PairOutcome?.ToString() ?? "None");
        var sectionId = $"pair-{pairIndex + 1}";

        sb.AppendLine($"<section id=\"{sectionId}\">");
        sb.AppendLine($"<h3>Pair {pairIndex + 1}: {title}</h3>");
        sb.AppendLine($"<p class=\"muted\">Status: <span class=\"chip\">{status}</span> | Outcome: <span class=\"chip\">{outcome}</span></p>");

        if (!staticSite)
        {
            sb.AppendLine("<p><a href=\"#details\">Back to details top</a></p>");
        }

        if (!string.IsNullOrWhiteSpace(pair.ErrorMessage))
        {
            sb.AppendLine("<details open>");
            sb.AppendLine("<summary>Error</summary>");
            sb.AppendLine("<p><strong>Type:</strong> " + EscapeHtml(pair.ErrorType ?? "Unknown") + "</p>");
            sb.AppendLine("<p><strong>Message:</strong> " + EscapeHtml(pair.ErrorMessage) + "</p>");
            sb.AppendLine("</details>");
        }

        var structuredDifferences = pair.Result?.Differences;
        if (structuredDifferences != null && structuredDifferences.Count > 0)
        {
            sb.AppendLine("<details open>");
            sb.AppendLine("<summary>Detailed Structured Differences</summary>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>Property</th><th>Expected</th><th>Actual</th></tr></thead><tbody>");
            foreach (var diff in structuredDifferences.Take(StructuredDifferencesLimit))
            {
                sb.AppendLine($"<tr><td><code>{EscapeHtml(diff.PropertyName)}</code></td><td>{EscapeHtml(Truncate(diff.Object1Value, 240))}</td><td>{EscapeHtml(Truncate(diff.Object2Value, 240))}</td></tr>");
            }

            if (structuredDifferences.Count > StructuredDifferencesLimit)
            {
                sb.AppendLine($"<tr><td colspan=\"3\" class=\"muted\">... and {structuredDifferences.Count - StructuredDifferencesLimit} more differences</td></tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</details>");
        }

        var rawTextDifferences = pair.RawTextDifferences;
        if (rawTextDifferences != null && rawTextDifferences.Count > 0)
        {
            sb.AppendLine("<details open>");
            sb.AppendLine("<summary>Raw Text Differences</summary>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>Type</th><th>Line A</th><th>Line B</th><th>Description</th><th>Text A</th><th>Text B</th></tr></thead><tbody>");
            foreach (var diff in rawTextDifferences.Take(RawTextDifferencesLimit))
            {
                sb.AppendLine($"<tr><td>{EscapeHtml(diff.Type.ToString())}</td><td>{diff.LineNumberA?.ToString() ?? "-"}</td><td>{diff.LineNumberB?.ToString() ?? "-"}</td><td>{EscapeHtml(diff.Description)}</td><td>{EscapeHtml(Truncate(diff.TextA, 240))}</td><td>{EscapeHtml(Truncate(diff.TextB, 240))}</td></tr>");
            }

            if (rawTextDifferences.Count > RawTextDifferencesLimit)
            {
                sb.AppendLine($"<tr><td colspan=\"6\" class=\"muted\">... and {rawTextDifferences.Count - RawTextDifferencesLimit} more raw text differences</td></tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</details>");
        }

        sb.AppendLine("</section>");
    }

    private static void AppendFilterScript(StringBuilder sb, bool staticSite)
    {
        sb.AppendLine("<script>");
        sb.AppendLine("(function(){");
        sb.AppendLine("  var searchInput=document.getElementById('searchInput');");
        sb.AppendLine("  var statusFilter=document.getElementById('statusFilter');");
        sb.AppendLine("  var groupFilter=document.getElementById('groupFilter');");
        sb.AppendLine("  var table=document.getElementById('pairsTable');");
        sb.AppendLine("  if(!table){return;}");
        sb.AppendLine("  var rows=table.querySelectorAll('tbody tr');");
        sb.AppendLine("  function applyFilters(){");
        sb.AppendLine("    var search=(searchInput.value||'').toLowerCase();");
        sb.AppendLine("    var status=statusFilter.value;");
        sb.AppendLine("    var group=groupFilter.value;");
        sb.AppendLine("    rows.forEach(function(row){");
        sb.AppendLine("      var rowFile=(row.getAttribute('data-file')||'').toLowerCase();");
        sb.AppendLine("      var rowStatus=row.getAttribute('data-status')||'';");
        sb.AppendLine("      var rowCategory=row.getAttribute('data-category')||'';");
        sb.AppendLine("      var rowOutcome=row.getAttribute('data-outcome')||'';");
        sb.AppendLine("      var matchesSearch=search.length===0||rowFile.indexOf(search)>=0;");
        sb.AppendLine("      var statusMapsToOutcome=status==='StatusMismatch'||status==='NonSuccess';");
        sb.AppendLine("      var matchesStatus=status==='All'||rowStatus===status||(statusMapsToOutcome&&rowOutcome===mapStatus(status));");
        sb.AppendLine("      var matchesGroup=group==='All'||rowCategory===group;");
        sb.AppendLine("      row.style.display=matchesSearch&&matchesStatus&&matchesGroup?'':'none';");
        sb.AppendLine("    });");
        sb.AppendLine("  }");
        sb.AppendLine("  function mapStatus(value){ if(value==='StatusMismatch'){ return 'StatusCodeMismatch'; } if(value==='NonSuccess'){ return 'BothNonSuccess'; } return value; }");
        sb.AppendLine("  searchInput.addEventListener('input',applyFilters);");
        sb.AppendLine("  statusFilter.addEventListener('change',applyFilters);");
        sb.AppendLine("  groupFilter.addEventListener('change',applyFilters);");
        sb.AppendLine("  applyFilters();");

        if (!staticSite)
        {
            sb.AppendLine("  window.scrollToPair=window.scrollToPair||function(){};");
        }

        sb.AppendLine("})();");
        sb.AppendLine("</script>");
    }

    private static void AppendDocumentFooter(StringBuilder sb)
    {
        sb.AppendLine("</main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
    }

    private static string GetStatus(ComparisonResultGridItem item)
    {
        if (item.HasError)
        {
            return "Error";
        }

        if (item.AreEqual)
        {
            return "Equal";
        }

        return "Different";
    }

    private static string GetStatus(FilePairComparisonResult pair)
    {
        if (pair.HasError)
        {
            return "Error";
        }

        if (pair.AreEqual)
        {
            return "Equal";
        }

        return "Different";
    }

    private static string GetPairFileName(int pairIndex)
    {
        return $"pair-{pairIndex + 1:D4}.html";
    }

    private static string GetPairTitle(ComparisonResultGridItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.RequestRelativePath))
        {
            return item.RequestRelativePath;
        }

        if (!string.IsNullOrWhiteSpace(item.File1Name) && !string.IsNullOrWhiteSpace(item.File2Name))
        {
            return $"{item.File1Name} vs {item.File2Name}";
        }

        return item.File1Name;
    }

    private static string GetPairTitle(FilePairComparisonResult pair)
    {
        if (!string.IsNullOrWhiteSpace(pair.RequestRelativePath))
        {
            return pair.RequestRelativePath;
        }

        if (!string.IsNullOrWhiteSpace(pair.File1Name) && !string.IsNullOrWhiteSpace(pair.File2Name))
        {
            return $"{pair.File1Name} vs {pair.File2Name}";
        }

        return pair.File1Name;
    }

    private static string EnsureHtmlExtension(string outputPath)
    {
        return outputPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : outputPath + ".html";
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(null)";
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static string EscapeHtml(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
    }
}
