#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"
$script = Join-Path $PSScriptRoot "Run-DevicePerformanceComparison.ps1"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("maui-device-perf-driver-" + [Guid]::NewGuid().ToString("N"))

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual)
    {
        throw "$message. Expected '$expected', actual '$actual'."
    }
}

New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

try
{
    $baseApp = Join-Path $testRoot "base.app"
    $headApp = Join-Path $testRoot "head.app"
    $output = Join-Path $testRoot "output"
    New-Item -ItemType Directory -Force -Path $baseApp, $headApp | Out-Null

    & $script `
        -Platform ios `
        -BaseApp $baseApp `
        -HeadApp $headApp `
        -BaseCommitSha abc123 `
        -HeadCommitSha def456 `
        -OutputDirectory $output `
        -DeviceId simulator-id `
        -DryRun

    Assert-Equal 0 $LASTEXITCODE "Dry-run should succeed"

    $plan = Get-Content (Join-Path $output "run-plan.json") -Raw | ConvertFrom-Json
    Assert-Equal 4 ($plan.Count) "ABBA run count"
    Assert-Equal "base" $plan[0].variant "Run 1 variant"
    Assert-Equal "head" $plan[1].variant "Run 2 variant"
    Assert-Equal "head" $plan[2].variant "Run 3 variant"
    Assert-Equal "base" $plan[3].variant "Run 4 variant"
    Assert-Equal $true ($plan[0].arguments -contains "--set-env=TestFilter=Category=Performance") "iOS performance filter"
    Assert-Equal $true ($plan[1].arguments -contains "--set-env=MAUI_PERF_VARIANT=head") "Head variant metadata"

    $androidOutput = Join-Path $testRoot "android-output"
    $baseApk = Join-Path $testRoot "base.apk"
    $headApk = Join-Path $testRoot "head.apk"
    New-Item -ItemType File -Force -Path $baseApk, $headApk | Out-Null

    & $script `
        -Platform android `
        -BaseApp $baseApk `
        -HeadApp $headApk `
        -BaseCommitSha abc123 `
        -HeadCommitSha def456 `
        -OutputDirectory $androidOutput `
        -DeviceId emulator-5554 `
        -DryRun

    $androidPlan = Get-Content (Join-Path $androidOutput "run-plan.json") -Raw | ConvertFrom-Json
    Assert-Equal $true ($androidPlan[0].arguments -contains "TestFilter=Category=Performance") "Android performance filter"
    Assert-Equal $true ($androidPlan[0].arguments -contains "com.microsoft.maui.controls.devicetests.TestInstrumentation") "Android instrumentation"
    Assert-Equal $true ($androidPlan[1].arguments -contains "MAUI_PERF_VARIANT=head") "Android head metadata"

    $helixOutput = Join-Path $testRoot "helix-output"
    $previousXHarnessCliPath = $env:XHARNESS_CLI_PATH
    try
    {
        $env:XHARNESS_CLI_PATH = "/payload/Microsoft.DotNet.XHarness.CLI.dll"
        & $script `
            -Platform ios `
            -BaseApp $baseApp `
            -HeadApp $headApp `
            -BaseCommitSha abc123 `
            -HeadCommitSha def456 `
            -OutputDirectory $helixOutput `
            -XHarnessMode helix `
            -DryRun

        $helixPlan = Get-Content (Join-Path $helixOutput "run-plan.json") -Raw | ConvertFrom-Json
        Assert-Equal "dotnet" $helixPlan[0].executable "Helix executable"
        Assert-Equal "exec" $helixPlan[0].arguments[0] "Helix dotnet exec argument"
        Assert-Equal $env:XHARNESS_CLI_PATH $helixPlan[0].arguments[1] "Helix CLI path"
    }
    finally
    {
        $env:XHARNESS_CLI_PATH = $previousXHarnessCliPath
    }

    Write-Host "All device performance driver tests passed."
}
finally
{
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
