#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Compares parsed base/head MAUI device-performance results.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsPath,

    [Parameter(Mandatory = $false)]
    [string]$MarkdownOut = "-",

    [Parameter(Mandatory = $false)]
    [string]$JsonOut,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedRepository,

    [Parameter(Mandatory = $true)]
    [int]$ExpectedPullRequestNumber,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedBaseCommitSha,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedHeadCommitSha,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedHarnessSha,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedPlatform,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedScenario,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 10)]
    [int]$ExpectedVariantRuns = 2,

    [Parameter(Mandatory = $false)]
    [double]$TimePctTolerance = 15
)

$ErrorActionPreference = "Stop"

function Get-Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1)
    {
        return [double]$sorted[$middle]
    }

    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2
}

function Get-Statistics($variantResults) {
    $measurements = @(
        $variantResults |
            ForEach-Object { $_.measurementsMilliseconds } |
            ForEach-Object { [double]$_ }
    )
    if ($measurements.Count -eq 0)
    {
        throw "A performance result variant contains no measurements."
    }

    return [PSCustomObject]@{
        Minimum = [double]($measurements | Measure-Object -Minimum).Minimum
        Maximum = [double]($measurements | Measure-Object -Maximum).Maximum
        Median = [double](Get-Median $measurements)
        Count = $measurements.Count
    }
}

function Get-Percent([double]$from, [double]$to) {
    if ($from -eq 0)
    {
        return $(if ($to -eq 0) { 0 } else { 100 })
    }

    return (($to - $from) / [Math]::Abs($from)) * 100
}

function Format-Milliseconds([double]$value) {
    return "{0:N2} ms" -f $value
}

function Get-UniqueValues($items, [string]$propertyPath) {
    $values = foreach ($item in $items) {
        $value = $item
        foreach ($segment in $propertyPath.Split('.')) {
            if ($null -eq $value) {
                break
            }
            $property = $value.PSObject.Properties[$segment]
            $value = if ($null -ne $property) { $property.Value } else { $null }
        }
        if ($null -ne $value) {
            "$value"
        }
    }
    return @($values | Sort-Object -Unique)
}

if (-not (Test-Path $ResultsPath))
{
    throw "Results file does not exist: $ResultsPath"
}

$parsed = Get-Content $ResultsPath -Raw | ConvertFrom-Json
$results = @($parsed | ForEach-Object { $_ })
$grouped = @($results | Group-Object { "$($_.scenario)|$($_.platform)" })
$comparisons = New-Object System.Collections.Generic.List[object]

foreach ($group in $grouped)
{
    $baseResults = @($group.Group | Where-Object { $_.variant -eq "base" })
    $headResults = @($group.Group | Where-Object { $_.variant -eq "head" })
    $parts = $group.Name.Split('|', 2)
    $provenanceErrors = New-Object System.Collections.Generic.List[string]

    if ($baseResults.Count -ne $ExpectedVariantRuns) {
        $provenanceErrors.Add("Expected $ExpectedVariantRuns base results, found $($baseResults.Count).")
    }
    if ($headResults.Count -ne $ExpectedVariantRuns) {
        $provenanceErrors.Add("Expected $ExpectedVariantRuns head results, found $($headResults.Count).")
    }

    $baseCommits = @($baseResults.commitSha | Sort-Object -Unique)
    $headCommits = @($headResults.commitSha | Sort-Object -Unique)
    if ($baseCommits.Count -ne 1 -or $baseCommits[0] -ne $ExpectedBaseCommitSha) {
        $provenanceErrors.Add("Base results do not match expected commit '$ExpectedBaseCommitSha'.")
    }
    if ($headCommits.Count -ne 1 -or $headCommits[0] -ne $ExpectedHeadCommitSha) {
        $provenanceErrors.Add("Head results do not match expected commit '$ExpectedHeadCommitSha'.")
    }

    $allResults = @($baseResults + $headResults)
    $expectedProperties = @(
        @{ Path = "repository"; Expected = $ExpectedRepository; Name = "repository" },
        @{ Path = "pullRequestNumber"; Expected = "$ExpectedPullRequestNumber"; Name = "PR number" },
        @{ Path = "harnessSha"; Expected = $ExpectedHarnessSha; Name = "harness SHA" },
        @{ Path = "platform"; Expected = $ExpectedPlatform; Name = "platform" },
        @{ Path = "scenario"; Expected = $ExpectedScenario; Name = "scenario" },
        @{ Path = "expectedVariantRuns"; Expected = "$ExpectedVariantRuns"; Name = "expected run count" }
    )
    foreach ($expectedProperty in $expectedProperties) {
        $values = @(Get-UniqueValues $allResults $expectedProperty.Path)
        if ($values.Count -ne 1 -or $values[0] -ne $expectedProperty.Expected) {
            $provenanceErrors.Add("Results do not match expected $($expectedProperty.Name) '$($expectedProperty.Expected)'.")
        }
    }

    foreach ($variant in @(
        @{ Name = "base"; Results = $baseResults },
        @{ Name = "head"; Results = $headResults }
    )) {
        $ordinals = @($variant.Results.runOrdinal | ForEach-Object { [int]$_ } | Sort-Object -Unique)
        $expectedOrdinals = @(1..$ExpectedVariantRuns)
        if (($ordinals -join ",") -ne ($expectedOrdinals -join ",")) {
            $provenanceErrors.Add("$($variant.Name) run ordinals must be 1..$ExpectedVariantRuns.")
        }
    }

    if (@($allResults | Where-Object { $_.correctness.passed -ne $true }).Count -gt 0) {
        $provenanceErrors.Add("One or more device results failed correctness validation.")
    }

    $environmentPaths = @(
        "environment.executionKind",
        "environment.deviceModel",
        "environment.osVersion",
        "environment.runtimeFramework",
        "environment.processArchitecture",
        "environment.runtimeVariant",
        "environment.sdkVersion",
        "build.azdoBuildId",
        "build.azdoBuildUrl",
        "build.helixJobId",
        "build.helixWorkItem"
    )
    foreach ($environmentPath in $environmentPaths) {
        $values = @(Get-UniqueValues $allResults $environmentPath)
        if ($values.Count -ne 1 -or [string]::IsNullOrWhiteSpace($values[0])) {
            $provenanceErrors.Add("'$environmentPath' must be present and identical for all results.")
        }
    }

    if ($provenanceErrors.Count -gt 0) {
        $comparisons.Add([PSCustomObject]@{
            Scenario = $parts[0]
            Platform = $parts[1]
            Complete = $false
            ProvenanceValidated = $false
            CorrectnessPassed = @($allResults | Where-Object { $_.correctness.passed -ne $true }).Count -eq 0
            Flag = "inconclusive"
            Reason = $provenanceErrors -join " "
        })
        continue
    }

    $base = Get-Statistics $baseResults
    $head = Get-Statistics $headResults
    $medianDeltaPct = Get-Percent $base.Median $head.Median
    $rangesDoNotOverlap = $head.Minimum -gt $base.Maximum -or $head.Maximum -lt $base.Minimum

    $flag = "neutral"
    if ($rangesDoNotOverlap -and $medianDeltaPct -ge $TimePctTolerance)
    {
        $flag = "time-regression-advisory"
    }
    elseif ($rangesDoNotOverlap -and $medianDeltaPct -le -$TimePctTolerance)
    {
        $flag = "time-improvement-advisory"
    }

    $counterNames = @(
        @($baseResults | ForEach-Object { $_.counters.PSObject.Properties.Name }) +
        @($headResults | ForEach-Object { $_.counters.PSObject.Properties.Name }) |
            Sort-Object -Unique
    )
    $counters = @(
        foreach ($name in $counterNames)
        {
            $baseValues = @(
                $baseResults |
                    ForEach-Object {
                        $property = $_.counters.PSObject.Properties[$name]
                        if ($null -ne $property) { $property.Value }
                    } |
                    Where-Object { $null -ne $_ } |
                    ForEach-Object { [double]$_ }
            )
            $headValues = @(
                $headResults |
                    ForEach-Object {
                        $property = $_.counters.PSObject.Properties[$name]
                        if ($null -ne $property) { $property.Value }
                    } |
                    Where-Object { $null -ne $_ } |
                    ForEach-Object { [double]$_ }
            )
            $baseValue = if ($baseValues.Count -gt 0) { ($baseValues | Measure-Object -Average).Average } else { $null }
            $headValue = if ($headValues.Count -gt 0) { ($headValues | Measure-Object -Average).Average } else { $null }
            [PSCustomObject]@{
                Name = $name
                Base = if ($null -ne $baseValue) { [double]$baseValue } else { $null }
                Head = if ($null -ne $headValue) { [double]$headValue } else { $null }
                Delta = if ($null -ne $baseValue -and $null -ne $headValue) {
                    [double]$headValue - [double]$baseValue
                } else {
                    $null
                }
            }
        }
    )

    $comparisons.Add([PSCustomObject]@{
        Scenario = $baseResults[0].scenario
        Platform = $baseResults[0].platform
        Complete = $true
        ProvenanceValidated = $true
        CorrectnessPassed = $true
        BaseCommit = $baseCommits[0]
        HeadCommit = $headCommits[0]
        BaseResultCount = $baseResults.Count
        HeadResultCount = $headResults.Count
        Build = [PSCustomObject]@{
            azdoBuildId = $baseResults[0].build.azdoBuildId
            azdoBuildUrl = $baseResults[0].build.azdoBuildUrl
            helixJobId = $baseResults[0].build.helixJobId
            helixWorkItem = $baseResults[0].build.helixWorkItem
        }
        Environment = $baseResults[0].environment
        Base = $base
        Head = $head
        MedianDeltaPct = $medianDeltaPct
        RangesDoNotOverlap = $rangesDoNotOverlap
        Counters = $counters
        Flag = $flag
        Reason = $null
    })
}

$incomplete = @($comparisons | Where-Object { -not $_.Complete })
$regressions = @($comparisons | Where-Object { $_.Flag -eq "time-regression-advisory" })
$improvements = @($comparisons | Where-Object { $_.Flag -eq "time-improvement-advisory" })

$verdict = if ($results.Count -eq 0 -or $incomplete.Count -gt 0) {
    "inconclusive"
} elseif ($regressions.Count -gt 0) {
    "time-regression-advisory"
} elseif ($improvements.Count -gt 0) {
    "time-improvement-advisory"
} else {
    "neutral"
}

$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine("| Scenario | Platform | Base range | Head range | Median delta | Result |")
[void]$builder.AppendLine("|---|---|---:|---:|---:|---|")

foreach ($comparison in $comparisons)
{
    if (-not $comparison.Complete)
    {
        [void]$builder.AppendLine("| ``$($comparison.Scenario)`` | $($comparison.Platform) | n/a | n/a | n/a | inconclusive |")
        continue
    }

    $baseRange = "$(Format-Milliseconds $comparison.Base.Minimum)-$(Format-Milliseconds $comparison.Base.Maximum)"
    $headRange = "$(Format-Milliseconds $comparison.Head.Minimum)-$(Format-Milliseconds $comparison.Head.Maximum)"
    $deltaSign = if ($comparison.MedianDeltaPct -gt 0) { "+" } else { "" }
    [void]$builder.AppendLine(
        "| ``$($comparison.Scenario)`` | $($comparison.Platform) | $baseRange | $headRange | $deltaSign$($comparison.MedianDeltaPct.ToString('N1'))% | $($comparison.Flag) |")
}

[void]$builder.AppendLine("")
[void]$builder.AppendLine("Timing is advisory. A change is flagged only when repeated base/head ranges do not overlap and the median delta exceeds the configured threshold.")

$markdown = $builder.ToString()
if ($MarkdownOut -eq "-")
{
    Write-Output $markdown
}
else
{
    $directory = Split-Path -Parent $MarkdownOut
    if ($directory -and -not (Test-Path $directory))
    {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    Set-Content -Path $MarkdownOut -Value $markdown -Encoding UTF8
}

$summary = [PSCustomObject]@{
    schemaVersion = 2
    verdict = $verdict
    timePctTolerance = $TimePctTolerance
    expected = [PSCustomObject]@{
        repository = $ExpectedRepository
        pullRequestNumber = $ExpectedPullRequestNumber
        baseCommitSha = $ExpectedBaseCommitSha
        headCommitSha = $ExpectedHeadCommitSha
        harnessSha = $ExpectedHarnessSha
        platform = $ExpectedPlatform
        scenario = $ExpectedScenario
        variantRuns = $ExpectedVariantRuns
    }
    provenanceValidated = $results.Count -gt 0 -and @($comparisons | Where-Object { -not $_.ProvenanceValidated }).Count -eq 0
    correctnessPassed = $results.Count -gt 0 -and @($comparisons | Where-Object { -not $_.CorrectnessPassed }).Count -eq 0
    accessibilityStatuses = @($results.correctness.accessibilityStatus | Sort-Object -Unique)
    comparisons = @($comparisons | ForEach-Object { $_ })
}

if ($JsonOut)
{
    $directory = Split-Path -Parent $JsonOut
    if ($directory -and -not (Test-Path $directory))
    {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    ConvertTo-Json -InputObject $summary -Depth 12 |
        Set-Content -Path $JsonOut -Encoding UTF8
}

Write-Host "Verdict: $verdict"
exit 0
