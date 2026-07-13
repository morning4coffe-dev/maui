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

    if ($baseResults.Count -eq 0 -or $headResults.Count -eq 0)
    {
        $comparisons.Add([PSCustomObject]@{
            Scenario = $parts[0]
            Platform = $parts[1]
            Complete = $false
            Flag = "inconclusive"
            Reason = "Expected at least one base and one head result."
        })
        continue
    }

    $baseCommits = @($baseResults.commitSha | Sort-Object -Unique)
    $headCommits = @($headResults.commitSha | Sort-Object -Unique)
    if ($baseCommits.Count -ne 1 -or $headCommits.Count -ne 1)
    {
        $comparisons.Add([PSCustomObject]@{
            Scenario = $parts[0]
            Platform = $parts[1]
            Complete = $false
            Flag = "inconclusive"
            Reason = "Each variant must contain results from exactly one commit."
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
        BaseCommit = $baseCommits[0]
        HeadCommit = $headCommits[0]
        BaseResultCount = $baseResults.Count
        HeadResultCount = $headResults.Count
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
    schemaVersion = 1
    verdict = $verdict
    timePctTolerance = $TimePctTolerance
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
