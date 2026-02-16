# .\scripts\Run-RequestComparison.ps1 `
#   -RequestFolder "C:\path\to\requests" `
#   -EndpointA "https://host-a/api/endpoint" `
#   -EndpointB "https://host-b/api/endpoint" `
#   -DomainModel "ComplexOrderResponse" `
#   -ContentType "application/json" `
#   -OutputDirectory "C:\path\to\reports" `
#   -OutputType Console,Json

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RequestFolder,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$EndpointA,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$EndpointB,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DomainModel,

    [Parameter(Mandatory = $false)]
    [string]$ContentType,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Console", "Json", "Markdown")]
    [string[]]$OutputType
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$cliProject = Join-Path $repoRoot "ComparisonTool.Cli\ComparisonTool.Cli.csproj"
$publishDir = Join-Path $repoRoot "ComparisonTool.Cli\bin\Release\net8.0\publish"
$exePath = Join-Path $publishDir "comparisontool.exe"

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

Write-Host "Running request comparison..."
Write-Host "$exePath $($arguments -join ' ')"

& $exePath @arguments
exit $LASTEXITCODE
