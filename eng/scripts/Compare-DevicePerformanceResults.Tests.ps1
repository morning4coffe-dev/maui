#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"
$script = Join-Path $PSScriptRoot "Compare-DevicePerformanceResults.ps1"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("maui-device-perf-compare-" + [Guid]::NewGuid().ToString("N"))

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual)
    {
        throw "$message. Expected '$expected', actual '$actual'."
    }
}

function Write-Results([string]$path, [object[]]$results) {
    ConvertTo-Json -InputObject $results -Depth 10 |
        Set-Content -Path $path -Encoding UTF8
}

function New-Result([string]$variant, [double[]]$measurements) {
    return [PSCustomObject]@{
        schemaVersion = 1
        scenario = "collectionview-scroll"
        platform = "ios"
        variant = $variant
        commitSha = if ($variant -eq "base") { "abc123" } else { "def456" }
        measurementsMilliseconds = $measurements
        statistics = [PSCustomObject]@{}
        counters = [PSCustomObject]@{ layoutPasses = if ($variant -eq "base") { 10 } else { 5 } }
    }
}

New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

try
{
    $resultsPath = Join-Path $testRoot "results.json"
    $summaryPath = Join-Path $testRoot "summary.json"
    $markdownPath = Join-Path $testRoot "summary.md"

    Write-Results $resultsPath @(
        (New-Result "base" @(100, 102, 104, 106)),
        (New-Result "head" @(70, 72, 74, 76)),
        (New-Result "head" @(71, 73, 75, 77)),
        (New-Result "base" @(101, 103, 105, 107))
    )

    & $script -ResultsPath $resultsPath -JsonOut $summaryPath -MarkdownOut $markdownPath
    Assert-Equal 0 $LASTEXITCODE "Comparator should succeed"

    $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
    Assert-Equal "time-improvement-advisory" $summary.verdict "Improvement verdict"
    Assert-Equal -5 $summary.comparisons[0].counters[0].delta "Counter delta"
    Assert-Equal 2 $summary.comparisons[0].baseResultCount "Base ABBA result count"
    Assert-Equal 2 $summary.comparisons[0].headResultCount "Head ABBA result count"

    Write-Results $resultsPath @(
        (New-Result "base" @(100, 110)),
        (New-Result "head" @(105, 115))
    )

    & $script -ResultsPath $resultsPath -JsonOut $summaryPath -MarkdownOut $markdownPath
    $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
    Assert-Equal "neutral" $summary.verdict "Overlapping timing ranges should be neutral"

    Write-Results $resultsPath @((New-Result "base" @(100, 110)))
    & $script -ResultsPath $resultsPath -JsonOut $summaryPath -MarkdownOut $markdownPath
    $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
    Assert-Equal "inconclusive" $summary.verdict "Missing head result should be inconclusive"

    Write-Host "All device performance comparator tests passed."
}
finally
{
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
