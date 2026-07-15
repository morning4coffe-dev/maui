#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Extracts MAUI device-performance JSON records from XHarness, Helix, or test logs.
#>

param(
    [Parameter(Mandatory = $true)]
    [string[]]$InputPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter(Mandatory = $false)]
    [switch]$AllowEmpty
)

$ErrorActionPreference = "Stop"
$prefix = "MAUI_PERF_RESULT:"
$results = New-Object System.Collections.Generic.List[object]
$seenJson = New-Object System.Collections.Generic.HashSet[string]

function Get-InputFiles([string]$path) {
    if (-not (Test-Path $path))
    {
        throw "Input path does not exist: $path"
    }

    $item = Get-Item $path
    if ($item.PSIsContainer)
    {
        return @(Get-ChildItem $item.FullName -File -Recurse)
    }

    return @($item)
}

function Assert-RequiredProperty($result, [string]$name, [string]$source) {
    $property = $result.PSObject.Properties[$name]
    if ($null -eq $property -or $null -eq $property.Value -or "$($property.Value)" -eq "")
    {
        throw "Performance result in '$source' is missing '$name'."
    }
}

foreach ($path in $InputPath)
{
    foreach ($file in Get-InputFiles $path)
    {
        foreach ($line in Get-Content $file.FullName)
        {
            $prefixIndex = $line.IndexOf($prefix, [StringComparison]::Ordinal)
            if ($prefixIndex -lt 0)
            {
                continue
            }

            $json = $line.Substring($prefixIndex + $prefix.Length).Trim()
            if (-not $json.StartsWith("{", [StringComparison]::Ordinal))
            {
                throw "Invalid performance result marker in '$($file.FullName)'."
            }

            if (-not $seenJson.Add($json))
            {
                continue
            }

            try
            {
                $result = $json | ConvertFrom-Json
            }
            catch
            {
                throw "Invalid performance result JSON in '$($file.FullName)': $($_.Exception.Message)"
            }

            Assert-RequiredProperty $result "schemaVersion" $file.FullName
            Assert-RequiredProperty $result "repository" $file.FullName
            Assert-RequiredProperty $result "pullRequestNumber" $file.FullName
            Assert-RequiredProperty $result "scenario" $file.FullName
            Assert-RequiredProperty $result "platform" $file.FullName
            Assert-RequiredProperty $result "variant" $file.FullName
            Assert-RequiredProperty $result "commitSha" $file.FullName
            Assert-RequiredProperty $result "harnessSha" $file.FullName
            Assert-RequiredProperty $result "runOrdinal" $file.FullName
            Assert-RequiredProperty $result "expectedVariantRuns" $file.FullName
            Assert-RequiredProperty $result "build" $file.FullName
            Assert-RequiredProperty $result "environment" $file.FullName
            Assert-RequiredProperty $result "correctness" $file.FullName
            Assert-RequiredProperty $result "timestampUtc" $file.FullName
            Assert-RequiredProperty $result "measurementsMilliseconds" $file.FullName
            Assert-RequiredProperty $result "statistics" $file.FullName

            if ([int]$result.schemaVersion -ne 2)
            {
                throw "Unsupported performance result schema '$($result.schemaVersion)' in '$($file.FullName)'."
            }

            if ([int]$result.pullRequestNumber -le 0 -or
                [int]$result.runOrdinal -le 0 -or
                [int]$result.expectedVariantRuns -le 0 -or
                [int]$result.runOrdinal -gt [int]$result.expectedVariantRuns)
            {
                throw "Performance result in '$($file.FullName)' has invalid PR/run provenance."
            }

            if ($result.variant -notin @("base", "head") -or
                $result.platform -notin @("android", "ios", "maccatalyst"))
            {
                throw "Performance result in '$($file.FullName)' has an unsupported variant or platform."
            }

            foreach ($field in @("azdoBuildId", "azdoBuildUrl", "helixJobId", "helixWorkItem"))
            {
                Assert-RequiredProperty $result.build $field $file.FullName
            }

            foreach ($field in @("executionKind", "deviceModel", "osVersion", "runtimeFramework", "processArchitecture", "runtimeVariant", "sdkVersion"))
            {
                Assert-RequiredProperty $result.environment $field $file.FullName
            }

            Assert-RequiredProperty $result.correctness "passed" $file.FullName
            Assert-RequiredProperty $result.correctness "accessibilityStatus" $file.FullName
            if ($result.correctness.passed -isnot [bool] -or
                $result.correctness.accessibilityStatus -notin @("not-assessed", "passed", "failed"))
            {
                throw "Performance result in '$($file.FullName)' has invalid correctness metadata."
            }

            $timestamp = [DateTimeOffset]::MinValue
            if (-not [DateTimeOffset]::TryParse(
                [string]$result.timestampUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind,
                [ref]$timestamp))
            {
                throw "Performance result in '$($file.FullName)' has an invalid timestamp."
            }

            if (@($result.measurementsMilliseconds).Count -eq 0)
            {
                throw "Performance result in '$($file.FullName)' contains no measurements."
            }

            $results.Add($result)
        }
    }
}

if ($results.Count -eq 0 -and -not $AllowEmpty)
{
    Write-Error "No '$prefix' records were found."
    exit 3
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory -and -not (Test-Path $outputDirectory))
{
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$output = @($results | ForEach-Object { $_ })
ConvertTo-Json -InputObject $output -Depth 12 |
    Set-Content -Path $OutputPath -Encoding UTF8

Write-Host "Wrote $($results.Count) performance result(s) to $OutputPath"
exit 0
