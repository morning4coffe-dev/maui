#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"
$script = Join-Path $PSScriptRoot "Prepare-DevicePerformancePayload.ps1"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("maui-device-perf-payload-test-" + [Guid]::NewGuid().ToString("N"))

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual)
    {
        throw "$message. Expected '$expected', actual '$actual'."
    }
}

New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

try
{
    $baseArtifacts = Join-Path $testRoot "base-artifacts/Controls.DeviceTests/Release/net10.0-android"
    $headArtifacts = Join-Path $testRoot "head-artifacts/Controls.DeviceTests/Release/net10.0-android"
    New-Item -ItemType Directory -Force -Path $baseArtifacts, $headArtifacts | Out-Null
    "base" | Set-Content (Join-Path $baseArtifacts "com.microsoft.maui.controls.devicetests-Signed.apk")
    "head" | Set-Content (Join-Path $headArtifacts "com.microsoft.maui.controls.devicetests-Signed.apk")
    @{
        schemaVersion = 1
        repository = "dotnet/maui"
        pullRequestNumber = 42
        variant = "base"
        platform = "android"
        commitSha = "abc123"
        harnessSha = "harness123"
        runtimeVariant = "mono"
        sdkVersion = "10.0.100"
    } | ConvertTo-Json | Set-Content (Join-Path $baseArtifacts "device-performance-build-metadata.json")
    @{
        schemaVersion = 1
        repository = "dotnet/maui"
        pullRequestNumber = 42
        variant = "head"
        platform = "android"
        commitSha = "def456"
        harnessSha = "harness123"
        runtimeVariant = "mono"
        sdkVersion = "10.0.101"
    } | ConvertTo-Json | Set-Content (Join-Path $headArtifacts "device-performance-build-metadata.json")

    $archive = Join-Path $testRoot "payload.zip"
    $metadataPath = Join-Path $testRoot "payload.json"
    & $script `
        -BaseArtifacts (Join-Path $testRoot "base-artifacts") `
        -HeadArtifacts (Join-Path $testRoot "head-artifacts") `
        -Platform android `
        -BaseCommitSha abc123 `
        -HeadCommitSha def456 `
        -ExpectedScenario collectionview-keepitemsinview-update `
        -Repository dotnet/maui `
        -PullRequestNumber 42 `
        -HarnessSha harness123 `
        -AzdoBuildId 100 `
        -AzdoBuildUrl https://build/100 `
        -OutputArchive $archive `
        -MetadataOut $metadataPath

    Assert-Equal 0 $LASTEXITCODE "Payload preparation should succeed"
    Assert-Equal $true (Test-Path $archive) "Payload archive should exist"

    $metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
    Assert-Equal "payload/base/com.microsoft.maui.controls.devicetests-Signed.apk" $metadata.baseAppRelativePath "Base relative path"
    Assert-Equal "payload/head/com.microsoft.maui.controls.devicetests-Signed.apk" $metadata.headAppRelativePath "Head relative path"
    Assert-Equal 2 $metadata.schemaVersion "Payload schema version"
    Assert-Equal 42 $metadata.pullRequestNumber "Payload PR number"
    Assert-Equal "harness123" $metadata.harnessSha "Payload harness SHA"
    Assert-Equal "collectionview-keepitemsinview-update" $metadata.expectedScenario "Expected scenario"
    Assert-Equal "10.0.100" $metadata.baseSdkVersion "Base SDK version"
    Assert-Equal "10.0.101" $metadata.headSdkVersion "Head SDK version"

    Write-Host "All device performance payload tests passed."
}
finally
{
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
