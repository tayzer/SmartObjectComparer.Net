[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$ProjectPath,
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot 'ComparisonTool.Desktop\ComparisonTool.Desktop.csproj'
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\desktop'
}

$resolvedProjectPath = (Resolve-Path $ProjectPath).Path
$resolvedOutputRoot = if (Test-Path $OutputRoot) {
    (Resolve-Path $OutputRoot).Path
}
else {
    New-Item -ItemType Directory -Path $OutputRoot | Select-Object -ExpandProperty FullName
}

$publishDir = Join-Path $resolvedOutputRoot $Runtime
$zipPath = Join-Path $resolvedOutputRoot "comparison-tool-desktop-$Runtime.zip"

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

dotnet publish $resolvedProjectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=embedded `
    -o $publishDir

$exePath = Join-Path $publishDir 'ComparisonTool.Desktop.exe'
$hostPagePath = Join-Path $publishDir 'wwwroot\index.html'

if (-not (Test-Path $exePath)) {
    throw "Expected desktop exe was not published: $exePath"
}

if (-not (Test-Path $hostPagePath)) {
    throw "Expected BlazorWebView host page was not published: $hostPagePath"
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force

if (-not (Test-Path $zipPath)) {
    throw "Expected desktop zip artifact was not created: $zipPath"
}

Write-Host "Desktop publish folder: $publishDir"
Write-Host "Desktop zip artifact: $zipPath"