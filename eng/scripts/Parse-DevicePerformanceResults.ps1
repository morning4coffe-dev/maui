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
            Assert-RequiredProperty $result "scenario" $file.FullName
            Assert-RequiredProperty $result "platform" $file.FullName
            Assert-RequiredProperty $result "variant" $file.FullName
            Assert-RequiredProperty $result "commitSha" $file.FullName
            Assert-RequiredProperty $result "measurementsMilliseconds" $file.FullName
            Assert-RequiredProperty $result "statistics" $file.FullName

            if ([int]$result.schemaVersion -ne 1)
            {
                throw "Unsupported performance result schema '$($result.schemaVersion)' in '$($file.FullName)'."
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
