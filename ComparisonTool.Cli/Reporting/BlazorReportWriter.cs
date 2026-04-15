using System.Reflection;
using System.Text;
using ComparisonTool.Core.Comparison.Analysis;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Writes a self-contained Blazor WASM report as a static-site folder.
/// The output folder contains the pre-published Blazor app with comparison data
/// injected into the <c>index.html</c>.
/// </summary>
internal static class BlazorReportWriter
{
    private const string ReportDataPlaceholder = "__REPORT_DATA_JSON__";
    private const string BlazorAssetsSubdirectory = "BlazorReportAssets";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes a Blazor WASM report folder to the specified output directory.
    /// </summary>
    /// <param name="context">The report generation context.</param>
    /// <param name="outputDirectory">The destination folder for the report.</param>
    /// <param name="enhancedAnalysis">Optional enhanced structural analysis result.</param>
    /// <returns>The path to the generated index.html file.</returns>
    public static async Task<string> WriteAsync(
        ReportContext context,
        string outputDirectory,
        EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult? enhancedAnalysis = null)
    {
        var blazorAssetsDir = ResolveBlazorAssetsDirectory();
        if (blazorAssetsDir == null)
        {
            throw new InvalidOperationException(
                "Blazor report assets not found. Ensure the ComparisonTool.Report project has been published " +
                "and the BlazorReportAssets directory exists alongside the CLI executable.");
        }

        Directory.CreateDirectory(outputDirectory);

        // Copy all Blazor WASM assets (except index.html) to the output directory.
        CopyDirectory(blazorAssetsDir, outputDirectory, excludeFileName: "index.html");

        // Read the template index.html, inject the report data JSON, and write to output.
        var templatePath = Path.Combine(blazorAssetsDir, "index.html");
        if (!File.Exists(templatePath))
        {
            throw new InvalidOperationException(
                $"Blazor report template index.html not found at '{templatePath}'.");
        }

        var template = await File.ReadAllTextAsync(templatePath, Utf8WithoutBom);
        var reportJson = BlazorReportBundleBuilder.BuildJson(context, enhancedAnalysis);

        var injected = template.Replace(ReportDataPlaceholder, reportJson);

        var indexPath = Path.Combine(outputDirectory, "index.html");
        await File.WriteAllTextAsync(indexPath, injected, Utf8WithoutBom);

        await WriteLauncherScriptsAsync(outputDirectory);

        return indexPath;
    }

    private static string? ResolveBlazorAssetsDirectory()
    {
        // First, check alongside the CLI executable.
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(exeDir))
        {
            var candidate = Path.Combine(exeDir, BlazorAssetsSubdirectory);
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "_framework")))
            {
                return candidate;
            }
        }

        // Fallback: check relative to the working directory (dev-time builds).
        var devCandidate = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, BlazorAssetsSubdirectory));
        if (Directory.Exists(devCandidate) && Directory.Exists(Path.Combine(devCandidate, "_framework")))
        {
            return devCandidate;
        }

        return null;
    }

    private static void CopyDirectory(string sourceDir, string targetDir, string? excludeFileName = null)
    {
        foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, dirPath);
            Directory.CreateDirectory(Path.Combine(targetDir, relativePath));
        }

        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (excludeFileName != null &&
                string.Equals(Path.GetFileName(filePath), excludeFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceDir, filePath);
            var destPath = Path.Combine(targetDir, relativePath);
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(filePath, destPath, overwrite: true);
        }
    }

    private static async Task WriteLauncherScriptsAsync(string outputDirectory)
    {
        var cmdScript = """
@echo off
echo Starting local server for Comparison Report...
echo.
echo Report will open at http://localhost:8890
echo Press Ctrl+C to stop the server.
echo.
where dotnet >nul 2>&1
if %errorlevel%==0 (
    start http://localhost:8890
    dotnet serve -p 8890 -d "%~dp0"
) else (
    where python >nul 2>&1
    if %errorlevel%==0 (
        start http://localhost:8890
        python -m http.server 8890 -d "%~dp0"
    ) else (
        echo No HTTP server found. Install .NET SDK or Python to serve this report.
        echo Alternatively, open this folder in an HTTP server or upload to a web server.
        pause
    )
)
""";

        var shScript = """
#!/bin/bash
echo "Starting local server for Comparison Report..."
echo ""
echo "Report will open at http://localhost:8890"
echo "Press Ctrl+C to stop the server."
echo ""
DIR="$(cd "$(dirname "$0")" && pwd)"
if command -v dotnet &> /dev/null; then
    dotnet serve -p 8890 -d "$DIR" &
    sleep 1 && xdg-open http://localhost:8890 2>/dev/null || open http://localhost:8890 2>/dev/null
    wait
elif command -v python3 &> /dev/null; then
    python3 -m http.server 8890 -d "$DIR" &
    sleep 1 && xdg-open http://localhost:8890 2>/dev/null || open http://localhost:8890 2>/dev/null
    wait
else
    echo "No HTTP server found. Install .NET SDK or Python to serve this report."
fi
""";

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "serve.cmd"),
            cmdScript,
            Utf8WithoutBom);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "serve.sh"),
            shScript,
            Utf8WithoutBom);
    }
}
