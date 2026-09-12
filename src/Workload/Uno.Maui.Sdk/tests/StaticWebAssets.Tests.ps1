[CmdletBinding()]
param(
	[Parameter(Mandatory)][string]$DotNetPath,
	[Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$sdkRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$mauiRoot = [IO.Path]::GetFullPath((Join-Path $sdkRoot "..\..\.."))
$version = (Get-Content (Join-Path $mauiRoot "global.json") -Raw | ConvertFrom-Json).tools.dotnet
$sdkLines = @(& $DotNetPath --list-sdks)
if ($LASTEXITCODE -ne 0) { throw "Cannot enumerate installed SDKs." }
$sdkLine = @($sdkLines | Where-Object { $_.StartsWith("$version ") })
if ($sdkLine.Count -ne 1 -or $sdkLine[0] -notmatch '^([^\s]+)\s+\[(.+)\]$') {
	throw "Pinned SDK $version is unavailable; no SDK is installed by this test."
}
$cli = Join-Path (Join-Path $Matches[2] $Matches[1]) "dotnet.dll"
$run = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([Guid]::NewGuid().ToString("N"))
New-Item $run -ItemType Directory | Out-Null
Copy-Item (Join-Path $PSScriptRoot "StaticWebAssets\*") -Destination $run -Recurse
$packages = Join-Path $run "packages"
$publish = Join-Path $run "publish"
$plainPublish = Join-Path $run "plain-publish"
$hostProject = Join-Path $run "Host\Host.csproj"
$separator = [IO.Path]::DirectorySeparatorChar
$report = [ordered]@{
	Status = "Failed"
	Sdk = $version
	Scope = "Real SDK static-web-asset discovery and publish fixtures; no Uno/browser/AOT runtime acceptance."
	Commands = [Collections.Generic.List[object]]::new()
	Assertions = 0
}
$properties = @(
	"-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
	"-p:RestorePackagesPath=$packages", "-p:NuGetPackageRoot=$packages$separator",
	"-p:UnoMauiSdkRoot=$sdkRoot$separator", "-p:NuGetAudit=true", "-p:RestoreIgnoreFailedSources=false"
)

function Invoke-StaticAssetsCommand([string]$Name, [string[]]$Arguments) {
	$log = Join-Path $run "$Name.log"
	& $DotNetPath $cli @Arguments @properties *> $log
	$code = $LASTEXITCODE
	$report.Commands.Add([pscustomobject]@{ Name = $Name; ExitCode = $code; Log = $log; Arguments = $Arguments })
	if ($code -ne 0) {
		Get-Content -LiteralPath $log -Tail 25 | Write-Host
		throw "$Name failed with exit code $code."
	}
}

function Assert-StaticAsset([string]$Directory, [string]$Filter, [string]$Source) {
	$files = @(Get-ChildItem -LiteralPath $Directory -Filter $Filter -File)
	if ($files.Count -ne 1 -or
		(Get-FileHash -LiteralPath $files[0].FullName).Hash -cne (Get-FileHash -LiteralPath $Source).Hash) {
		throw "Published static asset '$Filter' is missing, duplicated or differs from its source."
	}
	$report.Assertions++
}

$savedResolver = $env:MSBuildEnableWorkloadResolver
Push-Location $run
try {
	$env:MSBuildEnableWorkloadResolver = "false"
	Invoke-StaticAssetsCommand "app-build" @("build", (Join-Path $run "App\App.csproj"), "-c", "Release", "-v:minimal")
	Invoke-StaticAssetsCommand "host-restore" @("restore", $hostProject, "-v:minimal")
	Invoke-StaticAssetsCommand "host-publish" @("publish", $hostProject, "--no-restore", "-c", "Release", "-p:BuildProjectReferences=false", "-o", $publish, "-v:minimal")
	Assert-StaticAsset (Join-Path $publish "wwwroot\_content\Fixture.App") "Fixture.App*.lib.module.js" (Join-Path $run "App\wwwroot\Fixture.App.lib.module.js")
	Assert-StaticAsset (Join-Path $publish "wwwroot\_content\Fixture.Library") "site*.css" (Join-Path $run "Library\wwwroot\site.css")

	$plain = Join-Path $run "Plain\Plain.csproj"
	Invoke-StaticAssetsCommand "plain-build" @("build", $plain, "-c", "Release", "-v:minimal")
	Invoke-StaticAssetsCommand "plain-host-restore" @("restore", $hostProject, "-p:UnoMauiAppProject=$plain", "-v:minimal")
	Invoke-StaticAssetsCommand "plain-host-publish" @("publish", $hostProject, "--no-restore", "-c", "Release", "-p:UnoMauiAppProject=$plain", "-p:BuildProjectReferences=false", "-o", $plainPublish, "-v:minimal")
	if ((Test-Path (Join-Path $plainPublish "wwwroot\_content\Fixture.App")) -or
		(Test-Path (Join-Path $plainPublish "wwwroot\_content\Fixture.Library"))) {
		throw "A plain application inherited another application's static assets."
	}
	$report.Assertions++
	$report.Status = "Passed"
}
finally {
	$env:MSBuildEnableWorkloadResolver = $savedResolver
	Pop-Location
	$report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run "report.json")
	Write-Host "Static-web-asset report: $run"
}
Write-Host "Passed $($report.Assertions) real static-asset publish assertions across $($report.Commands.Count) SDK commands."
