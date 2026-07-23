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

#if IOS || MACCATALYST
	public static Task<IReadOnlyList<HandlerBenchmarkSample>> RunConnectCpuAsync(
		HandlerBenchmarkScenario scenario,
		int warmupCount,
		int iterationCount,
		int connectionsPerIteration) =>
		RunConnectCpuCoreAsync(
			scenario,
			warmupCount,
			iterationCount,
			connectionsPerIteration);
#endif

	private static partial Task<IReadOnlyList<HandlerBenchmarkSample>> RunScenarioCoreAsync(
		HandlerBenchmarkScenario scenario,
		int warmupCount,
		int iterationCount);

#if IOS || MACCATALYST
	private static partial Task<IReadOnlyList<HandlerBenchmarkSample>> RunConnectCpuCoreAsync(
		HandlerBenchmarkScenario scenario,
		int warmupCount,
		int iterationCount,
		int connectionsPerIteration);
#endif
}

internal static class HandlerBenchmarkOutput
{
	const string Prefix = "MAUIBENCH";
	const string NativeViewPropertyBatchingSwitch = "Microsoft.Maui.Experimental.NativeViewPropertyBatching";
	const string NativeViewPropertyUpdateBatchingSwitch =
		"Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled";

	static readonly System.Threading.AsyncLocal<Action<string>?> WriterScope = new();

	internal static Action<string>? Writer
	{
		private get => WriterScope.Value;
		set => WriterScope.Value = value;
	}

	public static void WriteMetadata(int warmupCount, int iterationCount)
	{
		var platform = GetPlatform();
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

		WriteLine(
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
		WriteLine(
			FormattableString.Invariant(
				$"{Prefix} schema=1 kind=sample scenario={scenario} iteration={sample.Iteration} durationUs={sample.DurationMicroseconds:F3} managedAllocatedBytes={sample.ManagedAllocatedBytes} uiThreadCpuUs={FormatOptional(sample.UiThreadCpuMicroseconds)}"));
	}

	public static void WriteSteadyStateMetadata(
		int warmupCount,
		int iterationCount,
		int transactionsPerIteration)
	{
		var platform = GetPlatform();
		var updateBatchingEnabled =
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool isEnabled) &&
			isEnabled;

		WriteLine(
			$"{Prefix} schema=1 kind=metadata platform={platform} " +
			$"scope=explicit-steady-state-property-transactions warmups={warmupCount} " +
			$"iterations={iterationCount} transactionsPerIteration={transactionsPerIteration} " +
			"clock=stopwatch comparisonScope=within-platform-only " +
			"managedAllocationScope=dotnet-current-ui-thread uiThreadCpu=android-only " +
			"harnessOverhead=not-subtracted javaAndNativeAllocations=not-measured " +
			"exactInteropCrossings=not-measured appStartup=not-measured " +
			$"nativeViewPropertyUpdateBatching={(updateBatchingEnabled ? "enabled" : "disabled")}");
	}

	public static void WriteConnectCpuMetadata(
		int warmupCount,
		int iterationCount,
		int connectionsPerIteration)
	{
		var platform = GetPlatform();

		WriteLine(
			$"{Prefix} schema=1 kind=metadata platform={platform} " +
			$"scope=handler-connect-cpu warmups={warmupCount} " +
			$"iterations={iterationCount} connectionsPerIteration={connectionsPerIteration} " +
			"clock=stopwatch comparisonScope=within-platform-only " +
			"managedAllocationScope=dotnet-current-ui-thread uiThreadCpu=not-measured " +
			"layout=not-measured nativeAllocations=not-measured " +
			"exactInteropCrossings=not-measured appStartup=not-measured");
	}

	public static void WriteAppleCallbackMetadata(
		int warmupCount,
		int iterationCount,
		int callbacksPerIteration)
	{
		var platform = GetPlatform();

		WriteLine(
			$"{Prefix} schema=1 kind=metadata platform={platform} " +
			$"scope=apple-high-frequency-callback-bursts warmups={warmupCount} " +
			$"iterations={iterationCount} callbacksPerIteration={callbacksPerIteration} " +
			"clock=stopwatch comparisonScope=within-platform-only " +
			"managedAllocationScope=dotnet-current-ui-thread uiThreadCpu=not-measured " +
			"nativeAllocations=not-measured appStartup=not-measured");
	}

	public static void WriteDiagnostic(string scenario, string name, long value)
	{
		WriteLine(
			$"{Prefix} schema=1 kind=diagnostic scenario={scenario} {name}={value}");
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

		WriteLine(
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

	static string GetPlatform() =>
		OperatingSystem.IsAndroid()
			? "android"
			: OperatingSystem.IsMacCatalyst()
				? "maccatalyst"
				: OperatingSystem.IsIOS()
					? "ios"
					: "unknown";

	static void WriteLine(string message)
	{
		Console.WriteLine(message);
		Writer?.Invoke(message);
	}
}
