using System;
using System.Threading.Tasks;
using Microsoft.Maui.DeviceTests.Stubs;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;
using Xunit;
using ControlsContentView = Microsoft.Maui.Controls.ContentView;
using ControlsGrid = Microsoft.Maui.Controls.Grid;

namespace Microsoft.Maui.DeviceTests
{
	public partial class ViewHandlerTests
	{
		const string NativeViewPropertyUpdateBatchingSwitch =
			"Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled";

		[Fact]
		public Task NativeViewPropertyBatcherAppliesControlAndContainerProperties() =>
			InvokeOnMainThreadAsync(() =>
			{
				var platformView = new UIButton();
				var containerView = new UIView();
				var view = new StubBase
				{
					FlowDirection = FlowDirection.RightToLeft,
					IsEnabled = false,
					Opacity = 0.42,
					Visibility = Visibility.Hidden,
				};

				platformView.InitializeNativeViewProperties(containerView, true, view);

				Assert.True(platformView.Hidden);
				Assert.True(containerView.Hidden);
				Assert.False(platformView.Enabled);
				Assert.Equal(UISemanticContentAttribute.ForceRightToLeft, platformView.SemanticContentAttribute);
				Assert.Equal(1, (double)platformView.Alpha);
				Assert.Equal(0.42, (double)containerView.Alpha, 3);
			});

		[Fact]
		public Task NativeViewPropertyBatcherDisablesInteractionForOrdinaryViews() =>
			InvokeOnMainThreadAsync(() =>
			{
				var platformView = new UIView();
				var view = new StubBase { IsEnabled = false };

				platformView.InitializeNativeViewProperties(platformView, false, view);

				Assert.False(platformView.UserInteractionEnabled);
			});

		[Fact]
		public async Task NativeViewPropertyBatchingRunsBeforeMapperOverrides()
		{
			var view = new StubBase { IsEnabled = false };
			var mapperOverride = new PropertyMapper<StubBase, StubBaseHandler>();
			var mapperOverrideRan = false;
			var nativeBatchRanFirst = false;

			mapperOverride[nameof(IView.IsEnabled)] = (handler, _) =>
			{
				mapperOverrideRan = true;
				nativeBatchRanFirst =
					ViewHandler.DidInitializeNativeViewProperties(handler) &&
					!handler.PlatformView.UserInteractionEnabled;
			};
			view.PropertyMapperOverrides = mapperOverride;

			AppContext.TryGetSwitch(ViewHandler.NativeViewPropertyBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(ViewHandler.NativeViewPropertyBatchingSwitch, true);

			try
			{
				await InvokeOnMainThreadAsync(() => CreateHandler(view));
			}
			finally
			{
				AppContext.SetSwitch(ViewHandler.NativeViewPropertyBatchingSwitch, originalSwitchValue);
			}

			Assert.True(mapperOverrideRan);
			Assert.True(nativeBatchRanFirst);
		}

		[Fact]
		public Task NativeViewPropertyBatcherPreservesCollapsedState() =>
			InvokeOnMainThreadAsync(() =>
			{
				var platformView = new UIView();
				var containerView = new UIView();
				var view = new StubBase { Visibility = Visibility.Collapsed };

				platformView.InitializeNativeViewProperties(containerView, true, view);

				Assert.True(platformView.Hidden);
				Assert.True(containerView.Hidden);
				Assert.Contains(platformView.Constraints, constraint => constraint is CollapseConstraint && constraint.Active);
				Assert.Contains(containerView.Constraints, constraint => constraint is CollapseConstraint && constraint.Active);

				platformView.UpdateVisibility(Visibility.Visible);
				containerView.UpdateVisibility(Visibility.Visible);

				Assert.False(platformView.Hidden);
				Assert.False(containerView.Hidden);
				Assert.DoesNotContain(platformView.Constraints, constraint => constraint is CollapseConstraint && constraint.Active);
				Assert.DoesNotContain(containerView.Constraints, constraint => constraint is CollapseConstraint && constraint.Active);
			});

		[Fact]
		public async Task NestedVisualElementTransformBatchFlushesOnlyAtOuterCommit()
		{
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, true);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var parent = new ControlsGrid();
					var view = new ControlsContentView();
					parent.Add(view);
					view.Frame = new Rect(0, 0, 120, 80);

					var handler = new ContentViewHandler();
					handler.SetMauiContext(MauiContext);
					handler.SetVirtualView(view);
					handler.PlatformArrange(view.Frame);
					handler.ResetNativePropertyUpdateDiagnostics();

					try
					{
						view.BatchBegin();
						view.BatchBegin();
						view.TranslationX = 6;
						view.TranslationY = 8;
						view.Scale = 0.9;
						view.Rotation = 12;

						Assert.True(handler.PlatformView.Layer.Transform.IsIdentity);

						view.BatchCommit();

						Assert.True(handler.PlatformView.Layer.Transform.IsIdentity);
						Assert.Equal(0, handler.NativePropertyUpdateBatchFlushCount);

						view.BatchCommit();

						Assert.Equal(1, handler.NativePropertyUpdateBatchFlushCount);
						Assert.False(handler.PlatformView.Layer.Transform.IsIdentity);
					}
					finally
					{
						((IElementHandler)handler).DisconnectHandler();
						parent.Remove(view);
					}
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}

		[Fact]
		public async Task VisualElementTransformBatchRemainsSynchronousWhenFeatureIsDisabled()
		{
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, false);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var parent = new ControlsGrid();
					var view = new ControlsContentView();
					parent.Add(view);
					view.Frame = new Rect(0, 0, 120, 80);

					var handler = new ContentViewHandler();
					handler.SetMauiContext(MauiContext);
					handler.SetVirtualView(view);
					handler.PlatformArrange(view.Frame);
					handler.ResetNativePropertyUpdateDiagnostics();

					try
					{
						view.BatchBegin();
						view.TranslationX = 5;

						Assert.False(handler.PlatformView.Layer.Transform.IsIdentity);

						view.BatchCommit();

						Assert.Equal(0, handler.NativePropertyUpdateBatchFlushCount);
					}
					finally
					{
						((IElementHandler)handler).DisconnectHandler();
						parent.Remove(view);
					}
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}

		[Fact]
		public async Task NativeTransformBatchPreservesMapperExtensions()
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
					handler.ResetNativePropertyUpdateDiagnostics();
					customMapperRuns = 0;

					handler.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand, null);
					view.TranslationX = 3;
					handler.UpdateValue(nameof(IView.TranslationX));
					view.TranslationX = 5;
					handler.UpdateValue(nameof(IView.TranslationX));

					Assert.Equal(2, customMapperRuns);
					Assert.Equal(0, handler.NativePropertyUpdateBatchFlushCount);

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
		public async Task ReplacedTransformMapperBypassesNativeBatch()
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
						[nameof(IView.TranslationX)] = (_, _) => customMapperRuns++,
					};
					var view = new StubBase();
					var handler = new StubBaseHandler(mapper);
					InitializeViewHandler(view, handler);
					handler.ResetNativePropertyUpdateDiagnostics();
					customMapperRuns = 0;

					handler.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand, null);
					view.TranslationX = 5;
					handler.UpdateValue(nameof(IView.TranslationX));
					handler.Invoke(ViewHandler.CommitNativePropertyUpdateBatchCommand, null);

					Assert.Equal(1, customMapperRuns);
					Assert.Equal(0, handler.NativePropertyUpdateBatchFlushCount);
				});
			}
			finally
			{
				AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, originalSwitchValue);
			}
		}

		[Fact]
		public async Task DisconnectClearsPendingNativeTransformBatch()
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
					handler.ResetNativePropertyUpdateDiagnostics();

					handler.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand, null);
					view.TranslationX = 5;
					handler.UpdateValue(nameof(IView.TranslationX));
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
	}
}