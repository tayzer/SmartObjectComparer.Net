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
    /// Also writes a top-level .html redirector file next to the folder for easy
    /// access from Jenkins artifacts or file browsers.
    /// </summary>
    /// <param name="context">The report generation context.</param>
    /// <param name="outputDirectory">The destination folder for the report.</param>
    /// <param name="enhancedAnalysis">Optional enhanced structural analysis result.</param>
    /// <returns>The path to the top-level redirector .html file.</returns>
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

        // Write a top-level .html file that redirects into the report folder.
        // This gives Jenkins/CI a single clickable .html artifact.
        var redirectPath = outputDirectory + ".html";
        var folderName = Path.GetFileName(outputDirectory);
        await WriteRedirectorHtmlAsync(redirectPath, folderName);

        return redirectPath;
    }

    private static async Task WriteRedirectorHtmlAsync(string redirectPath, string reportFolderName)
    {
        var html = string.Join(
            Environment.NewLine,
            "<!DOCTYPE html>",
            "<html lang=\"en\">",
            "<head>",
            "    <meta charset=\"utf-8\" />",
            "    <title>Comparison Report</title>",
            "</head>",
            "<body>",
            "    <div id=\"message\" style=\"font-family:Segoe UI,Roboto,sans-serif;padding:32px;line-height:1.6;color:#102a43;max-width:760px;margin:0 auto;\">",
            $"        <p>Loading report... If it does not open automatically, <a href=\"{reportFolderName}/index.html\">click here</a>.</p>",
            "    </div>",
            "    <script>",
            "        if (window.location.protocol === 'file:') {",
            "            document.getElementById('message').innerHTML = '<h2 style=\"margin:0 0 12px;\">This report cannot be opened directly from disk</h2><p>Blazor WebAssembly reports must be served over HTTP. Jenkins artifacts work because Jenkins serves them over HTTP.</p><p>For local testing, serve this folder with a local web server and open <strong>" + reportFolderName + ".html</strong> through that server.</p>';",
            "        } else {",
            $"            window.location.replace(\"{reportFolderName}/index.html\");",
            "        }",
            "    </script>",
            "</body>",
            "</html>");

        await File.WriteAllTextAsync(redirectPath, html, Utf8WithoutBom);
    }

    private static string? ResolveBlazorAssetsDirectory()
    {
        // For single-file publish, AppContext.BaseDirectory points to the extraction
        // directory. But BlazorReportAssets use ExcludeFromSingleFile=true, so they
        // sit alongside the actual exe on disk. Use the process path first.
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var exeDir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(exeDir))
            {
                var candidate = Path.Combine(exeDir, BlazorAssetsSubdirectory);
                if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "_framework")))
                {
                    return candidate;
                }
            }
        }

        // Fallback: AppContext.BaseDirectory (dev-time builds, non-single-file).
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var candidate = Path.Combine(baseDir, BlazorAssetsSubdirectory);
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "_framework")))
            {
                return candidate;
            }
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
setlocal
set PORT=8890
set DIR=%~dp0

echo.
echo  Comparison Report Server
echo  ========================
echo.
echo  Starting local server at http://localhost:%PORT%
echo  Press Ctrl+C to stop.
echo.

:: Try python first (most reliable HTTP server)
where python >nul 2>&1
if %errorlevel%==0 (
    start http://localhost:%PORT%
    python -m http.server %PORT% -d "%DIR%"
    goto :eof
)

:: Try PowerShell HTTP listener (available on all Windows)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$port=%PORT%; $dir='%DIR%'; Start-Process 'http://localhost:'+$port;" ^
    "$listener=[System.Net.HttpListener]::new(); $listener.Prefixes.Add('http://localhost:'+$port+'/');" ^
    "$listener.Start(); Write-Host 'Serving on http://localhost:'+$port; Write-Host 'Press Ctrl+C to stop.';" ^
    "try { while($listener.IsListening) { $ctx=$listener.GetContext(); $req=$ctx.Request;" ^
    "$path=$req.Url.LocalPath; if($path -eq '/') {$path='/index.html'};" ^
    "$file=Join-Path $dir $path.TrimStart('/').Replace('/','\\');" ^
    "if(Test-Path $file -PathType Leaf) {" ^
    "$bytes=[IO.File]::ReadAllBytes($file); $ext=[IO.Path]::GetExtension($file);" ^
    "$mime=@{'.html'='text/html';'.js'='application/javascript';'.wasm'='application/wasm';" ^
    "'.css'='text/css';'.json'='application/json';'.br'='application/octet-stream';" ^
    "'.gz'='application/octet-stream';'.svg'='image/svg+xml';'.png'='image/png';" ^
    "'.woff'='font/woff';'.woff2'='font/woff2'}[$ext];" ^
    "if(-not $mime){$mime='application/octet-stream'};" ^
    "$ctx.Response.ContentType=$mime; $ctx.Response.ContentLength64=$bytes.Length;" ^
    "$ctx.Response.OutputStream.Write($bytes,0,$bytes.Length)} else {$ctx.Response.StatusCode=404};" ^
    "$ctx.Response.Close() } } finally { $listener.Stop() }"
""";

        var shScript = """
#!/bin/bash
PORT=8890
DIR="$(cd "$(dirname "$0")" && pwd)"

echo ""
echo "  Comparison Report Server"
echo "  ========================"
echo ""
echo "  Starting local server at http://localhost:$PORT"
echo "  Press Ctrl+C to stop."
echo ""

if command -v python3 &> /dev/null; then
    (sleep 1 && xdg-open http://localhost:$PORT 2>/dev/null || open http://localhost:$PORT 2>/dev/null) &
    python3 -m http.server $PORT -d "$DIR"
elif command -v python &> /dev/null; then
    (sleep 1 && xdg-open http://localhost:$PORT 2>/dev/null || open http://localhost:$PORT 2>/dev/null) &
    python -m http.server $PORT -d "$DIR"
else
    echo "Python not found. Install Python 3 to serve this report locally."
    echo "Alternatively, use any HTTP server pointed at: $DIR"
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
