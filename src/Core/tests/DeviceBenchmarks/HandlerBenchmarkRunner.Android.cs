using System.Diagnostics;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.TestUtils.DeviceTests.Runners;
using AView = Android.Views.View;
using ControlsView = Microsoft.Maui.Controls.View;

namespace Microsoft.Maui.DeviceBenchmarks;

internal static partial class HandlerBenchmarkRunner
{
	static readonly TimeSpan LayoutTimeout = TimeSpan.FromSeconds(5);

	private static partial Task<IReadOnlyList<HandlerBenchmarkSample>> RunScenarioCoreAsync(
		HandlerBenchmarkScenario scenario,
		int warmupCount,
		int iterationCount) =>
		TestDispatcher.Current.DispatchAsync(
			() => RunOnUiThreadAsync(scenario, warmupCount, iterationCount));

	public static Task RunSteadyStateAsync(
		string scenarioName,
		Func<ControlsView> createView,
		Func<IViewHandler> createHandler,
		Action<ControlsView, int> runTransaction,
		int warmupCount,
		int iterationCount,
		int transactionsPerIteration) =>
		TestDispatcher.Current.DispatchAsync(
			() => RunSteadyStateOnUiThread(
				scenarioName,
				createView,
				createHandler,
				runTransaction,
				warmupCount,
				iterationCount,
				transactionsPerIteration));

	static void RunSteadyStateOnUiThread(
		string scenarioName,
		Func<ControlsView> createView,
		Func<IViewHandler> createHandler,
		Action<ControlsView, int> runTransaction,
		int warmupCount,
		int iterationCount,
		int transactionsPerIteration)
	{
		var view = createView();
		var handler = createHandler();
		handler.SetMauiContext(new MauiContext(TestServices.Services, TestWindow.PlatformWindow));
		handler.SetVirtualView(view);

		try
		{
			for (var i = 0; i < warmupCount; i++)
				_ = MeasureSteadyStateOnce(view, runTransaction, i, transactionsPerIteration);

			var samples = new List<HandlerBenchmarkSample>(iterationCount);
			for (var i = 0; i < iterationCount; i++)
			{
				var sample = MeasureSteadyStateOnce(
					view,
					runTransaction,
					warmupCount + i,
					transactionsPerIteration);
				var benchmarkSample = new HandlerBenchmarkSample(
					i,
					sample.DurationMicroseconds,
					sample.ManagedAllocatedBytes,
					sample.UiThreadCpuMicroseconds);
				samples.Add(benchmarkSample);
				HandlerBenchmarkOutput.WriteSample(scenarioName, benchmarkSample);
			}

			HandlerBenchmarkOutput.WriteSummary(scenarioName, samples);
		}
		finally
		{
			handler.DisconnectHandler();
		}
	}

	static RawHandlerBenchmarkSample MeasureSteadyStateOnce(
		ControlsView view,
		Action<ControlsView, int> runTransaction,
		int iteration,
		int transactionsPerIteration)
	{
		var startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
		var startCpuNanoseconds = global::Android.OS.Debug.ThreadCpuTimeNanos();
		var startTimestamp = Stopwatch.GetTimestamp();

		for (var transaction = 0; transaction < transactionsPerIteration; transaction++)
			runTransaction(view, (iteration * transactionsPerIteration) + transaction);

		var durationMicroseconds = Stopwatch.GetElapsedTime(startTimestamp).TotalMicroseconds;
		var endCpuNanoseconds = global::Android.OS.Debug.ThreadCpuTimeNanos();
		double? uiThreadCpuMicroseconds =
			startCpuNanoseconds >= 0 && endCpuNanoseconds >= startCpuNanoseconds
				? (endCpuNanoseconds - startCpuNanoseconds) / 1_000.0
				: null;

		return new(
			durationMicroseconds,
			GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes,
			uiThreadCpuMicroseconds);
	}

	static async Task<IReadOnlyList<HandlerBenchmarkSample>> RunOnUiThreadAsync(
		HandlerBenchmarkScenario scenario,
		int warmupCount,
		int iterationCount)
	{
		var activity = TestWindow.PlatformWindow;
		var rootView = activity.FindViewById<FrameLayout>(global::Android.Resource.Id.Content)
			?? throw new InvalidOperationException("The benchmark activity did not provide a content root.");
		using var host = new BenchmarkHost(activity);

		rootView.AddView(
			host,
			new FrameLayout.LayoutParams(
				ViewGroup.LayoutParams.MatchParent,
				ViewGroup.LayoutParams.MatchParent));

		try
		{
			await host.WaitForInitialLayoutAsync().WaitAsync(LayoutTimeout);

			for (var i = 0; i < warmupCount; i++)
				_ = await MeasureOnceAsync(scenario, host);

			var samples = new List<HandlerBenchmarkSample>(iterationCount);
			for (var i = 0; i < iterationCount; i++)
			{
				var sample = await MeasureOnceAsync(scenario, host);
				samples.Add(new(
					i,
					sample.DurationMicroseconds,
					sample.ManagedAllocatedBytes,
					sample.UiThreadCpuMicroseconds));
			}

			return samples;
		}
		finally
		{
			rootView.RemoveView(host);
		}
	}

	static async Task<RawHandlerBenchmarkSample> MeasureOnceAsync(
		HandlerBenchmarkScenario scenario,
		BenchmarkHost host)
	{
		var view = scenario.CreateView();
		var handler = scenario.CreateHandler();
		handler.SetMauiContext(new MauiContext(TestServices.Services, TestWindow.PlatformWindow));

		var completion = new TaskCompletionSource<RawHandlerBenchmarkSample>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var completionWithTimeout = completion.Task.WaitAsync(LayoutTimeout);
		using var layoutParameters = new FrameLayout.LayoutParams(
			ViewGroup.LayoutParams.MatchParent,
			ViewGroup.LayoutParams.MatchParent);

		AView? platformView = null;
		try
		{
			var startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
			var startCpuNanoseconds = global::Android.OS.Debug.ThreadCpuTimeNanos();
			var startTimestamp = Stopwatch.GetTimestamp();

			handler.SetVirtualView(view);
			platformView = (handler.ContainerView ?? handler.PlatformView) as AView
				?? throw new InvalidOperationException(
					$"Handler '{handler.GetType().Name}' did not create an Android view.");

			host.Arm(
				completion,
				startTimestamp,
				startAllocatedBytes,
				startCpuNanoseconds);
			host.AddView(platformView, layoutParameters);

			return await completionWithTimeout;
		}
		finally
		{
			host.Reset();

			if (platformView?.Parent == host)
				host.RemoveView(platformView);

			handler.DisconnectHandler();
		}
	}

	sealed class BenchmarkHost : FrameLayout
	{
		readonly TaskCompletionSource _initialLayout =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<RawHandlerBenchmarkSample>? _completion;
		long _startTimestamp;
		long _startAllocatedBytes;
		long _startCpuNanoseconds;

		public BenchmarkHost(global::Android.App.Activity activity)
			: base(activity)
		{
		}

		public Task WaitForInitialLayoutAsync() => _initialLayout.Task;

		public void Arm(
			TaskCompletionSource<RawHandlerBenchmarkSample> completion,
			long startTimestamp,
			long startAllocatedBytes,
			long startCpuNanoseconds)
		{
			_completion = completion;
			_startTimestamp = startTimestamp;
			_startAllocatedBytes = startAllocatedBytes;
			_startCpuNanoseconds = startCpuNanoseconds;
		}

		public void Reset() => _completion = null;

		protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
		{
			base.OnLayout(changed, left, top, right, bottom);
			_initialLayout.TrySetResult();

			var completion = _completion;
			if (completion is null)
				return;

			_completion = null;
			var endCpuNanoseconds = global::Android.OS.Debug.ThreadCpuTimeNanos();
			double? uiThreadCpuMicroseconds =
				_startCpuNanoseconds >= 0 && endCpuNanoseconds >= _startCpuNanoseconds
					? (endCpuNanoseconds - _startCpuNanoseconds) / 1_000.0
					: null;

			completion.TrySetResult(new(
				Stopwatch.GetElapsedTime(_startTimestamp).TotalMicroseconds,
				GC.GetAllocatedBytesForCurrentThread() - _startAllocatedBytes,
				uiThreadCpuMicroseconds));
		}
	}
}
