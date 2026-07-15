#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"
$skillRoot = Split-Path -Parent $PSScriptRoot
$validator = [IO.Path]::Combine($skillRoot, "scripts", "Validate-PerformanceReport.ps1")
$policy = [IO.Path]::Combine($skillRoot, "references", "recommendation-policy.json")
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("maui-perf-report-validator-" + [Guid]::NewGuid().ToString("N"))

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) {
        throw "$message. Expected '$expected', actual '$actual'."
    }
}

function Write-Json([string]$path, $value) {
    ConvertTo-Json -InputObject $value -Depth 12 | Set-Content $path -Encoding UTF8
}

function New-Selection(
    [bool]$wholePrClean,
    [string]$deviceStatus = "",
    [int]$staticCount = 0,
    [int]$sampledCount = 0
) {
    $deviceScenarios = @()
    if ($deviceStatus) {
        $deviceScenarios = @([PSCustomObject]@{
            id = "scenario"
            automationStatus = $deviceStatus
        })
    }

    return [PSCustomObject]@{
        coverage = [PSCustomObject]@{
            canClaimWholePrClean = $wholePrClean
        }
        deviceScenarios = $deviceScenarios
        sampledProductFiles = if ($sampledCount -gt 0) { @(1..$sampledCount) } else { @() }
        staticOnlyProductFiles = if ($staticCount -gt 0) { @(1..$staticCount) } else { @() }
    }
}

function New-Summary(
    [string]$verdict = "neutral",
    [bool]$complete = $true,
    [bool]$clean = $true,
    [bool]$confirmedRegression = $false
) {
    return [PSCustomObject]@{
        verdict = $verdict
        coverageComplete = $complete
        executionComplete = $complete
        benchmarkDataComplete = $complete
        canClaimClean = $clean
        allocRegressions = if ($confirmedRegression) {
            @([PSCustomObject]@{ name = "Benchmark"; confirmed = $true })
        } else {
            @()
        }
    }
}

function New-Decision(
    [string]$assessment,
    [string]$nextAction,
    [string]$costAttribution = "unknown",
    [string]$workaroundStatus = "none",
    [string]$staticSeverity = "none",
    [bool]$correctnessBenefitEstablished = $false,
    [bool]$testedAlternativeAvailable = $false
) {
    return [ordered]@{
        schemaVersion = 1
        assessment = $assessment
        confidence = "low"
        costAttribution = $costAttribution
        correctnessBenefitEstablished = $correctnessBenefitEstablished
        testedAlternativeAvailable = $testedAlternativeAvailable
        staticFindingSeverity = $staticSeverity
        workaround = [ordered]@{
            status = $workaroundStatus
        }
        nextAction = $nextAction
        issueDisposition = "human-only"
        recommendations = @()
    }
}

function Write-Report([string]$path, $decision) {
    $decisionJson = ConvertTo-Json -InputObject $decision -Depth 10 -Compress
    @"
## Performance analysis

**Verdict:** test

### Tradeoff assessment

test

### Performance recommendations

No evidence-backed optimization identified.

### Possible workaround

test

### Recommended next action

test

### Coverage

test

> Automated analysis by the **perf-check** agentic workflow.

<!-- perf-analysis-decision: $decisionJson -->
"@ | Set-Content $path -Encoding UTF8
}

function Invoke-Validation(
    [string]$name,
    $decision,
    $selection,
    $summary,
    $deviceValidation = $null
) {
    $caseRoot = Join-Path $testRoot $name
    New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null
    $reportPath = Join-Path $caseRoot "report.md"
    $selectionPath = Join-Path $caseRoot "selection.json"
    $summaryPath = Join-Path $caseRoot "summary.json"
    $validationPath = Join-Path $caseRoot "validation.json"
    Write-Report $reportPath $decision
    Write-Json $selectionPath $selection
    Write-Json $summaryPath $summary

    $arguments = @{
        ReportPath = $reportPath
        PolicyPath = $policy
        SelectionPath = $selectionPath
        SummaryPath = $summaryPath
        JsonOut = $validationPath
    }

    if ($null -ne $deviceValidation) {
        $devicePath = Join-Path $caseRoot "device-validation.json"
        Write-Json $devicePath $deviceValidation
        $arguments.DeviceValidationPath = $devicePath
    }

    & $validator @arguments
    $exitCode = $LASTEXITCODE
    $validation = Get-Content $validationPath -Raw | ConvertFrom-Json
    return [PSCustomObject]@{
        ExitCode = $exitCode
        Validation = $validation
    }
}

New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

try {
    $clean = Invoke-Validation `
        "clean" `
        (New-Decision "not-applicable" "no_concerns") `
        (New-Selection $true) `
        (New-Summary)
    Assert-Equal 0 $clean.ExitCode "Complete clean report should pass"
    Assert-Equal $true $clean.Validation.valid "Complete clean validity"

    $advisoryAccept = Invoke-Validation `
        "advisory-accept" `
        (New-Decision "likely-worth-it" "accept_tradeoff" "deliberate") `
        (New-Selection $true) `
        (New-Summary "time-regression-advisory" $true $false)
    Assert-Equal 2 $advisoryAccept.ExitCode "Advisory evidence must not accept tradeoff"

    $partialNoConcerns = Invoke-Validation `
        "partial-no-concerns" `
        (New-Decision "not-applicable" "no_concerns") `
        (New-Selection $false "manual-device-ci-ready") `
        (New-Summary "inconclusive" $false $false)
    Assert-Equal 2 $partialNoConcerns.ExitCode "Partial coverage must not report no concerns"

    $confirmedRegression = Invoke-Validation `
        "confirmed-regression" `
        (New-Decision "not-applicable" "optimize_before_merge" "accidental") `
        (New-Selection $false "manual-device-ci-ready") `
        (New-Summary "alloc-regression" $true $false $true)
    Assert-Equal 0 $confirmedRegression.ExitCode "Confirmed regression should override coverage gap"

    $confirmedRegressionDeferred = Invoke-Validation `
        "confirmed-regression-deferred" `
        (New-Decision "unclear" "run_more_measurements" "accidental") `
        (New-Selection $false "manual-device-ci-ready") `
        (New-Summary "alloc-regression" $true $false $true)
    Assert-Equal 2 $confirmedRegressionDeferred.ExitCode "Confirmed accidental regression must not be deferred"

    $supportedMissing = Invoke-Validation `
        "supported-missing" `
        (New-Decision "unclear" "run_more_measurements") `
        (New-Selection $false "manual-device-ci-ready") `
        (New-Summary "inconclusive" $false $false)
    Assert-Equal 0 $supportedMissing.ExitCode "Supported missing evidence should request measurement"

    $unsupportedMissing = Invoke-Validation `
        "unsupported-missing" `
        (New-Decision "unclear" "needs_human_discussion") `
        (New-Selection $false "required-not-yet-automated") `
        (New-Summary "inconclusive" $false $false)
    Assert-Equal 0 $unsupportedMissing.ExitCode "Unsupported evidence should request discussion"

    $unvalidatedWorkaround = Invoke-Validation `
        "unvalidated-workaround" `
        (New-Decision "likely-not-worth-it" "prefer_validated_workaround" "deliberate" "validated") `
        (New-Selection $true) `
        (New-Summary "alloc-regression" $true $false $true)
    Assert-Equal 2 $unvalidatedWorkaround.ExitCode "External/unsealed workaround must fail validation"

    $validatedWorkaroundEvidence = [PSCustomObject]@{
        sealed = $true
        correctnessPassed = $true
        allAffectedPlatformsCovered = $true
    }
    $validatedWorkaround = Invoke-Validation `
        "validated-workaround" `
        (New-Decision "likely-not-worth-it" "prefer_validated_workaround" "deliberate" "validated" "none" $true) `
        (New-Selection $true) `
        (New-Summary "alloc-regression" $true $false $true) `
        $validatedWorkaroundEvidence
    Assert-Equal 0 $validatedWorkaround.ExitCode "Sealed validated workaround should pass"

    $deviceOnlySelection = [PSCustomObject]@{
        coverage = [PSCustomObject]@{
            productFileCount = 1
            managedMeasuredFileCount = 0
            managedSampledFileCount = 0
            deviceRequiredFileCount = 1
            staticOnlyFileCount = 0
            canClaimWholePrClean = $false
        }
        deviceScenarios = @([PSCustomObject]@{
            id = "scenario"
            automationStatus = "manual-device-ci-ready"
        })
        sampledProductFiles = @()
        staticOnlyProductFiles = @()
    }
    $completeDeviceEvidence = [PSCustomObject]@{
        sealed = $true
        deviceEvidenceComplete = $true
        correctnessPassed = $true
        allAffectedPlatformsCovered = $true
        acceptedMeasurements = @([PSCustomObject]@{ verdict = "time-regression-advisory" })
    }
    $deviceAdvisoryDiscussion = Invoke-Validation `
        "device-advisory-discussion" `
        (New-Decision "unclear" "needs_human_discussion" "deliberate") `
        $deviceOnlySelection `
        (New-Summary "inconclusive" $false $false) `
        $completeDeviceEvidence
    Assert-Equal 0 $deviceAdvisoryDiscussion.ExitCode "Complete advisory device evidence should require discussion"

    $deviceAdvisoryAccept = Invoke-Validation `
        "device-advisory-accept" `
        (New-Decision "likely-worth-it" "accept_tradeoff" "deliberate" "none" "none" $true) `
        $deviceOnlySelection `
        (New-Summary "inconclusive" $false $false) `
        $completeDeviceEvidence
    Assert-Equal 2 $deviceAdvisoryAccept.ExitCode "Advisory device evidence must not accept a tradeoff"

    Write-Host "All performance report validator tests passed."
}
finally {
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
