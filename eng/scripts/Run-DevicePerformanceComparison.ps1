#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs base/head MAUI device-performance apps in ABBA order on one device.
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("ios", "maccatalyst", "android")]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [string]$BaseApp,

    [Parameter(Mandatory = $true)]
    [string]$HeadApp,

    [Parameter(Mandatory = $true)]
    [string]$BaseCommitSha,

    [Parameter(Mandatory = $true)]
    [string]$HeadCommitSha,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $false)]
    [string]$DeviceId,

    [Parameter(Mandatory = $false)]
    [string]$AndroidPackageName = "com.microsoft.maui.controls.devicetests",

    [Parameter(Mandatory = $false)]
    [string]$AndroidInstrumentation = "com.microsoft.maui.controls.devicetests.TestInstrumentation",

    [Parameter(Mandatory = $false)]
    [string]$TestFilter = "Category=Performance",

    [Parameter(Mandatory = $false)]
    [string]$Timeout = "01:15:00",

    [Parameter(Mandatory = $false)]
    [ValidateSet("dotnet", "global", "helix")]
    [string]$XHarnessMode = "dotnet",

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$scriptDirectory = $PSScriptRoot
$parser = Join-Path $scriptDirectory "Parse-DevicePerformanceResults.ps1"
$comparator = Join-Path $scriptDirectory "Compare-DevicePerformanceResults.ps1"

function Assert-AppExists([string]$path, [string]$name) {
    if (-not (Test-Path $path))
    {
        throw "$name app does not exist: $path"
    }
}

function Get-XHarnessCommand([string]$variant, [string]$app, [string]$commitSha, [string]$runDirectory) {
    $arguments = New-Object System.Collections.Generic.List[string]
    if ($XHarnessMode -eq "dotnet")
    {
        $executable = "dotnet"
        $arguments.Add("xharness")
    }
    elseif ($XHarnessMode -eq "helix")
    {
        if ([string]::IsNullOrWhiteSpace($env:XHARNESS_CLI_PATH))
        {
            throw "XHARNESS_CLI_PATH is required for Helix execution."
        }

        $executable = "dotnet"
        $arguments.Add("exec")
        $arguments.Add($env:XHARNESS_CLI_PATH)
    }
    else
    {
        $executable = "xharness"
    }

    if ($Platform -eq "android")
    {
        $arguments.Add("android")
        $arguments.Add("test")
        $arguments.Add("--app")
        $arguments.Add($app)
        $arguments.Add("--package-name")
        $arguments.Add($AndroidPackageName)
        $arguments.Add("--instrumentation")
        $arguments.Add($AndroidInstrumentation)
        if ($DeviceId)
        {
            $arguments.Add("--device-id")
            $arguments.Add($DeviceId)
        }
        $arguments.Add("--output-directory")
        $arguments.Add($runDirectory)
        $arguments.Add("--timeout")
        $arguments.Add($Timeout)
        $arguments.Add("-v")
        $arguments.Add("--arg")
        $arguments.Add("TestFilter=$TestFilter")
        $arguments.Add("--arg")
        $arguments.Add("MAUI_PERF_VARIANT=$variant")
        $arguments.Add("--arg")
        $arguments.Add("MAUI_PERF_COMMIT_SHA=$commitSha")
    }
    else
    {
        $arguments.Add("apple")
        $arguments.Add("test")
        $arguments.Add("--app")
        $arguments.Add($app)
        $arguments.Add("--target")
        $arguments.Add($(if ($Platform -eq "ios") { "ios-simulator-64" } else { "maccatalyst" }))
        if ($DeviceId)
        {
            $arguments.Add("--device")
            $arguments.Add($DeviceId)
        }
        $arguments.Add("--output-directory")
        $arguments.Add($runDirectory)
        $arguments.Add("--timeout")
        $arguments.Add($Timeout)
        $arguments.Add("--launch-timeout")
        $arguments.Add("00:10:00")
        $arguments.Add("-v")
        $arguments.Add("--set-env=TestFilter=$TestFilter")
        $arguments.Add("--set-env=MAUI_PERF_VARIANT=$variant")
        $arguments.Add("--set-env=MAUI_PERF_COMMIT_SHA=$commitSha")
    }

    return [PSCustomObject]@{
        Executable = $executable
        Arguments = @($arguments)
    }
}

Assert-AppExists $BaseApp "Base"
Assert-AppExists $HeadApp "Head"

if (-not (Test-Path $OutputDirectory))
{
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
}

$runs = @(
    [PSCustomObject]@{ Variant = "base"; App = $BaseApp; CommitSha = $BaseCommitSha; Number = 1 },
    [PSCustomObject]@{ Variant = "head"; App = $HeadApp; CommitSha = $HeadCommitSha; Number = 1 },
    [PSCustomObject]@{ Variant = "head"; App = $HeadApp; CommitSha = $HeadCommitSha; Number = 2 },
    [PSCustomObject]@{ Variant = "base"; App = $BaseApp; CommitSha = $BaseCommitSha; Number = 2 }
)

$plan = New-Object System.Collections.Generic.List[object]
foreach ($run in $runs)
{
    $runDirectory = Join-Path $OutputDirectory "$($run.Variant)-run$($run.Number)"
    $command = Get-XHarnessCommand $run.Variant $run.App $run.CommitSha $runDirectory
    $plan.Add([PSCustomObject]@{
        Variant = $run.Variant
        Number = $run.Number
        CommitSha = $run.CommitSha
        RunDirectory = $runDirectory
        Executable = $command.Executable
        Arguments = @($command.Arguments)
    })
}

ConvertTo-Json -InputObject @($plan | ForEach-Object { $_ }) -Depth 8 |
    Set-Content -Path (Join-Path $OutputDirectory "run-plan.json") -Encoding UTF8

if ($DryRun)
{
    foreach ($run in $plan)
    {
        Write-Host "$($run.Executable) $($run.Arguments -join ' ')"
    }
    exit 0
}

foreach ($run in $plan)
{
    New-Item -ItemType Directory -Force -Path $run.RunDirectory | Out-Null
    $consoleLog = Join-Path $run.RunDirectory "xharness-console.log"

    Write-Host "Running $($run.Variant) iteration $($run.Number) on $Platform..."
    $arguments = @($run.Arguments)
    & $run.Executable @arguments *> $consoleLog
    if ($LASTEXITCODE -ne 0)
    {
        throw "XHarness failed for $($run.Variant) iteration $($run.Number) with exit code $LASTEXITCODE."
    }
}

$resultsPath = Join-Path $OutputDirectory "device-performance-results.json"
$summaryJson = Join-Path $OutputDirectory "device-performance-summary.json"
$summaryMarkdown = Join-Path $OutputDirectory "device-performance-summary.md"

& $parser -InputPath $OutputDirectory -OutputPath $resultsPath
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

& $comparator -ResultsPath $resultsPath -JsonOut $summaryJson -MarkdownOut $summaryMarkdown
exit $LASTEXITCODE
