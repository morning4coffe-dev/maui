using System;
using System.Threading.Tasks;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.DeviceTests.Stubs;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Xunit;
using ControlsContentView = Microsoft.Maui.Controls.ContentView;

namespace Microsoft.Maui.DeviceTests
{
	public partial class ViewHandlerTests
	{
		const string NativeViewPropertyUpdateBatchingSwitch =
			"Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled";

		[Fact]
		public async Task ChildIsVisibleIfWrapperIsVisible()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var child = new Button(MauiContext.Context);
				child.Visibility = ViewStates.Gone;

				var wrapper = new WrapperView(MauiContext.Context);
				wrapper.Visibility = ViewStates.Gone;
				wrapper.AddView(child);

				Assert.Equal(ViewStates.Gone, wrapper.Visibility);
				Assert.Equal(ViewStates.Gone, child.Visibility);

				wrapper.Visibility = ViewStates.Visible;

				Assert.Equal(ViewStates.Visible, wrapper.Visibility);
				Assert.Equal(ViewStates.Visible, child.Visibility);
			});
		}

		[Fact]
		public async Task NativePropertyUpdatesFlushOnceWithFinalValues()
		{
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, true);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var customMapperRuns = 0;
					var mapper = new PropertyMapper<StubBase, StubBaseHandler>(StubBaseHandler.StubMapper);
					mapper.AppendToMapping(
						nameof(IView.TranslationX),
						(_, _) => customMapperRuns++);

					var view = new StubBase();
					var handler = new StubBaseHandler(mapper);
					InitializeViewHandler(view, handler);

					handler.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand, null);

					view.IsEnabled = false;
					handler.UpdateValue(nameof(IView.IsEnabled));
					view.Opacity = 0.42;
					handler.UpdateValue(nameof(IView.Opacity));
					view.TranslationX = 3;
					handler.UpdateValue(nameof(IView.TranslationX));
					view.TranslationX = 5;
					handler.UpdateValue(nameof(IView.TranslationX));
					view.TranslationY = 7;
					handler.UpdateValue(nameof(IView.TranslationY));
					view.Scale = 0.8;
					handler.UpdateValue(nameof(IView.Scale));
					view.ScaleX = 0.9;
					handler.UpdateValue(nameof(IView.ScaleX));
					view.ScaleY = 0.7;
					handler.UpdateValue(nameof(IView.ScaleY));
					view.Rotation = 11;
					handler.UpdateValue(nameof(IView.Rotation));
					view.RotationX = 13;
					handler.UpdateValue(nameof(IView.RotationX));
					view.RotationY = 17;
					handler.UpdateValue(nameof(IView.RotationY));

					Assert.True(handler.PlatformView.Enabled);
					Assert.Equal(1, handler.PlatformView.Alpha);
					Assert.Equal(0, handler.PlatformView.TranslationX);
					Assert.Equal(2, customMapperRuns);

					handler.Invoke(ViewHandler.CommitNativePropertyUpdateBatchCommand, null);

					var context = handler.PlatformView.Context;
					Assert.False(handler.PlatformView.Enabled);
					Assert.Equal(0.42f, handler.PlatformView.Alpha, 3);
					Assert.Equal((float)context.ToPixels(5), handler.PlatformView.TranslationX, 3);
					Assert.Equal((float)context.ToPixels(7), handler.PlatformView.TranslationY, 3);
					Assert.Equal(0.72f, handler.PlatformView.ScaleX, 3);
					Assert.Equal(0.56f, handler.PlatformView.ScaleY, 3);
					Assert.Equal(11, handler.PlatformView.Rotation);
					Assert.Equal(13, handler.PlatformView.RotationX);
					Assert.Equal(17, handler.PlatformView.RotationY);
					Assert.Equal(1, handler.NativePropertyUpdateBatchFlushCount);

					handler.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand, null);
					handler.Invoke(ViewHandler.CommitNativePropertyUpdateBatchCommand, null);
					Assert.Equal(1, handler.NativePropertyUpdateBatchFlushCount);
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}

		[Fact]
		public async Task NestedVisualElementBatchFlushesOnlyAtOuterCommit()
		{
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, true);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var view = new ControlsContentView();
					var handler = new ContentViewHandler();
					handler.SetMauiContext(MauiContext);
					handler.SetVirtualView(view);

					try
					{
						view.BatchBegin();
						view.BatchBegin();
						view.Opacity = 0.4;
						view.TranslationX = 6;

						Assert.Equal(1, handler.PlatformView.Alpha);
						Assert.Equal(0, handler.PlatformView.TranslationX);

						view.BatchCommit();

						Assert.Equal(1, handler.PlatformView.Alpha);
						Assert.Equal(0, handler.NativePropertyUpdateBatchFlushCount);

						view.BatchCommit();

						Assert.Equal(0.4f, handler.PlatformView.Alpha, 3);
						Assert.Equal(
							(float)handler.PlatformView.Context.ToPixels(6),
							handler.PlatformView.TranslationX,
							3);
						Assert.Equal(1, handler.NativePropertyUpdateBatchFlushCount);
					}
					finally
					{
						((IElementHandler)handler).DisconnectHandler();
					}
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}

		[Fact]
		public async Task VisualElementBatchRemainsSynchronousWhenFeatureIsDisabled()
		{
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, false);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var view = new ControlsContentView();
					var handler = new ContentViewHandler();
					handler.SetMauiContext(MauiContext);
					handler.SetVirtualView(view);

					try
					{
						view.BatchBegin();
						view.Opacity = 0.25;

						Assert.Equal(0.25f, handler.PlatformView.Alpha, 3);

						view.BatchCommit();

						Assert.Equal(0, handler.NativePropertyUpdateBatchFlushCount);
					}
					finally
					{
						((IElementHandler)handler).DisconnectHandler();
					}
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}

		[Fact]
		public async Task ReplacedMapperBypassesNativePropertyBatch()
		{
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, true);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var customMapperRuns = 0;
					var mapper = new PropertyMapper<StubBase, StubBaseHandler>(StubBaseHandler.StubMapper)
					{
						[nameof(IView.Opacity)] = (_, _) => customMapperRuns++,
					};
					var view = new StubBase();
					var handler = new StubBaseHandler(mapper);
					InitializeViewHandler(view, handler);

					handler.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand, null);
					view.Opacity = 0.5;
					handler.UpdateValue(nameof(IView.Opacity));
					handler.Invoke(ViewHandler.CommitNativePropertyUpdateBatchCommand, null);

					Assert.Equal(1, customMapperRuns);
					Assert.Equal(1, handler.PlatformView.Alpha);
					Assert.Equal(0, handler.NativePropertyUpdateBatchFlushCount);
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}
		[Fact]
		public async Task BatchedOpacityTargetsContainerCreatedDuringBatch()
		{
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, true);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var view = new StubBase();
					var handler = new StubBaseHandler();
					InitializeViewHandler(view, handler);

					handler.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand, null);

					view.Shadow = new ShadowStub();
					handler.UpdateValue(nameof(IView.Shadow));
					view.Opacity = 0.35;
					handler.UpdateValue(nameof(IView.Opacity));

					handler.Invoke(ViewHandler.CommitNativePropertyUpdateBatchCommand, null);

					var containerView = Assert.IsType<WrapperView>(handler.ContainerView);
					Assert.Equal(0.35f, containerView.Alpha, 3);
					Assert.Equal(1, handler.PlatformView.Alpha);
					Assert.Equal(1, handler.NativePropertyUpdateBatchFlushCount);
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}

		[Fact]
		public async Task BatchedOpacityTargetsPlatformViewWhenContainerIsRemovedDuringBatch()
		{
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, true);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var view = new StubBase
					{
						Shadow = new ShadowStub(),
					};
					var handler = new StubBaseHandler();
					InitializeViewHandler(view, handler);
					Assert.IsType<WrapperView>(handler.ContainerView);

					handler.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand, null);
					view.Opacity = 0.3;
					handler.UpdateValue(nameof(IView.Opacity));
					view.Shadow = null;
					handler.UpdateValue(nameof(IView.Shadow));
					handler.Invoke(ViewHandler.CommitNativePropertyUpdateBatchCommand, null);

					Assert.Null(handler.ContainerView);
					Assert.Equal(0.3f, handler.PlatformView.Alpha, 3);
					Assert.Equal(1, handler.NativePropertyUpdateBatchFlushCount);
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}

		[Fact]
		public async Task DisconnectClearsPendingNativePropertyBatch()
		{
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, true);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var view = new StubBase();
					var handler = new StubBaseHandler();
					InitializeViewHandler(view, handler);

					handler.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand, null);
					view.Opacity = 0.2;
					handler.UpdateValue(nameof(IView.Opacity));
					((IElementHandler)handler).DisconnectHandler();
					handler.Invoke(ViewHandler.CommitNativePropertyUpdateBatchCommand, null);

					Assert.Equal(0, handler.NativePropertyUpdateBatchFlushCount);
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}

		[Fact]
		public async Task BatchedNaNScalePreservesPlatformScale()
		{
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, true);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var view = new StubBase();
					var handler = new StubBaseHandler();
					InitializeViewHandler(view, handler);
					handler.PlatformView.ScaleX = 2;
					handler.PlatformView.ScaleY = 3;

					handler.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand, null);
					view.Scale = double.NaN;
					handler.UpdateValue(nameof(IView.Scale));
					handler.Invoke(ViewHandler.CommitNativePropertyUpdateBatchCommand, null);

					Assert.Equal(2, handler.PlatformView.ScaleX);
					Assert.Equal(3, handler.PlatformView.ScaleY);
					Assert.Equal(0, handler.NativePropertyUpdateBatchFlushCount);
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}
	}
}