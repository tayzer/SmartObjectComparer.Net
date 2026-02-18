# Run with hardcoded parameters (edit values below as needed)
# .\scripts\Run-RequestComparison.ps1

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$cliProject = Join-Path $repoRoot "ComparisonTool.Cli\ComparisonTool.Cli.csproj"
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
$OutputType = @("Console", "Markdown")
# ------------------------------

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
exit $LASTEXITCODE
