using System.Diagnostics;
using CoreGraphics;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.TestUtils.DeviceTests.Runners;
using UIKit;

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
