# Run with hardcoded parameters (edit values below as needed)
# .\scripts\Run-RequestComparison.ps1

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$cliProject = Join-Path $repoRoot "ComparisonTool.Cli\ComparisonTool.Cli.csproj"
$reportPublishDir = Join-Path $repoRoot "ComparisonTool.Report\bin\publish"
$publishDir = Join-Path $repoRoot "ComparisonTool.Cli\bin\Release\net10.0\publish"
$exePath = Join-Path $publishDir "comparisontool.exe"

# ---- Hardcoded parameters ----
$RequestFolder = "C:\Dev\GitMain\ComparisonTool\ComparisonTool.MockApi\MockRequests"
$EndpointA = "http://localhost:5055/api/mock/a"
$EndpointB = "http://localhost:5055/api/mock/b"
$DomainModel = "ComplexOrderResponse"
$ContentType = "application/xml"
$IgnoreRulesPath = ""
$OutputDirectory = "C:\Dev\GitMain\ComparisonTool\reports"
$OutputType = @("Console", "Markdown", "Html")
# Html output generates a Jenkins-style redirector .html file plus a companion folder
# containing the Blazor assets.
# ------------------------------

if (Test-Path $reportPublishDir)
{
    Remove-Item -Path $reportPublishDir -Recurse -Force
}

if (Test-Path $publishDir)
{
    Remove-Item -Path $publishDir -Recurse -Force
}

Write-Host "Building CLI project..."
& dotnet publish $cliProject -c Release -o $publishDir
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $exePath))
{
    throw "CLI executable not found at $exePath"
}

if (-not (Test-Path $RequestFolder))
{
    throw "Request folder not found: $RequestFolder"
}

$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$existingReportFolders = @(Get-ChildItem -Path $OutputDirectory -Directory -Filter "request-comparison-*" | Select-Object -ExpandProperty FullName)
$runStartedAt = Get-Date

$arguments = @(
    "request",
    $RequestFolder,
    "-a", $EndpointA,
    "-b", $EndpointB,
    "-m", $DomainModel,
    "-o", $OutputDirectory,
    "-f"
) + $OutputType

if (-not [string]::IsNullOrWhiteSpace($ContentType))
{
    $arguments += @("--content-type", $ContentType)
}

if (-not [string]::IsNullOrWhiteSpace($IgnoreRulesPath))
{
    $arguments += @("--ignore-rules", $IgnoreRulesPath)
}

Write-Host "Running request comparison..."
Write-Host "$exePath $($arguments -join ' ')"

& $exePath @arguments
$cliExitCode = $LASTEXITCODE

if ($OutputType -contains "Html")
{
    $latestReport = Get-ChildItem -Path $OutputDirectory -Directory -Filter "request-comparison-*" |
        Where-Object { $_.FullName -notin $existingReportFolders -or $_.LastWriteTime -ge $runStartedAt } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($latestReport -and (Test-Path (Join-Path $latestReport.FullName "serve.cmd")))
    {
        Get-NetTCPConnection -LocalPort 8890 -State Listen -ErrorAction SilentlyContinue |
            ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

        Write-Host ""
        Write-Host "Launching local report server from: $($latestReport.FullName)"
        Start-Process -FilePath (Join-Path $latestReport.FullName "serve.cmd") -WorkingDirectory $latestReport.FullName
    }
    else
    {
        Write-Warning "No new report folder from this run was found under $OutputDirectory"
    }
}

exit $cliExitCode
