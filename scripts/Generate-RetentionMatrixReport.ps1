Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspaceRoot = if ($env:GITHUB_WORKSPACE) {
    $env:GITHUB_WORKSPACE
}
else {
    (Get-Location).Path
}

$requiredTests = @(
    'Evaluate_WhenSuccessfulOutcomeModesVary_UsesExpectedOutcomeMatrix'
    'Evaluate_WhenNonSuccessOverrideVaries_UsesExpectedOverrideMatrix'
    'Evaluate_WhenKeepBoundedPerRunCapIsExceeded_TrimsLaterNonSuccessItemsByOrdinal'
    'Evaluate_WhenWindowWouldKeepButWorkspaceCapExceeded_CapWins'
    'Evaluate_WhenWindowWouldRetainButPerRunCapExceeded_CapPrecedenceTrims'
    'ExportRunDetailsJson_WhenArtifactsAreTrimmed_UsesMetadataWithoutArtifactReads'
    'ExportRunDetailsCsv_WhenArtifactsAreTrimmed_SucceedsWithoutArtifactReads'
    'WriteAsync_WhenRawArtifactsAreTrimmedByPolicy_CompletesUsingRetentionMetadata'
    'WriteAsync_WhenRetainedArtifactIsMissing_LabelsMissingUnexpectedlyAndContinues'
)

$outputDir = Join-Path $workspaceRoot 'artifacts/ci/retention-matrix'
$markdownPath = Join-Path $outputDir 'RetentionMatrixReport.md'
$jsonPath = Join-Path $outputDir 'RetentionMatrixReport.json'

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$trxFiles = @(Get-ChildItem -Path $workspaceRoot -Filter '*.trx' -Recurse -File -ErrorAction SilentlyContinue)
$orderedTrxFiles = @(
    $trxFiles | Sort-Object @{ Expression = { -not ($_.FullName -match '[\\/]TestResults[\\/]') } }, FullName
)
$trxFileList = @($orderedTrxFiles | Where-Object { $null -ne $_ })

$statusByTest = @{}
$evidenceByTest = @{}
foreach ($testName in $requiredTests) {
    $statusByTest[$testName] = 'NotFound'
    $evidenceByTest[$testName] = @()
}

foreach ($trxFile in $trxFileList) {
    try {
        [xml]$doc = Get-Content -Path $trxFile.FullName -Raw
    }
    catch {
        continue
    }

    $resultNodes = @($doc.SelectNodes("//*[local-name()='UnitTestResult']"))
    foreach ($node in $resultNodes) {
        $testName = [string]$node.testName
        if ([string]::IsNullOrWhiteSpace($testName) -or -not $statusByTest.ContainsKey($testName)) {
            continue
        }

        $outcome = [string]$node.outcome
        if ([string]::IsNullOrWhiteSpace($outcome)) {
            $outcome = 'Unknown'
        }

        $evidenceByTest[$testName] += [ordered]@{
            outcome = $outcome
            trxFile = $trxFile.FullName.Substring($workspaceRoot.Length).TrimStart('\', '/')
            executionId = [string]$node.executionId
            testId = [string]$node.testId
        }

        switch ($outcome.ToLowerInvariant()) {
            'passed' {
                $statusByTest[$testName] = 'Passed'
                continue
            }
            'failed' {
                if ($statusByTest[$testName] -ne 'Passed') {
                    $statusByTest[$testName] = 'Failed'
                }
                continue
            }
            default {
                if ($statusByTest[$testName] -notin @('Passed', 'Failed')) {
                    $statusByTest[$testName] = $outcome
                }
            }
        }
    }
}

$trxFilesScanned = @(
    foreach ($trxFile in $trxFileList) {
        $trxFile.FullName.Substring($workspaceRoot.Length).TrimStart('\', '/')
    }
)
$trxFilesScannedCount = $trxFileList.Count
$foundTestsCount = @($requiredTests | Where-Object { $statusByTest[$_] -ne 'NotFound' }).Count

$generatedAtUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$statusCounts = [ordered]@{}
foreach ($testName in $requiredTests) {
    $status = $statusByTest[$testName]
    if (-not $statusCounts.Contains($status)) {
        $statusCounts[$status] = 0
    }
    $statusCounts[$status]++
}

$reportObject = [ordered]@{
    generatedAtUtc = $generatedAtUtc
    workspaceRoot = $workspaceRoot
    trxFilesScanned = $trxFilesScanned
    summary = [ordered]@{
        requiredTests = $requiredTests.Count
        foundTests = $foundTestsCount
        statuses = $statusCounts
    }
    tests = @(
        foreach ($testName in $requiredTests) {
            [ordered]@{
                testName = $testName
                status = $statusByTest[$testName]
                evidence = $evidenceByTest[$testName]
            }
        }
    )
}

$jsonContent = $reportObject | ConvertTo-Json -Depth 8
Set-Content -Path $jsonPath -Value $jsonContent -Encoding UTF8

$summaryParts = @()
foreach ($kvp in $statusCounts.GetEnumerator() | Sort-Object Name) {
    $summaryParts += "$($kvp.Name): $($kvp.Value)"
}
$summaryLine = if ($summaryParts.Count -gt 0) {
    [string]::Join(', ', $summaryParts)
}
else {
    'No statuses collected.'
}

$mdLines = @(
    '# Retention Matrix Evidence Report'
    ''
    "Generated (UTC): $generatedAtUtc"
    "TRX files scanned: $trxFilesScannedCount"
    ''
    '## Summary'
    ''
    "- Required evidence tests: $($requiredTests.Count)"
    "- Found in TRX: $foundTestsCount"
    "- Status counts: $summaryLine"
    ''
    '## Evidence Matrix'
    ''
    '| Test | Status | Evidence Count |'
    '| --- | --- | --- |'
)

foreach ($testName in $requiredTests) {
    $status = $statusByTest[$testName]
    $evidenceCount = @($evidenceByTest[$testName]).Count
    $mdLines += "| $testName | $status | $evidenceCount |"
}

Set-Content -Path $markdownPath -Value ($mdLines -join [Environment]::NewLine) -Encoding UTF8

Write-Host "Generated retention matrix report: $markdownPath"
Write-Host "Generated retention matrix JSON: $jsonPath"