#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Validates an AI-generated performance report against sealed workflow evidence.

.DESCRIPTION
    The report must contain a `perf-analysis-decision` JSON metadata comment. This
    script validates its schema, recommendation limits, evidence labels, workaround
    claims, and next-action gates against selection/benchmark evidence.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ReportPath,

    [Parameter(Mandatory = $true)]
    [string]$PolicyPath,

    [Parameter(Mandatory = $true)]
    [string]$SelectionPath,

    [Parameter(Mandatory = $false)]
    [string]$SummaryPath,

    [Parameter(Mandatory = $false)]
    [string]$DeviceValidationPath,

    [Parameter(Mandatory = $false)]
    [string]$JsonOut
)

$ErrorActionPreference = "Stop"
$errors = New-Object System.Collections.Generic.List[string]

function Add-ValidationError([string]$message) {
    $errors.Add($message)
}

function Test-AllowedValue($value, $allowedValues, [string]$fieldName) {
    if ([string]::IsNullOrWhiteSpace([string]$value) -or $value -notin @($allowedValues)) {
        Add-ValidationError "'$fieldName' must be one of: $(@($allowedValues) -join ', ')."
        return $false
    }

    return $true
}

function Get-PropertyValue($object, [string]$name) {
    if ($null -eq $object) {
        return $null
    }

    $property = $object.PSObject.Properties[$name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

foreach ($path in @($ReportPath, $PolicyPath, $SelectionPath)) {
    if (-not (Test-Path $path)) {
        throw "Required validation input does not exist: $path"
    }
}

$report = Get-Content $ReportPath -Raw
$policy = Get-Content $PolicyPath -Raw | ConvertFrom-Json
$selection = Get-Content $SelectionPath -Raw | ConvertFrom-Json
$summary = if ($SummaryPath -and (Test-Path $SummaryPath)) {
    Get-Content $SummaryPath -Raw | ConvertFrom-Json
} else {
    $null
}
$deviceValidation = if ($DeviceValidationPath -and (Test-Path $DeviceValidationPath)) {
    Get-Content $DeviceValidationPath -Raw | ConvertFrom-Json
} else {
    $null
}

$requiredHeadings = @(
    "## Performance analysis",
    "### Tradeoff assessment",
    "### Performance recommendations",
    "### Possible workaround",
    "### Recommended next action",
    "### Coverage"
)

foreach ($heading in $requiredHeadings) {
    if ($report -notmatch "(?m)^$([regex]::Escape($heading))\s*$") {
        Add-ValidationError "Required heading is missing: $heading"
    }
}

if ($report -notmatch '(?i)automated analysis by the \*\*perf-check\*\* agentic workflow') {
    Add-ValidationError "AI/workflow attribution is missing."
}

$decisionMatches = [regex]::Matches(
    $report,
    '<!--\s*perf-analysis-decision:\s*(\{.*\})\s*-->',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)

if ($decisionMatches.Count -ne 1) {
    Add-ValidationError "Report must contain exactly one perf-analysis-decision metadata comment."
    $decision = $null
} else {
    try {
        $decision = $decisionMatches[0].Groups[1].Value | ConvertFrom-Json
    } catch {
        Add-ValidationError "perf-analysis-decision metadata is invalid JSON: $($_.Exception.Message)"
        $decision = $null
    }
}

if ($null -ne $decision) {
    if ([int](Get-PropertyValue $decision "schemaVersion") -ne 1) {
        Add-ValidationError "Decision schemaVersion must be 1."
    }

    [void](Test-AllowedValue (Get-PropertyValue $decision "assessment") $policy.tradeoffAssessments "assessment")
    [void](Test-AllowedValue (Get-PropertyValue $decision "confidence") $policy.confidenceLevels "confidence")
    [void](Test-AllowedValue (Get-PropertyValue $decision "costAttribution") $policy.costAttributions "costAttribution")
    [void](Test-AllowedValue (Get-PropertyValue $decision "nextAction") @($policy.nextActions.id) "nextAction")
    [void](Test-AllowedValue (Get-PropertyValue $decision.workaround "status") $policy.workaroundStatuses "workaround.status")

    if ((Get-PropertyValue $decision "issueDisposition") -ne "human-only") {
        Add-ValidationError "issueDisposition must be 'human-only'."
    }

    $correctnessBenefitEstablished = Get-PropertyValue $decision "correctnessBenefitEstablished"
    if ($correctnessBenefitEstablished -isnot [bool]) {
        Add-ValidationError "correctnessBenefitEstablished must be a boolean."
    }
    $testedAlternativeAvailable = Get-PropertyValue $decision "testedAlternativeAvailable"
    if ($testedAlternativeAvailable -isnot [bool]) {
        Add-ValidationError "testedAlternativeAvailable must be a boolean."
    }

    $staticFindingSeverity = Get-PropertyValue $decision "staticFindingSeverity"
    if ($staticFindingSeverity -notin @("none", "warning", "error")) {
        Add-ValidationError "staticFindingSeverity must be none, warning, or error."
    }

    $recommendations = @($decision.recommendations)
    if ($recommendations.Count -gt [int]$policy.limits.maxRecommendations) {
        Add-ValidationError "At most $($policy.limits.maxRecommendations) recommendations are allowed."
    }

    for ($index = 0; $index -lt $recommendations.Count; $index++) {
        $recommendation = $recommendations[$index]
        $prefix = "recommendations[$index]"

        foreach ($field in @("text", "evidence", "expectedDirection", "risk")) {
            if ([string]::IsNullOrWhiteSpace([string](Get-PropertyValue $recommendation $field))) {
                Add-ValidationError "$prefix.$field is required."
            }
        }

        [void](Test-AllowedValue (Get-PropertyValue $recommendation "status") $policy.recommendationEvidence "$prefix.status")

        if ((Get-PropertyValue $recommendation "testedHere") -isnot [bool]) {
            Add-ValidationError "$prefix.testedHere must be a boolean."
        }
    }

    if ($recommendations.Count -eq 0 -and $report -notmatch '(?i)No evidence-backed optimization identified\.') {
        Add-ValidationError "An empty recommendation list requires the explicit no-optimization statement."
    }

    $assessment = [string](Get-PropertyValue $decision "assessment")
    $nextAction = [string](Get-PropertyValue $decision "nextAction")
    $actionPolicy = @($policy.nextActions | Where-Object { $_.id -eq $nextAction }) | Select-Object -First 1
    if ($null -ne $actionPolicy -and $assessment -notin @($actionPolicy.allowedAssessments)) {
        Add-ValidationError "Action '$nextAction' is incompatible with assessment '$assessment'."
    }

    $coverage = $selection.coverage
    $deviceScenarios = @($selection.deviceScenarios)
    $unsupportedDevicePath = @(
        $deviceScenarios | Where-Object { $_.automationStatus -eq "required-not-yet-automated" }
    ).Count -gt 0
    $summaryComplete = $null -ne $summary `
        -and [bool](Get-PropertyValue $summary "coverageComplete") `
        -and [bool](Get-PropertyValue $summary "executionComplete") `
        -and [bool](Get-PropertyValue $summary "benchmarkDataComplete")
    $deviceEvidenceComplete = $null -ne $deviceValidation `
        -and [bool](Get-PropertyValue $deviceValidation "sealed") `
        -and [bool](Get-PropertyValue $deviceValidation "deviceEvidenceComplete") `
        -and [bool](Get-PropertyValue $deviceValidation "correctnessPassed") `
        -and [bool](Get-PropertyValue $deviceValidation "allAffectedPlatformsCovered") `
        -and -not $unsupportedDevicePath
    $productFileCount = Get-PropertyValue $coverage "productFileCount"
    if ($null -ne $productFileCount) {
        $managedCount = [int](Get-PropertyValue $coverage "managedMeasuredFileCount")
        $deviceCount = [int](Get-PropertyValue $coverage "deviceRequiredFileCount")
        $sampledCount = [int](Get-PropertyValue $coverage "managedSampledFileCount")
        $staticCount = [int](Get-PropertyValue $coverage "staticOnlyFileCount")
        $classificationComplete = [int]$productFileCount -gt 0 `
            -and ($managedCount + $deviceCount) -eq [int]$productFileCount `
            -and $sampledCount -eq 0 `
            -and $staticCount -eq 0
        $wholePrEvidenceComplete = $classificationComplete `
            -and ($managedCount -eq 0 -or $summaryComplete) `
            -and ($deviceCount -eq 0 -or $deviceEvidenceComplete)
    } else {
        $wholePrEvidenceComplete =
            [bool](Get-PropertyValue $coverage "canClaimWholePrClean") -and $summaryComplete
    }
    $deviceAdvisory = $deviceEvidenceComplete -and @(
        $deviceValidation.acceptedMeasurements |
            Where-Object { $_.verdict -in @("time-regression-advisory", "time-improvement-advisory") }
    ).Count -gt 0
    $advisoryOnly = ($null -ne $summary -and [string]$summary.verdict -eq "time-regression-advisory") `
        -or $deviceAdvisory
    $confirmedAllocationRegression = $null -ne $summary -and @(
        $summary.allocRegressions | Where-Object { $_.confirmed -eq $true }
    ).Count -gt 0
    $confirmedBlockingRegression = $confirmedAllocationRegression -or $staticFindingSeverity -eq "error"

    $supportedMeasurementPath = @(
        $deviceScenarios | Where-Object { $_.automationStatus -eq "manual-device-ci-ready" }
    ).Count -gt 0
    $hasCoverageGap = -not $wholePrEvidenceComplete `
        -or @($selection.sampledProductFiles).Count -gt 0 `
        -or @($selection.staticOnlyProductFiles).Count -gt 0 `
        -or ($deviceScenarios.Count -gt 0 -and -not $deviceEvidenceComplete)

    $sealedWorkaroundValidation = $null -ne $deviceValidation `
        -and [bool](Get-PropertyValue $deviceValidation "sealed") `
        -and [bool](Get-PropertyValue $deviceValidation "correctnessPassed") `
        -and [bool](Get-PropertyValue $deviceValidation "allAffectedPlatformsCovered")

    $workaroundStatus = [string](Get-PropertyValue $decision.workaround "status")
    if ($workaroundStatus -eq "validated" -and -not $sealedWorkaroundValidation) {
        Add-ValidationError "A validated workaround requires sealed correctness evidence for every affected platform."
    }

    if ($nextAction -eq "prefer_validated_workaround" -and -not $sealedWorkaroundValidation) {
        Add-ValidationError "prefer_validated_workaround requires sealed validated-workaround evidence."
    }

    if ($assessment -in @("likely-worth-it", "likely-not-worth-it") `
        -and (-not $wholePrEvidenceComplete -or $advisoryOnly)) {
        Add-ValidationError "A worth-it assessment requires complete non-advisory whole-PR evidence."
    }

    if ($assessment -eq "likely-worth-it" `
        -and (-not $correctnessBenefitEstablished -or $testedAlternativeAvailable)) {
        Add-ValidationError "likely-worth-it requires an established correctness benefit and no better tested alternative."
    }

    if ($assessment -eq "likely-not-worth-it" `
        -and (-not $confirmedBlockingRegression -or (-not $testedAlternativeAvailable -and -not $sealedWorkaroundValidation))) {
        Add-ValidationError "likely-not-worth-it requires a confirmed regression plus a tested alternative or sealed validated workaround."
    }

    if ($nextAction -in @("accept_tradeoff", "accept_with_followup") `
        -and (-not $wholePrEvidenceComplete -or $advisoryOnly)) {
        Add-ValidationError "Acceptance actions require complete non-advisory whole-PR evidence."
    }

    if ($nextAction -in @("accept_tradeoff", "accept_with_followup") `
        -and (-not $correctnessBenefitEstablished `
            -or [string]$decision.costAttribution -ne "deliberate" `
            -or $testedAlternativeAvailable)) {
        Add-ValidationError "Acceptance actions require a deliberate cost, established correctness benefit, and no better tested alternative."
    }

    if ($nextAction -eq "no_concerns" `
        -and (-not $wholePrEvidenceComplete `
            -or -not [bool](Get-PropertyValue $summary "canClaimClean") `
            -or $staticFindingSeverity -eq "error")) {
        Add-ValidationError "no_concerns requires complete clean whole-PR evidence."
    }

    if ($nextAction -eq "optimize_before_merge" -and -not $confirmedBlockingRegression) {
        Add-ValidationError "optimize_before_merge requires a confirmed regression or error-level static finding."
    }

    if ($nextAction -eq "run_more_measurements") {
        if (-not $supportedMeasurementPath) {
            Add-ValidationError "run_more_measurements requires a concrete supported measurement path."
        }
        if ($confirmedBlockingRegression -and [string]$decision.costAttribution -eq "accidental") {
            Add-ValidationError "A confirmed accidental blocking regression cannot be deferred to more measurements."
        }
    }

    if ($nextAction -eq "needs_human_discussion" -and -not ($unsupportedDevicePath -or $hasCoverageGap -or $advisoryOnly -or $staticFindingSeverity -ne "none")) {
        Add-ValidationError "needs_human_discussion requires an unsupported path, coverage gap, or static concern."
    }
}

$validationResult = [PSCustomObject]@{
    schemaVersion = 1
    valid = $errors.Count -eq 0
    errors = @($errors | ForEach-Object { $_ })
}

if ($JsonOut) {
    $directory = Split-Path -Parent $JsonOut
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    ConvertTo-Json -InputObject $validationResult -Depth 8 |
        Set-Content -Path $JsonOut -Encoding UTF8
}

if ($errors.Count -gt 0) {
    foreach ($validationError in $errors) {
        [Console]::Error.WriteLine("ERROR: $validationError")
    }
    exit 2
}

Write-Host "Performance report validation passed."
exit 0
