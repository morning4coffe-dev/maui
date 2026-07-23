using System.Diagnostics;
using CoreGraphics;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.TestUtils.DeviceTests.Runners;
using UIKit;
using ControlsGrid = Microsoft.Maui.Controls.Grid;
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

	private static partial Task<IReadOnlyList<HandlerBenchmarkSample>> RunConnectCpuCoreAsync(
		HandlerBenchmarkScenario scenario,
		int warmupCount,
		int iterationCount,
		int connectionsPerIteration) =>
		TestDispatcher.Current.DispatchAsync(
			() => RunConnectCpuOnUiThread(
				scenario,
				warmupCount,
				iterationCount,
				connectionsPerIteration));

	public static Task<HandlerTransformBenchmarkResult> RunTransformSteadyStateAsync(
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

	public static Task<AppleCallbackBenchmarkResult> RunAppleCallbackSteadyStateAsync<TView, THandler>(
		Func<TView> createView,
		Func<THandler> createHandler,
		Action<TView, THandler, int> runCallback,
		Action<TView> resetDiagnostics,
		Func<TView, long> getCallbackCount,
		Action<TView, THandler>? initialize,
		Action<TView, THandler>? cleanup,
		int warmupCount,
		int iterationCount,
		int callbacksPerIteration)
		where TView : ControlsView
		where THandler : IViewHandler =>
		TestDispatcher.Current.DispatchAsync(
			() => RunAppleCallbackSteadyStateOnUiThread(
				createView,
				createHandler,
				runCallback,
				resetDiagnostics,
				getCallbackCount,
				initialize,
				cleanup,
				warmupCount,
				iterationCount,
				callbacksPerIteration));

	static HandlerTransformBenchmarkResult RunSteadyStateOnUiThread(
		string scenarioName,
		Func<ControlsView> createView,
		Func<IViewHandler> createHandler,
		Action<ControlsView, int> runTransaction,
		int warmupCount,
		int iterationCount,
		int transactionsPerIteration)
	{
		var parent = new ControlsGrid();
		var view = createView();
		parent.Add(view);
		view.Frame = new Rect(0, 0, 120, 80);

		var handler = createHandler();
		handler.SetMauiContext(new MauiContext(TestServices.Services));
		handler.SetVirtualView(view);
		handler.PlatformArrange(view.Frame);

		var viewHandler = handler as ViewHandler
			?? throw new InvalidOperationException(
				$"Handler '{handler.GetType().Name}' was not a ViewHandler.");

		try
		{
			for (var i = 0; i < warmupCount; i++)
				_ = MeasureSteadyStateOnce(view, runTransaction, i, transactionsPerIteration);

			viewHandler.ResetNativePropertyUpdateDiagnostics();
			if (handler is ExperimentalConfigurationButtonHandler configurationHandler)
				configurationHandler.ResetConfigurationDiagnostics();

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
			}

			return new(
				samples,
				viewHandler.NativePropertyUpdateBatchFlushCount,
				(handler as ExperimentalConfigurationButtonHandler)?.ConfigurationApplyCount ?? 0,
				(handler as ExperimentalConfigurationButtonHandler)?.ConfigurationBatchFlushCount ?? 0);
		}
		finally
		{
			handler.DisconnectHandler();
			parent.Remove(view);
		}
	}

	static RawHandlerBenchmarkSample MeasureSteadyStateOnce(
		ControlsView view,
		Action<ControlsView, int> runTransaction,
		int iteration,
		int transactionsPerIteration)
	{
		var startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
		var startTimestamp = Stopwatch.GetTimestamp();

		for (var transaction = 0; transaction < transactionsPerIteration; transaction++)
			runTransaction(view, (iteration * transactionsPerIteration) + transaction);

		return new(
			Stopwatch.GetElapsedTime(startTimestamp).TotalMicroseconds,
			GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes,
			null);
	}

	static AppleCallbackBenchmarkResult RunAppleCallbackSteadyStateOnUiThread<TView, THandler>(
		Func<TView> createView,
		Func<THandler> createHandler,
		Action<TView, THandler, int> runCallback,
		Action<TView> resetDiagnostics,
		Func<TView, long> getCallbackCount,
		Action<TView, THandler>? initialize,
		Action<TView, THandler>? cleanup,
		int warmupCount,
		int iterationCount,
		int callbacksPerIteration)
		where TView : ControlsView
		where THandler : IViewHandler
	{
		var parent = new ControlsGrid();
		var view = createView();
		parent.Add(view);
		view.Frame = new Rect(0, 0, 320, 240);

		var handler = createHandler();
		handler.SetMauiContext(new MauiContext(TestServices.Services));
		handler.SetVirtualView(view);
		handler.PlatformArrange(view.Frame);

		try
		{
			initialize?.Invoke(view, handler);

			for (var i = 0; i < warmupCount; i++)
			{
				_ = MeasureAppleCallbackOnce(
					view,
					handler,
					runCallback,
					i * callbacksPerIteration,
					callbacksPerIteration);
			}

			resetDiagnostics(view);

			var samples = new List<HandlerBenchmarkSample>(iterationCount);
			for (var i = 0; i < iterationCount; i++)
			{
				var sample = MeasureAppleCallbackOnce(
					view,
					handler,
					runCallback,
					(warmupCount + i) * callbacksPerIteration,
					callbacksPerIteration);
				samples.Add(new(
					i,
					sample.DurationMicroseconds,
					sample.ManagedAllocatedBytes,
					sample.UiThreadCpuMicroseconds));
			}

			return new(samples, getCallbackCount(view));
		}
		finally
		{
			cleanup?.Invoke(view, handler);
			handler.DisconnectHandler();
			parent.Remove(view);
		}
	}

	static RawHandlerBenchmarkSample MeasureAppleCallbackOnce<TView, THandler>(
		TView view,
		THandler handler,
		Action<TView, THandler, int> runCallback,
		int firstCallback,
		int callbacksPerIteration)
		where TView : ControlsView
		where THandler : IViewHandler
	{
		var startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
		var startTimestamp = Stopwatch.GetTimestamp();

		for (var callback = 0; callback < callbacksPerIteration; callback++)
			runCallback(view, handler, firstCallback + callback);

		return new(
			Stopwatch.GetElapsedTime(startTimestamp).TotalMicroseconds,
			GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes,
			null);
	}

	static IReadOnlyList<HandlerBenchmarkSample> RunConnectCpuOnUiThread(
		HandlerBenchmarkScenario scenario,
		int warmupCount,
		int iterationCount,
		int connectionsPerIteration)
	{
		for (var i = 0; i < warmupCount; i++)
		{
			_ = MeasureConnectCpuOnce(
				scenario,
				connectionsPerIteration);
		}

		var samples = new List<HandlerBenchmarkSample>(iterationCount);
		for (var i = 0; i < iterationCount; i++)
		{
			var sample = MeasureConnectCpuOnce(
				scenario,
				connectionsPerIteration);
			samples.Add(new(
				i,
				sample.DurationMicroseconds,
				sample.ManagedAllocatedBytes,
				sample.UiThreadCpuMicroseconds));
		}

		return samples;
	}

	static RawHandlerBenchmarkSample MeasureConnectCpuOnce(
		HandlerBenchmarkScenario scenario,
		int connectionsPerIteration)
	{
		var startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
		var startTimestamp = Stopwatch.GetTimestamp();

		for (var connection = 0; connection < connectionsPerIteration; connection++)
		{
			var view = scenario.CreateView();
			var handler = scenario.CreateHandler();
			handler.SetMauiContext(new MauiContext(TestServices.Services));

			try
			{
				handler.SetVirtualView(view);
			}
			finally
			{
				handler.DisconnectHandler();
			}
		}

		return new(
			Stopwatch.GetElapsedTime(startTimestamp).TotalMicroseconds,
			GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes,
			null);
	}

	static async Task<IReadOnlyList<HandlerBenchmarkSample>> RunOnUiThreadAsync(
		HandlerBenchmarkScenario scenario,
		int warmupCount,
		int iterationCount)
	{
		var window = TestWindow.PlatformWindow;
		var rootView = window.RootViewController?.View
			?? throw new InvalidOperationException("The benchmark window did not provide a root view.");
		using var host = new BenchmarkHost(rootView.Bounds)
		{
			AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
		};

		rootView.AddSubview(host);
		rootView.SetNeedsLayout();
		rootView.LayoutIfNeeded();
		host.SetNeedsLayout();
		host.LayoutIfNeeded();

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
			host.RemoveFromSuperview();
		}
	}

	static async Task<RawHandlerBenchmarkSample> MeasureOnceAsync(
		HandlerBenchmarkScenario scenario,
		BenchmarkHost host)
	{
		var view = scenario.CreateView();
		var handler = scenario.CreateHandler();
		handler.SetMauiContext(new MauiContext(TestServices.Services));

		var completion = new TaskCompletionSource<RawHandlerBenchmarkSample>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var completionWithTimeout = completion.Task.WaitAsync(LayoutTimeout);

		UIView? platformView = null;
		try
		{
			var startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
			var startTimestamp = Stopwatch.GetTimestamp();

			handler.SetVirtualView(view);
			platformView = (handler.ContainerView ?? handler.PlatformView) as UIView
				?? throw new InvalidOperationException(
					$"Handler '{handler.GetType().Name}' did not create a UIKit view.");

			host.Arm(completion, startTimestamp, startAllocatedBytes);
			host.AddSubview(platformView);
			platformView.SetNeedsLayout();
			host.SetNeedsLayout();

			return await completionWithTimeout;
		}
		finally
		{
			host.Reset();
			platformView?.RemoveFromSuperview();
			handler.DisconnectHandler();
		}
	}

	sealed class BenchmarkHost : UIView
	{
		readonly TaskCompletionSource _initialLayout =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<RawHandlerBenchmarkSample>? _completion;
		long _startTimestamp;
		long _startAllocatedBytes;

		public BenchmarkHost(CGRect frame)
			: base(frame)
		{
		}

		public Task WaitForInitialLayoutAsync() => _initialLayout.Task;

		public void Arm(
			TaskCompletionSource<RawHandlerBenchmarkSample> completion,
			long startTimestamp,
			long startAllocatedBytes)
		{
			_completion = completion;
			_startTimestamp = startTimestamp;
			_startAllocatedBytes = startAllocatedBytes;
		}

		public void Reset() => _completion = null;

		public override void LayoutSubviews()
		{
			base.LayoutSubviews();

			// UIKit lays out children after the parent returns. Force the child pass here so
			// the endpoint includes the MAUI view's first layout. This harness work is part of
			// the sample, so results are suitable for within-platform comparisons only.
			// Scenarios with active safe-area handling need a separate completion probe because
			// MauiView can invalidate its ancestors and defer completion to another layout pass.
			foreach (var subview in Subviews)
			{
				subview.Frame = Bounds;
				subview.SetNeedsLayout();
				subview.LayoutIfNeeded();
			}

			_initialLayout.TrySetResult();

			var completion = _completion;
			if (completion is null)
				return;

			_completion = null;
			completion.TrySetResult(new(
				Stopwatch.GetElapsedTime(_startTimestamp).TotalMicroseconds,
				GC.GetAllocatedBytesForCurrentThread() - _startAllocatedBytes,
				null));
		}
	}
}
