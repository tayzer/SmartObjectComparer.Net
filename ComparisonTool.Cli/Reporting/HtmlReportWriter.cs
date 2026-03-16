using System.Reflection;
using System.Text.Json;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Writes a self-contained static HTML report powered by the embedded React UI template.
/// </summary>
public static class HtmlReportWriter
{
    private const string ResourceSuffix = "Resources.ReportTemplate.html";
    private const string ReportDataPlaceholder = "__REPORT_DATA_JSON__";

    /// <summary>
    /// Writes a standalone HTML report file.
    /// </summary>
    /// <param name="context">The report generation context.</param>
    /// <param name="outputPath">The destination HTML file path.</param>
    /// <returns>A task that completes when the file has been written.</returns>
    public static async Task WriteAsync(ReportContext context, string outputPath)
    {
        var template = await ReadTemplateAsync();

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        HtmlReportBootstrapDto bootstrap;

        if (context.HtmlMode == HtmlReportMode.StaticSite)
        {
            var baseDirectory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
            var dataRootPath = BuildDataRootPath(outputPath);
            var dataDirectory = Path.Combine(
                baseDirectory,
                dataRootPath);

            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }

            Directory.CreateDirectory(dataDirectory);

            bootstrap = await HtmlReportBundleBuilder.WriteStaticSiteAsync(context, dataRootPath, baseDirectory);
        }
        else
        {
            bootstrap = HtmlReportBundleBuilder.BuildSingleFile(context).Bootstrap;
        }

        var reportJson = JsonSerializer.Serialize(bootstrap, ComparisonReportJson.CompactOptions);
        var html = template.Replace(ReportDataPlaceholder, reportJson, StringComparison.Ordinal);

        await File.WriteAllTextAsync(outputPath, html);
    }

    private static string BuildDataRootPath(string outputPath)
    {
        return $"{Path.GetFileNameWithoutExtension(outputPath)}.data";
    }

    private static async Task<string> ReadTemplateAsync()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));

        if (resourceName == null)
        {
            throw new InvalidOperationException("Embedded HTML report template was not found in the CLI assembly.");
        }

        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Unable to open embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}