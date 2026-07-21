#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

$skillRoot = Split-Path -Parent $PSScriptRoot
$selector = [IO.Path]::Combine($skillRoot, "scripts", "Select-Benchmarks.ps1")
$comparator = [IO.Path]::Combine($skillRoot, "scripts", "Compare-BenchmarkResults.ps1")
$scenarioRegistry = [IO.Path]::Combine($skillRoot, "references", "platform-scenarios.json")
$recommendationPolicyPath = [IO.Path]::Combine($skillRoot, "references", "recommendation-policy.json")
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("maui-perf-tests-" + [Guid]::NewGuid().ToString("N"))

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "ASSERT TRUE FAILED: $Message"
    }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) {
        throw "ASSERT EQUAL FAILED: $Message. Expected '$Expected', actual '$Actual'."
    }
}

function Write-ChangedFiles([string]$Name, [string[]]$Files) {
    $path = Join-Path $testRoot "$Name.txt"
    $Files | Set-Content -Path $path -Encoding UTF8
    return $path
}

function Invoke-SelectorFixture([string]$Name, [string[]]$Files) {
    $changedFilesPath = Write-ChangedFiles $Name $Files
    $outputPath = Join-Path $testRoot "$Name.json"

    & $selector `
        -ChangedFilesPath $changedFilesPath `
        -ScenarioRegistryPath $scenarioRegistry `
        -OutputPath $outputPath

    Assert-Equal 0 $LASTEXITCODE "Selector fixture '$Name' should be relevant"
    return Get-Content $outputPath -Raw | ConvertFrom-Json
}

function Write-BenchmarkReport(
    [string]$Path,
    [string]$FullName,
    [double]$Allocated,
    [double]$Mean = 100,
    [bool]$IncludeMemory = $true,
    [bool]$IncludeStatistics = $true
) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $benchmark = @{
        FullName = $FullName
        Parameters = ""
    }
    if ($IncludeStatistics) {
        $benchmark.Statistics = @{
            Mean = $Mean
            StandardError = 1
        }
    }
    if ($IncludeMemory) {
        $benchmark.Memory = @{
            BytesAllocatedPerOperation = $Allocated
        }
    }

    $document = @{ Benchmarks = @($benchmark) }

    $document | ConvertTo-Json -Depth 8 | Set-Content -Path $Path -Encoding UTF8
}

function Invoke-ComparatorFixture(
    [string]$Name,
    [string]$ManifestStatus = "complete"
) {
    $root = Join-Path $testRoot $Name
    $summaryPath = Join-Path $root "summary.json"
    $markdownPath = Join-Path $root "table.md"
    $manifestPath = Join-Path $root "manifest.json"

    @{ status = $ManifestStatus } |
        ConvertTo-Json |
        Set-Content -Path $manifestPath -Encoding UTF8

    & $comparator `
        -BaseDir (Join-Path $root "base") `
        -HeadDir (Join-Path $root "head") `
        -RunManifestPath $manifestPath `
        -MarkdownOut $markdownPath `
        -JsonOut $summaryPath

    Assert-Equal 0 $LASTEXITCODE "Comparator fixture '$Name' should run"
    return Get-Content $summaryPath -Raw | ConvertFrom-Json
}

New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

try {
    $recommendationPolicy = Get-Content $recommendationPolicyPath -Raw | ConvertFrom-Json
    Assert-Equal 1 $recommendationPolicy.schemaVersion "Recommendation policy schema"
    Assert-Equal 3 $recommendationPolicy.limits.maxRecommendations "Recommendation limit"
    Assert-Equal 7 (@($recommendationPolicy.nextActions).Count) "Next action count"
    $nextActionIds = @($recommendationPolicy.nextActions | ForEach-Object { $_.id })
    foreach ($requiredAction in @(
        "no_concerns",
        "accept_tradeoff",
        "accept_with_followup",
        "optimize_before_merge",
        "run_more_measurements",
        "prefer_validated_workaround",
        "needs_human_discussion"
    )) {
        Assert-True ($nextActionIds -contains $requiredAction) "Missing next action '$requiredAction'"
    }
    Assert-True (
        @($recommendationPolicy.hardGates | Where-Object { $_ -match "Never recommend automatic issue closure" }).Count -eq 1
    ) "No-auto-close gate missing"
    Assert-True (
        @($recommendationPolicy.tradeoffAssessmentGates."likely-not-worth-it" | Where-Object { $_ -match "tested lower-cost" }).Count -eq 1
    ) "Likely-not-worth-it alternative gate missing"
    Assert-True (
        @($recommendationPolicy.hardGates | Where-Object { $_ -match "advisory-only evidence" }).Count -eq 1
    ) "Advisory evidence assessment gate missing"
    $acceptTradeoff = $recommendationPolicy.nextActions | Where-Object { $_.id -eq "accept_tradeoff" }
    Assert-Equal 1 (@($acceptTradeoff.allowedAssessments).Count) "Accept tradeoff assessment count"
    Assert-Equal "likely-worth-it" $acceptTradeoff.allowedAssessments[0] "Accept tradeoff assessment gate"
    Assert-True (
        @($acceptTradeoff.requires | Where-Object { $_ -match "non-advisory" }).Count -eq 1
    ) "Accept tradeoff non-advisory gate missing"
    $runMoreMeasurements = $recommendationPolicy.nextActions | Where-Object { $_.id -eq "run_more_measurements" }
    Assert-Equal "unclear" $runMoreMeasurements.allowedAssessments[0] "Run-more assessment gate"
    Assert-True $recommendationPolicy.decisionRules.confirmedRegressionTakesPrecedenceOverCoverageGaps "Regression precedence missing"
    Assert-Equal "needs_human_discussion" $recommendationPolicy.decisionRules.unsupportedMissingEvidenceAction "Unsupported evidence action"
    Assert-True (-not $recommendationPolicy.decisionRules.externalEvidenceCanValidateWorkaround) "External workaround evidence must not validate"
    Assert-True (
        @($recommendationPolicy.costAttributions) -contains "deliberate"
    ) "Deliberate cost attribution missing"
    Assert-True (
        @($recommendationPolicy.workaroundStatuses) -contains "plausible-unverified"
    ) "Unverified workaround status missing"

    $platform = Invoke-SelectorFixture "platform" @(
        "src/Controls/src/Core/Handlers/Items/Android/MauiRecyclerView.cs",
        "src/Controls/src/Core/Handlers/Items2/iOS/LayoutFactory2.cs"
    )
    Assert-Equal "device-required" $platform.coverage.status "CollectionView platform files require devices"
    Assert-Equal 0 @($platform.suites).Count "Platform files must not map to managed suites"
    Assert-True $platform.requiresDeviceMeasurement "Platform selection should require device measurement"
    $scenarioIds = @($platform.deviceScenarios | ForEach-Object { $_.id })
    Assert-True ($scenarioIds -contains "collectionview-items-update-android") "Android CollectionView scenario missing"
    Assert-True ($scenarioIds -contains "collectionview-scroll-ios") "iOS CollectionView scenario missing"
    $androidScenario = $platform.deviceScenarios | Where-Object { $_.id -eq "collectionview-items-update-android" }
    Assert-Equal "manual-device-ci-ready" $androidScenario.automationStatus "Android pipeline status"
    Assert-Equal "eng/pipelines/ci-device-performance.yml" $androidScenario.pipeline.path "Android pipeline path"
    Assert-Equal "android" $androidScenario.pipeline.platforms[0] "Android canonical pipeline platform"
    $iosScenario = $platform.deviceScenarios | Where-Object { $_.id -eq "collectionview-scroll-ios" }
    Assert-Equal 2 (@($iosScenario.pipeline.platforms).Count) "Apple pipeline platform count"
    Assert-Equal "ios" $iosScenario.pipeline.platforms[0] "iOS canonical pipeline platform"
    Assert-Equal "maccatalyst" $iosScenario.pipeline.platforms[1] "MacCatalyst canonical pipeline platform"

    $uncoveredItemsViewLayout = Invoke-SelectorFixture "items-view-layout" @(
        "src/Controls/src/Core/Handlers/Items/iOS/ItemsViewLayout.cs"
    )
    Assert-Equal "collectionview-items-update-ios" $uncoveredItemsViewLayout.deviceScenarios[0].id "ItemsViewLayout should use its dedicated update scenario"
    Assert-Equal "manual-device-ci-ready" $uncoveredItemsViewLayout.deviceScenarios[0].automationStatus "ItemsViewLayout update scenario status"
    Assert-Equal "collectionview-keepitemsinview-update" $uncoveredItemsViewLayout.deviceScenarios[0].resultScenario "ItemsViewLayout result scenario"

    $windowsPlatform = Invoke-SelectorFixture "windows-platform" @(
        "src/Controls/src/Core/Handlers/Items/Windows/ItemsViewHandler.cs"
    )
    $windowsScenario = $windowsPlatform.deviceScenarios[0]
    Assert-Equal "required-not-yet-automated" $windowsScenario.automationStatus "Windows scenario remains unsupported"
    Assert-True ($null -eq $windowsScenario.pipeline) "Unsupported scenarios must not expose a pipeline handoff"

    $carouselPlatform = Invoke-SelectorFixture "carousel-platform" @(
        "src/Controls/src/Core/Handlers/Items/Android/MauiCarouselRecyclerView.cs",
        "src/Controls/src/Core/Handlers/Items/iOS/MauiCollectionView.cs",
        "src/Controls/src/Core/Handlers/Items2/CarouselViewHandler2.iOS.cs",
        "src/Controls/src/Core/PublicAPI/net-ios/PublicAPI.Unshipped.txt"
    )
    $carouselScenarioIds = @($carouselPlatform.deviceScenarios | ForEach-Object { $_.id })
    Assert-Equal 1 $carouselScenarioIds.Count "CarouselView should select only its dedicated scenario"
    Assert-Equal "carouselview-swipe-disabled" $carouselScenarioIds[0] "CarouselView scenario selection"
    Assert-Equal "manual-device-ci-ready" $carouselPlatform.deviceScenarios[0].automationStatus "CarouselView pipeline status"
    Assert-Equal 3 @($carouselPlatform.deviceScenarios[0].pipeline.platforms).Count "CarouselView platform count"
    Assert-Equal "carouselview-swipe-disabled" $carouselPlatform.deviceScenarios[0].resultScenario "CarouselView result scenario"
    Assert-Equal 0 $carouselPlatform.coverage.staticOnlyFileCount "PublicAPI files must not create performance coverage gaps"
    Assert-Equal 3 $carouselPlatform.coverage.deviceRequiredFileCount "CarouselView device file count"

    $sharedMauiCollectionView = Invoke-SelectorFixture "shared-maui-collection-view" @(
        "src/Controls/src/Core/Handlers/Items/iOS/MauiCollectionView.cs"
    )
    Assert-Equal "collectionview-handler-device" $sharedMauiCollectionView.deviceScenarios[0].id "Shared MauiCollectionView changes need generic coverage without Carousel activation"
    Assert-Equal "required-not-yet-automated" $sharedMauiCollectionView.deviceScenarios[0].automationStatus "Shared MauiCollectionView generic scenario status"

    $controlHandler = Invoke-SelectorFixture "control-handler" @(
        "src/Core/src/Handlers/Button/ButtonHandler.cs"
    )
    Assert-Equal "static-only" $controlHandler.coverage.status "A control handler is not covered by registrar benchmarks"
    Assert-Equal 0 @($controlHandler.suites).Count "Broad handler mapping must not return a false managed suite"

    $converter = Invoke-SelectorFixture "converter" @(
        "src/Controls/src/Core/FlowDirectionConverter.cs"
    )
    Assert-Equal "static-only" $converter.coverage.status "Generic converters are not covered by TypeConversionBenchmarker"
    Assert-Equal 0 @($converter.suites).Count "Generic converters must not map to a narrow conversion benchmark"

    $multiBinding = Invoke-SelectorFixture "multi-binding" @(
        "src/Controls/src/Core/MultiBinding.cs"
    )
    Assert-Equal "static-only" $multiBinding.coverage.status "MultiBinding is not exercised by the selected binding benchmarks"
    Assert-Equal 0 @($multiBinding.suites).Count "MultiBinding must not receive false managed coverage"

    $blazor = Invoke-SelectorFixture "blazor-root" @(
        "src/BlazorWebView/src/MauiBlazorWebView.cs"
    )
    Assert-True $blazor.relevant "Shipping BlazorWebView source must be performance-relevant"
    Assert-Equal "static-only" $blazor.coverage.status "Unbenchmarked shipping roots remain static-only"

    $mapper = Invoke-SelectorFixture "mapper" @(
        "src/Core/src/PropertyMapper.cs"
    )
    Assert-Equal "managed-complete" $mapper.coverage.status "PropertyMapper should map to managed benchmarks"
    Assert-Equal 1 @($mapper.suites).Count "PropertyMapper should select one project"
    Assert-Equal "Core" $mapper.suites[0].project "PropertyMapper should select Core benchmarks"

    $mixedRoots = Invoke-SelectorFixture "mixed-roots" @(
        "src/Core/src/PropertyMapper.cs",
        "src/BlazorWebView/src/MauiBlazorWebView.cs"
    )
    Assert-Equal "mixed" $mixedRoots.coverage.status "Mixed benchmarked and unbenchmarked shipping roots must remain partial"
    Assert-True (-not $mixedRoots.coverage.canClaimWholePrClean) "Mixed-root PR cannot claim whole-PR clean coverage"

    $commonInputs = Invoke-SelectorFixture "common-inputs" @(
        "src/Core/src/PropertyMapper.cs",
        "eng/Versions.targets"
    )
    Assert-True $commonInputs.suites[0].benchmarkInputsChanged "Shared build inputs must invalidate benchmark comparison"
    Assert-True (-not $commonInputs.coverage.canClaimWholePrClean) "Changed shared build inputs cannot claim clean coverage"

    $xamlInputs = Invoke-SelectorFixture "xaml-inputs" @(
        "src/Controls/src/Xaml/XamlLoader.cs",
        "src/Controls/tests/Xaml.UnitTests/Benchmark.xaml"
    )
    Assert-Equal 1 @($xamlInputs.suites).Count "XAML product change should select XAML benchmarks"
    Assert-True $xamlInputs.suites[0].benchmarkInputsChanged "XAML unit-test fixture changes alter the benchmark workload"

    $rangeRoot = Join-Path $testRoot "allocation-range"
    Write-BenchmarkReport ([IO.Path]::Combine($rangeRoot, "base", "run1", "sample-report-full.json")) "Microsoft.Maui.Benchmarks.Sample.Run" 100
    Write-BenchmarkReport ([IO.Path]::Combine($rangeRoot, "base", "run2", "sample-report-full.json")) "Microsoft.Maui.Benchmarks.Sample.Run" 140
    Write-BenchmarkReport ([IO.Path]::Combine($rangeRoot, "head", "run1", "sample-report-full.json")) "Microsoft.Maui.Benchmarks.Sample.Run" 150
    Write-BenchmarkReport ([IO.Path]::Combine($rangeRoot, "head", "run2", "sample-report-full.json")) "Microsoft.Maui.Benchmarks.Sample.Run" 190
    $range = Invoke-ComparatorFixture "allocation-range"
    Assert-Equal "alloc-regression" $range.verdict "Non-overlapping allocation ranges should regress"
    Assert-Equal 10 $range.allocRegressions[0].confirmedDeltaBytes "Only the proven range gap should be reported"
    Assert-True $range.allocRegressions[0].confirmed "Repeated allocation evidence should be confirmed"
    Assert-True (-not $range.canClaimClean) "A confirmed regression cannot claim a clean result"

    $disjointRoot = Join-Path $testRoot "disjoint"
    Write-BenchmarkReport ([IO.Path]::Combine($disjointRoot, "base", "run1", "base-report-full.json")) "Microsoft.Maui.Benchmarks.BaseOnly" 100
    Write-BenchmarkReport ([IO.Path]::Combine($disjointRoot, "base", "run2", "base-report-full.json")) "Microsoft.Maui.Benchmarks.BaseOnly" 100
    Write-BenchmarkReport ([IO.Path]::Combine($disjointRoot, "head", "run1", "head-report-full.json")) "Microsoft.Maui.Benchmarks.HeadOnly" 100
    Write-BenchmarkReport ([IO.Path]::Combine($disjointRoot, "head", "run2", "head-report-full.json")) "Microsoft.Maui.Benchmarks.HeadOnly" 100
    $disjoint = Invoke-ComparatorFixture "disjoint"
    Assert-Equal "inconclusive" $disjoint.verdict "Disjoint benchmark sets must not be neutral"
    Assert-True (-not $disjoint.canClaimClean) "Disjoint benchmark sets cannot claim clean"
    Assert-Equal 0 $disjoint.commonCount "Disjoint fixture should have no common benchmarks"

    $incompleteRoot = Join-Path $testRoot "incomplete"
    foreach ($side in @("base", "head")) {
        Write-BenchmarkReport ([IO.Path]::Combine($incompleteRoot, $side, "run1", "sample-report-full.json")) "Microsoft.Maui.Benchmarks.Sample.Run" 100
        Write-BenchmarkReport ([IO.Path]::Combine($incompleteRoot, $side, "run2", "sample-report-full.json")) "Microsoft.Maui.Benchmarks.Sample.Run" 100
    }
    $incomplete = Invoke-ComparatorFixture "incomplete" "incomplete"
    Assert-Equal "inconclusive" $incomplete.verdict "Incomplete runner manifest must fail closed"
    Assert-True (-not $incomplete.executionComplete) "Incomplete manifest should be reflected in summary"
    Assert-True (-not $incomplete.canClaimClean) "Incomplete execution cannot claim clean"

    $missingDataRoot = Join-Path $testRoot "missing-data"
    Write-BenchmarkReport ([IO.Path]::Combine($missingDataRoot, "base", "run1", "sample-report-full.json")) "Microsoft.Maui.Benchmarks.Sample.Run" 100
    Write-BenchmarkReport ([IO.Path]::Combine($missingDataRoot, "base", "run2", "sample-report-full.json")) "Microsoft.Maui.Benchmarks.Sample.Run" 100 -IncludeMemory $false
    Write-BenchmarkReport ([IO.Path]::Combine($missingDataRoot, "head", "run1", "sample-report-full.json")) "Microsoft.Maui.Benchmarks.Sample.Run" 100
    Write-BenchmarkReport ([IO.Path]::Combine($missingDataRoot, "head", "run2", "sample-report-full.json")) "Microsoft.Maui.Benchmarks.Sample.Run" 100
    $missingData = Invoke-ComparatorFixture "missing-data"
    Assert-Equal "inconclusive" $missingData.verdict "Missing allocation data must fail closed"
    Assert-True (-not $missingData.benchmarkDataComplete) "Missing allocation data should be listed as incomplete"
    Assert-True (-not $missingData.canClaimClean) "Missing allocation data cannot claim clean"

    $noManifestSummaryPath = Join-Path $incompleteRoot "summary-no-manifest.json"
    & $comparator `
        -BaseDir (Join-Path $incompleteRoot "base") `
        -HeadDir (Join-Path $incompleteRoot "head") `
        -MarkdownOut (Join-Path $incompleteRoot "table-no-manifest.md") `
        -JsonOut $noManifestSummaryPath
    $noManifest = Get-Content $noManifestSummaryPath -Raw | ConvertFrom-Json
    Assert-Equal "inconclusive" $noManifest.verdict "A runner manifest is required for a clean verdict"
    Assert-True (-not $noManifest.executionComplete) "Missing runner manifest must be incomplete"

    Write-Output "All perf-analysis tests passed."
}
finally {
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
