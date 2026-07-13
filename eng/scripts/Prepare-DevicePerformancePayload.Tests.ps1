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

    $archive = Join-Path $testRoot "payload.zip"
    $metadataPath = Join-Path $testRoot "payload.json"
    & $script `
        -BaseArtifacts (Join-Path $testRoot "base-artifacts") `
        -HeadArtifacts (Join-Path $testRoot "head-artifacts") `
        -Platform android `
        -BaseCommitSha abc123 `
        -HeadCommitSha def456 `
        -OutputArchive $archive `
        -MetadataOut $metadataPath

    Assert-Equal 0 $LASTEXITCODE "Payload preparation should succeed"
    Assert-Equal $true (Test-Path $archive) "Payload archive should exist"

    $metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
    Assert-Equal "payload/base/com.microsoft.maui.controls.devicetests-Signed.apk" $metadata.baseAppRelativePath "Base relative path"
    Assert-Equal "payload/head/com.microsoft.maui.controls.devicetests-Signed.apk" $metadata.headAppRelativePath "Head relative path"

    Write-Host "All device performance payload tests passed."
}
finally
{
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
