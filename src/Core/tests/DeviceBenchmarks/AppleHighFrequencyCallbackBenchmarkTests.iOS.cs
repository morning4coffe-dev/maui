using CoreGraphics;
using Foundation;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;
using Xunit;
using Xunit.Abstractions;
using ControlsContentView = Microsoft.Maui.Controls.ContentView;
using ControlsEditor = Microsoft.Maui.Controls.Editor;
using ControlsEntry = Microsoft.Maui.Controls.Entry;
using ControlsScrollView = Microsoft.Maui.Controls.ScrollView;

namespace Microsoft.Maui.DeviceBenchmarks;

public class AppleHighFrequencyCallbackBenchmarkTests
{
	const int WarmupCount = 20;
	const int IterationCount = 100;
	const int CallbacksPerIteration = 100;
	const long ExpectedCallbacks = IterationCount * CallbacksPerIteration;
	const string FirstText = "callback-a";
	const string SecondText = "callback-b";
	readonly ITestOutputHelper _output;

	public AppleHighFrequencyCallbackBenchmarkTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	[Trait("Category", "Performance")]
	[Trait("Category", "AppleCallbacks")]
	public async Task HighFrequencyCallbackBursts()
	{
		HandlerBenchmarkOutput.Writer = _output.WriteLine;

		try
		{
			HandlerBenchmarkOutput.WriteAppleCallbackMetadata(
				WarmupCount,
				IterationCount,
				CallbacksPerIteration);

			await RunScenario<CountingContentView, ContentViewHandler>(
				"AppleCallbackHarnessNoOp",
				static () => new CountingContentView(),
				static () => new ContentViewHandler(),
				static (view, _, _) => view.CallbackCount++,
				ExpectedCallbacks);

			await RunScenario<CallbackScrollView, ScrollViewHandler>(
				"ScrollViewVerticalDelegateCallback",
				static () => new CallbackScrollView(),
				static () => new ScrollViewHandler(),
				static (view, handler, callback) =>
				{
					view.CallbackSource.ContentOffset = new CGPoint(
						0,
						(callback & 1) == 0 ? 10 : 20);
					GetRequiredScrollDelegate(handler).Scrolled(view.CallbackSource);
				},
				ExpectedCallbacks,
				cleanup: static (view, _) => view.CallbackSource.Dispose());

			await RunScenario<CallbackScrollView, ScrollViewHandler>(
				"ScrollViewDiagonalDelegateCallback",
				static () => new CallbackScrollView(),
				static () => new ScrollViewHandler(),
				static (view, handler, callback) =>
				{
					var first = (callback & 1) == 0;
					view.CallbackSource.ContentOffset = new CGPoint(
						first ? 10 : 20,
						first ? 30 : 40);
					GetRequiredScrollDelegate(handler).Scrolled(view.CallbackSource);
				},
				ExpectedCallbacks * 2,
				cleanup: static (view, _) => view.CallbackSource.Dispose());

			await RunScenario<CallbackEntry, EntryHandler>(
				"EntryEditingChangedStableTextCallback",
				static () => new CallbackEntry(),
				static () => new EntryHandler(),
				static (view, handler, _) =>
				{
					handler.PlatformView.SendActionForControlEvents(
						UIControlEvent.EditingChanged);
					view.CallbackCount++;
				},
				ExpectedCallbacks);

			await RunScenario<CallbackEntry, EntryHandler>(
				"EntryEditingChangedChangedTextRoundTrip",
				static () => new CallbackEntry(),
				static () => new EntryHandler(),
				static (_, handler, callback) =>
				{
					handler.PlatformView.SuppressTextPropertySet(true);
					try
					{
						handler.PlatformView.Text =
							(callback & 1) == 0 ? FirstText : SecondText;
					}
					finally
					{
						handler.PlatformView.SuppressTextPropertySet(false);
					}

					handler.PlatformView.SendActionForControlEvents(
						UIControlEvent.EditingChanged);
				},
				ExpectedCallbacks);

			await RunScenario<CallbackEditor, EditorHandler>(
				"EditorChangedStableTextCallback",
				static () => new CallbackEditor(),
				static () => new EditorHandler(),
				static (view, handler, _) =>
				{
					GetRequiredTextViewDelegate(handler).Changed(
						handler.PlatformView);
					view.CallbackCount++;
				},
				ExpectedCallbacks);

			await RunScenario<ValidationEntry, EntryHandler>(
				"EntrySingleRangeValidationCallback",
				static () => new ValidationEntry(),
				static () => new EntryHandler(),
				static (view, handler, _) =>
				{
					if (GetRequiredTextFieldDelegate(handler).ShouldChangeCharacters(
						handler.PlatformView,
						view.SingleRange,
						"x"))
					{
						view.CallbackCount++;
					}
				},
				ExpectedCallbacks,
				cleanup: static (view, _) => view.DisposeRanges());

			await RunScenario<ValidationEntry, EntryHandler>(
				"EntryThreeRangeValidationCallback",
				static () => new ValidationEntry(),
				static () => new EntryHandler(),
				static (view, handler, _) =>
				{
					if (GetRequiredTextFieldDelegate(handler).ShouldChangeCharacters(
						handler.PlatformView,
						view.ThreeRanges,
						"x"))
					{
						view.CallbackCount++;
					}
				},
				ExpectedCallbacks,
				cleanup: static (view, _) => view.DisposeRanges());

			await RunScenario<PanCallbackView, ContentViewHandler>(
				"PanGestureManagedDispatch",
				static () => new PanCallbackView(),
				static () => new ContentViewHandler(),
				static (view, _, callback) =>
					((IPanGestureController)view.Recognizer).SendPan(
						view,
						callback,
						callback * 0.5,
						1),
				ExpectedCallbacks);

			await RunScenario<PinchCallbackView, ContentViewHandler>(
				"PinchGestureManagedDispatch",
				static () => new PinchCallbackView(),
				static () => new ContentViewHandler(),
				static (view, _, callback) =>
					((IPinchGestureController)view.Recognizer).SendPinch(
						view,
						1 + ((callback & 15) * 0.01),
						new Microsoft.Maui.Graphics.Point(0.5, 0.5)),
				ExpectedCallbacks);

			await RunScenario<PointerCallbackView, ContentViewHandler>(
				"PointerMovedManagedDispatch",
				static () => new PointerCallbackView(),
				static () => new ContentViewHandler(),
				static (view, handler, callback) =>
				{
					var platformArgs = new PlatformPointerEventArgs(
						handler.PlatformView,
						view.NativeRecognizer);
					view.Recognizer.SendPointerMoved(
						view,
						_ => new Microsoft.Maui.Graphics.Point(callback, callback),
						platformArgs);
				},
				ExpectedCallbacks,
				cleanup: static (view, _) => view.NativeRecognizer.Dispose());
		}
		finally
		{
			HandlerBenchmarkOutput.Writer = null;
		}
	}

	static async Task RunScenario<TView, THandler>(
		string scenarioName,
		Func<TView> createView,
		Func<THandler> createHandler,
		Action<TView, THandler, int> runCallback,
		long expectedCallbacks,
		Action<TView, THandler>? initialize = null,
		Action<TView, THandler>? cleanup = null)
		where TView : View, ICallbackCounter
		where THandler : IViewHandler
	{
		var result = await HandlerBenchmarkRunner.RunAppleCallbackSteadyStateAsync(
			createView,
			createHandler,
			runCallback,
			static view => view.CallbackCount = 0,
			static view => view.CallbackCount,
			initialize,
			cleanup,
			WarmupCount,
			IterationCount,
			CallbacksPerIteration);

		foreach (var sample in result.Samples)
			HandlerBenchmarkOutput.WriteSample(scenarioName, sample);

		HandlerBenchmarkOutput.WriteSummary(scenarioName, result.Samples);
		HandlerBenchmarkOutput.WriteDiagnostic(
			scenarioName,
			"callbackCount",
			result.CallbackCount);

		Assert.Equal(expectedCallbacks, result.CallbackCount);
	}

	static IUIScrollViewDelegate GetRequiredScrollDelegate(
		ScrollViewHandler handler) =>
		handler.PlatformView.Delegate
			?? throw new InvalidOperationException(
				"The ScrollView callback delegate was not installed.");

	static IUITextFieldDelegate GetRequiredTextFieldDelegate(
		EntryHandler handler) =>
		handler.PlatformView.Delegate
			?? throw new InvalidOperationException(
				"The Entry callback delegate was not installed.");

	static IUITextViewDelegate GetRequiredTextViewDelegate(
		EditorHandler handler) =>
		handler.PlatformView.Delegate
			?? throw new InvalidOperationException(
				"The Editor callback delegate was not installed.");

	interface ICallbackCounter
	{
		long CallbackCount { get; set; }
	}

	sealed class CountingContentView : ControlsContentView, ICallbackCounter
	{
		public long CallbackCount { get; set; }
	}

	sealed class CallbackScrollView : ControlsScrollView, ICallbackCounter
	{
		public CallbackScrollView()
		{
			Content = new BoxView
			{
				HeightRequest = 1000,
				WidthRequest = 1000,
			};
			Orientation = ScrollOrientation.Both;
			Scrolled += (_, _) => CallbackCount++;
		}

		public long CallbackCount { get; set; }

		public UIScrollView CallbackSource { get; } = new();
	}

	sealed class CallbackEntry : ControlsEntry, ICallbackCounter
	{
		public CallbackEntry()
		{
			Text = FirstText;
			TextChanged += (_, _) => CallbackCount++;
		}

		public long CallbackCount { get; set; }
	}

	sealed class CallbackEditor : ControlsEditor, ICallbackCounter
	{
		public CallbackEditor()
		{
			Text = FirstText;
		}

		public long CallbackCount { get; set; }
	}

	sealed class ValidationEntry : ControlsEntry, ICallbackCounter
	{
		public ValidationEntry()
		{
			MaxLength = 128;
			Text = new string('a', 64);
			SingleRange =
				new[] { NSValue.FromRange(new NSRange(16, 0)) };
			ThreeRanges =
				new[]
				{
					NSValue.FromRange(new NSRange(8, 0)),
					NSValue.FromRange(new NSRange(24, 0)),
					NSValue.FromRange(new NSRange(40, 0)),
				};
		}

		public long CallbackCount { get; set; }

		public NSValue[] SingleRange { get; }

		public NSValue[] ThreeRanges { get; }

		public void DisposeRanges()
		{
			foreach (var range in SingleRange)
				range.Dispose();

			foreach (var range in ThreeRanges)
				range.Dispose();
		}
	}

	sealed class PanCallbackView : ControlsContentView, ICallbackCounter
	{
		public PanCallbackView()
		{
			Recognizer.PanUpdated += (_, _) => CallbackCount++;
			GestureRecognizers.Add(Recognizer);
		}

		public long CallbackCount { get; set; }

		public PanGestureRecognizer Recognizer { get; } = new();
	}

	sealed class PinchCallbackView : ControlsContentView, ICallbackCounter
	{
		public PinchCallbackView()
		{
			Recognizer.PinchUpdated += (_, _) => CallbackCount++;
			GestureRecognizers.Add(Recognizer);
		}

		public long CallbackCount { get; set; }

		public PinchGestureRecognizer Recognizer { get; } = new();
	}

	sealed class PointerCallbackView : ControlsContentView, ICallbackCounter
	{
		public PointerCallbackView()
		{
			Recognizer.PointerMoved += (_, _) => CallbackCount++;
			GestureRecognizers.Add(Recognizer);
		}

		public long CallbackCount { get; set; }

		public PointerGestureRecognizer Recognizer { get; } = new();

		public UIHoverGestureRecognizer NativeRecognizer { get; } =
			new(static _ => { });
	}
}
