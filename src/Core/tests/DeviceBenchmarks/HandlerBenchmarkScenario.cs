namespace Microsoft.Maui.DeviceBenchmarks;

internal sealed record HandlerBenchmarkScenario(
	string Name,
	Func<IView> CreateView,
	Func<IViewHandler> CreateHandler);

internal readonly record struct HandlerBenchmarkSample(
	int Iteration,
	double DurationMicroseconds,
	long ManagedAllocatedBytes,
	double? UiThreadCpuMicroseconds);

internal readonly record struct RawHandlerBenchmarkSample(
	double DurationMicroseconds,
	long ManagedAllocatedBytes,
	double? UiThreadCpuMicroseconds);

internal sealed record HandlerTransformBenchmarkResult(
	IReadOnlyList<HandlerBenchmarkSample> Samples,
	int BatchFlushes);
