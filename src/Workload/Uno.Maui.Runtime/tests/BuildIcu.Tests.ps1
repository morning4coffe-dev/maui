[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if (-not [OperatingSystem]::IsMacOS()) {
    throw "ICU cache tests require macOS and Xcode."
}

$buildScript = Join-Path (Split-Path $PSScriptRoot -Parent) "nuget/buildTransitive/build-icu-catalyst.sh"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("uno-icu-cache-tests-" + [Guid]::NewGuid().ToString("N"))
$cacheRoot = Join-Path $testRoot "cache with spaces"
$assertions = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
    $script:assertions++
}

function Get-OutputPath([string]$Architecture, [string]$MinimumVersion = "15.0") {
    $output = & bash $buildScript --print-output-directory $cacheRoot $Architecture $MinimumVersion
    Assert-True ($LASTEXITCODE -eq 0) "Cache path resolution failed."
    Assert-True (@($output).Count -eq 1) "Cache path resolution must emit exactly one line."
    return [string]$output
}

function Publish-TestEntry([string]$Path, [string]$Contents) {
    $staging = Join-Path $testRoot ([Guid]::NewGuid().ToString("N"))
    New-Item $staging -ItemType Directory | Out-Null
    Set-Content (Join-Path $staging "libicuuc.a") $Contents
    Set-Content (Join-Path $staging "libicudata.a") $Contents
    Set-Content (Join-Path $staging "ICU-LICENSE.txt") "Test fixture"
    Set-Content (Join-Path $staging "build-key") (Split-Path $Path -Leaf)
    Move-Item $staging $Path
}

try {
    $arm64 = Get-OutputPath "arm64"
    $x64 = Get-OutputPath "x86_64"
    $newMinimum = Get-OutputPath "arm64" "16.0"
    Assert-True ($arm64 -ne $x64) "Architectures must use different immutable entries."
    Assert-True ($arm64 -ne $newMinimum) "Minimum OS changes must use different entries."
    Assert-True ($arm64 -eq (Get-OutputPath "arm64")) "Identical inputs must resolve identically."

    Publish-TestEntry $arm64 "arm64"
    New-Item "$x64.lock" -ItemType Directory | Out-Null
    & bash $buildScript $cacheRoot arm64 15.0
    Assert-True ($LASTEXITCODE -eq 0) "Another architecture's build must not block a cache hit."
    Publish-TestEntry $x64 "x64"
    Assert-True ((Get-Content (Join-Path $arm64 "libicuuc.a") -Raw).Trim() -eq "arm64") "Publishing x64 must not replace arm64 archives."
    Assert-True ((Get-Content (Join-Path $x64 "libicudata.a") -Raw).Trim() -eq "x64") "The x64 consumer must use its own archive."

    New-Item $newMinimum -ItemType Directory | Out-Null
    Set-Content (Join-Path $newMinimum "libicuuc.a") "partial"
    $failure = (& bash $buildScript $cacheRoot arm64 16.0 2>&1) -join "`n"
    Assert-True ($LASTEXITCODE -ne 0) "An incomplete published entry must never be a cache hit."
    Assert-True ($failure.Contains("Incomplete ICU cache entry")) "An incomplete entry must fail explicitly, without rebuilding in place."

    $concurrent = Get-OutputPath "arm64" "17.0"
    New-Item "$concurrent.lock" -ItemType Directory | Out-Null
    $start = [Diagnostics.ProcessStartInfo]::new("bash")
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @($buildScript, $cacheRoot, "arm64", "17.0")) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        Assert-True (-not $process.WaitForExit(250)) "A concurrent builder must wait for publication."
        Publish-TestEntry $concurrent "published"
        Assert-True ($process.WaitForExit(10000)) "A waiter must observe the completed immutable entry."
        Assert-True ($process.ExitCode -eq 0) "A waiter must reuse the other builder's completed entry."
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}
finally {
    if (Test-Path $testRoot) {
        Remove-Item $testRoot -Recurse -Force
    }
}

Write-Host "Passed $assertions ICU cache assertions."
