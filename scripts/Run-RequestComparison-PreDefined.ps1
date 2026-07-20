# Run with hardcoded parameters (edit values below as needed)
# .\scripts\Run-RequestComparison-PreDefined.ps1

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$cliProject = Join-Path $repoRoot "ComparisonTool.Cli\ComparisonTool.Cli.csproj"
$reportProject = Join-Path $repoRoot "ComparisonTool.Report\ComparisonTool.Report.csproj"
$reportPublishDir = Join-Path $repoRoot "ComparisonTool.Report\bin\publish"
$publishDir = Join-Path $repoRoot "ComparisonTool.Cli\bin\Release\net10.0\publish"
$exePath = Join-Path $publishDir "comparisontool.exe"
$reportAssetsDir = Join-Path $reportPublishDir "wwwroot"

# ---- Hardcoded parameters ----
$RequestFolder = "C:\Dev\GitMain\ComparisonTool\ComparisonTool.MockApi\MockRequests"
$EndpointA = "http://localhost:5055/api/mock/a"
$EndpointB = "http://localhost:5055/api/mock/b"
$DomainModel = "ComplexOrderResponse"
$ContentType = "application/xml"
$IgnoreRulesPath = ""
$TreatNullAndEmptyCollectionsAsEqual = $false
$AlternateContract = $false
$AlternateContractProfile = ""
$UseProfileEndpoints = $false
$Header = @()
$HeaderA = @()
$HeaderB = @()
$HeadersFile = ""
$HeadersAFile = ""
$HeadersBFile = ""
$NoEndpointDefaults = $false
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

Write-Host "Publishing Blazor report assets..."
& dotnet publish $reportProject -c Release -o $reportPublishDir
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish for ComparisonTool.Report failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path (Join-Path $reportAssetsDir "index.html")))
{
    throw "Blazor report publish output was not found at $reportAssetsDir"
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

if (-not [string]::IsNullOrWhiteSpace($IgnoreRulesPath))
{
    $arguments += @("--ignore-rules", $IgnoreRulesPath)
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
$cliExitCode = $LASTEXITCODE

if ($OutputType -contains "Html")
{
    $latestReport = Get-ChildItem -Path $OutputDirectory -Directory -Filter "request-comparison-*" |
        Where-Object { $_.FullName -notin $existingReportFolders -or $_.LastWriteTime -ge $runStartedAt } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    $latestRedirector = if ($latestReport)
    {
        "$($latestReport.FullName).html"
    }
    else
    {
        $null
    }

    if ($latestReport -and $latestRedirector -and (Test-Path $latestRedirector))
    {
        Get-NetTCPConnection -LocalPort 8890 -State Listen -ErrorAction SilentlyContinue |
            ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

        $redirectFileName = [System.IO.Path]::GetFileName($latestRedirector)
        $redirectUrl = "http://localhost:8890/$redirectFileName"
        $pythonCommand = Get-Command python -ErrorAction SilentlyContinue

        if (-not $pythonCommand)
        {
            $pythonCommand = Get-Command py -ErrorAction SilentlyContinue
        }

        Write-Host ""
        Write-Host "Launching Jenkins-like local artifact server from: $OutputDirectory"
        Write-Host "Opening report entry point: $redirectUrl"

        if ($pythonCommand)
        {
            if ($pythonCommand.Name -match "^py(\.exe)?$")
            {
                Start-Process -FilePath $pythonCommand.Source -ArgumentList @("-3", "-m", "http.server", "8890", "-d", $OutputDirectory) -WorkingDirectory $OutputDirectory
            }
            else
            {
                Start-Process -FilePath $pythonCommand.Source -ArgumentList @("-m", "http.server", "8890", "-d", $OutputDirectory) -WorkingDirectory $OutputDirectory
            }

            Start-Process $redirectUrl
        }
        elseif (Test-Path (Join-Path $latestReport.FullName "serve.cmd"))
        {
            Write-Warning "Python was not found. Falling back to the report-folder server, which is useful for local smoke testing but does not exactly match Jenkins artifact hosting."
            Start-Process -FilePath (Join-Path $latestReport.FullName "serve.cmd") -WorkingDirectory $latestReport.FullName
        }
        else
        {
            Write-Warning "No local HTTP server could be started automatically. Serve '$OutputDirectory' over HTTP and open '$redirectFileName'."
        }
    }
    else
    {
        Write-Warning "No new report folder from this run was found under $OutputDirectory"
    }
}

exit $cliExitCode
