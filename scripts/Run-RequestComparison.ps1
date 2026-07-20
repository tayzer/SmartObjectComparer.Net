# .\scripts\Run-RequestComparison.ps1 `
#   -RequestFolder "C:\path\to\requests" `
#   -EndpointA "Local Mock Customer Lookup SOAP" `
#   -EndpointB "Local Mock Customer Lookup JSON" `
#   -DomainModel "ExpectedJsonCustomerLookupResponse" `
#   -AlternateContractProfile "expected-json-customer-lookup" `
#   -ContentType "application/xml" `
#   -OutputDirectory "C:\path\to\reports" `
#   -OutputType Console,Json,Markdown,Html

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RequestFolder,

    [Parameter(Mandatory = $false)]
    [string]$EndpointA,

    [Parameter(Mandatory = $false)]
    [string]$EndpointB,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DomainModel,

    [Parameter(Mandatory = $false)]
    [string]$ContentType,

    [Parameter(Mandatory = $false)]
    [switch]$TreatNullAndEmptyCollectionsAsEqual,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Console", "Json", "Markdown", "Html")]
    [string[]]$OutputType,

    [Parameter(Mandatory = $false)]
    [switch]$AlternateContract,

    [Parameter(Mandatory = $false)]
    [string]$AlternateContractProfile,

    [Parameter(Mandatory = $false)]
    [switch]$UseProfileEndpoints,

    [Parameter(Mandatory = $false)]
    [string[]]$Header = @(),

    [Parameter(Mandatory = $false)]
    [string[]]$HeaderA = @(),

    [Parameter(Mandatory = $false)]
    [string[]]$HeaderB = @(),

    [Parameter(Mandatory = $false)]
    [string]$HeadersFile,

    [Parameter(Mandatory = $false)]
    [string]$HeadersAFile,

    [Parameter(Mandatory = $false)]
    [string]$HeadersBFile,

    [Parameter(Mandatory = $false)]
    [switch]$NoEndpointDefaults
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$cliProject = Join-Path $repoRoot "ComparisonTool.Cli\ComparisonTool.Cli.csproj"
$publishDir = Join-Path $repoRoot "ComparisonTool.Cli\bin\Release\net10.0\publish"
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
    "-m", $DomainModel,
    "-o", $OutputDirectory,
    "-f"
) + $OutputType

if (-not [string]::IsNullOrWhiteSpace($EndpointA))
{
    $arguments += @("-a", $EndpointA)
}

if (-not [string]::IsNullOrWhiteSpace($EndpointB))
{
    $arguments += @("-b", $EndpointB)
}

if (-not [string]::IsNullOrWhiteSpace($ContentType))
{
    $arguments += @("--content-type", $ContentType)
}

if ($TreatNullAndEmptyCollectionsAsEqual)
{
    $arguments += "--treat-null-empty-collections-equal"
}

if ($AlternateContract)
{
    $arguments += "--alternate-contract"
}

if (-not [string]::IsNullOrWhiteSpace($AlternateContractProfile))
{
    $arguments += @("--alternate-contract-profile", $AlternateContractProfile)
}

if ($UseProfileEndpoints)
{
    $arguments += "--use-profile-endpoints"
}

foreach ($value in $Header)
{
    $arguments += @("--header", $value)
}

foreach ($value in $HeaderA)
{
    $arguments += @("--header-a", $value)
}

foreach ($value in $HeaderB)
{
    $arguments += @("--header-b", $value)
}

if (-not [string]::IsNullOrWhiteSpace($HeadersFile))
{
    $arguments += @("--headers-file", $HeadersFile)
}

if (-not [string]::IsNullOrWhiteSpace($HeadersAFile))
{
    $arguments += @("--headers-a-file", $HeadersAFile)
}

if (-not [string]::IsNullOrWhiteSpace($HeadersBFile))
{
    $arguments += @("--headers-b-file", $HeadersBFile)
}

if ($NoEndpointDefaults)
{
    $arguments += "--no-endpoint-defaults"
}

Write-Host "Running request comparison..."
Write-Host "$exePath $($arguments -join ' ')"

& $exePath @arguments
exit $LASTEXITCODE
