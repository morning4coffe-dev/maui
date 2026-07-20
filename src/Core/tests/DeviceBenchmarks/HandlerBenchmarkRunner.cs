using System.Globalization;

namespace Microsoft.Maui.DeviceBenchmarks;

internal static partial class HandlerBenchmarkRunner
{
	public static async Task RunAsync(
		HandlerBenchmarkScenario scenario,
		int warmupCount,
		int iterationCount)
	{
		var samples = await RunScenarioCoreAsync(scenario, warmupCount, iterationCount);

		foreach (var sample in samples)
			HandlerBenchmarkOutput.WriteSample(scenario.Name, sample);

		HandlerBenchmarkOutput.WriteSummary(scenario.Name, samples);
	}

	private static partial Task<IReadOnlyList<HandlerBenchmarkSample>> RunScenarioCoreAsync(
		HandlerBenchmarkScenario scenario,
		int warmupCount,
		int iterationCount);
}

internal static class HandlerBenchmarkOutput
{
	const string Prefix = "MAUIBENCH";
	const string NativeViewPropertyBatchingSwitch = "Microsoft.Maui.Experimental.NativeViewPropertyBatching";
	const string NativeViewPropertyUpdateBatchingSwitch =
		"Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled";

	public static void WriteMetadata(int warmupCount, int iterationCount)
	{
		var platform = OperatingSystem.IsAndroid()
			? "android"
			: OperatingSystem.IsIOS()
				? "ios"
				: OperatingSystem.IsMacCatalyst()
					? "maccatalyst"
					: "unknown";
		var appleExecutionStatus = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()
			? "executed-by-current-run"
			: "not-executed-by-current-run";
		var iosLayoutHarnessMode = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()
			? "synchronous-child-layout-if-needed"
			: "not-applicable";
		var nativeViewPropertyBatching = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()
			? AppContext.TryGetSwitch(NativeViewPropertyBatchingSwitch, out bool isEnabled) && isEnabled
				? "enabled"
				: "disabled"
			: "not-applicable";

		Console.WriteLine(
			$"{Prefix} schema=1 kind=metadata platform={platform} " +
			$"scope=handler-connect-to-first-layout warmups={warmupCount} iterations={iterationCount} " +
			"clock=stopwatch comparisonScope=within-platform-only " +
			"managedAllocationScope=dotnet-current-ui-thread " +
			"uiThreadCpu=android-only harnessOverhead=not-subtracted " +
			"javaAndNativeAllocations=not-measured exactInteropCrossings=not-measured " +
			$"iosLayoutHarnessMode={iosLayoutHarnessMode} " +
			$"nativeViewPropertyBatching={nativeViewPropertyBatching} " +
			$"appStartup=not-measured appleExecutionStatus={appleExecutionStatus}");
	}

	public static void WriteSample(string scenario, HandlerBenchmarkSample sample)
	{
		Console.WriteLine(
			FormattableString.Invariant(
				$"{Prefix} schema=1 kind=sample scenario={scenario} iteration={sample.Iteration} durationUs={sample.DurationMicroseconds:F3} managedAllocatedBytes={sample.ManagedAllocatedBytes} uiThreadCpuUs={FormatOptional(sample.UiThreadCpuMicroseconds)}"));
	}

	public static void WriteSteadyStateMetadata(
		int warmupCount,
		int iterationCount,
		int transactionsPerIteration)
	{
		var updateBatchingEnabled =
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool isEnabled) &&
			isEnabled;

		Console.WriteLine(
			$"{Prefix} schema=1 kind=metadata platform=android " +
			$"scope=explicit-steady-state-property-transactions warmups={warmupCount} " +
			$"iterations={iterationCount} transactionsPerIteration={transactionsPerIteration} " +
			"clock=stopwatch comparisonScope=within-platform-only " +
			"managedAllocationScope=dotnet-current-ui-thread uiThreadCpu=android-only " +
			"harnessOverhead=not-subtracted javaAndNativeAllocations=not-measured " +
			"exactInteropCrossings=not-measured appStartup=not-measured " +
			$"nativeViewPropertyUpdateBatching={(updateBatchingEnabled ? "enabled" : "disabled")}");
	}

	public static void WriteSummary(string scenario, IReadOnlyList<HandlerBenchmarkSample> samples)
	{
		if (samples.Count == 0)
			throw new InvalidOperationException($"Scenario '{scenario}' produced no samples.");

		var durations = samples
			.Select(sample => sample.DurationMicroseconds)
			.Order()
			.ToArray();
		var cpuSamples = samples
			.Where(sample => sample.UiThreadCpuMicroseconds.HasValue)
			.Select(sample => sample.UiThreadCpuMicroseconds!.Value)
			.ToArray();

		Console.WriteLine(
			FormattableString.Invariant(
				$"{Prefix} schema=1 kind=summary scenario={scenario} count={samples.Count} meanDurationUs={durations.Average():F3} p50DurationUs={Percentile(durations, 0.50):F3} p95DurationUs={Percentile(durations, 0.95):F3} minDurationUs={durations[0]:F3} maxDurationUs={durations[^1]:F3} meanManagedAllocatedBytes={samples.Average(sample => sample.ManagedAllocatedBytes):F3} meanUiThreadCpuUs={FormatOptional(cpuSamples.Length == 0 ? null : cpuSamples.Average())} percentileMethod=nearest-rank"));
	}

	static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
	{
		var index = Math.Clamp(
			(int)Math.Ceiling(percentile * sortedValues.Count) - 1,
			0,
			sortedValues.Count - 1);

		return sortedValues[index];
	}

	static string FormatOptional(double? value) =>
		value?.ToString("F3", CultureInfo.InvariantCulture) ?? "na";
}
