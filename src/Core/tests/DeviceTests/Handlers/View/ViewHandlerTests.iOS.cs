using System.Threading.Tasks;
using Microsoft.Maui.DeviceTests.Stubs;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;
using Xunit;

namespace Microsoft.Maui.DeviceTests
{
	public partial class ViewHandlerTests
	{
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
	}
}