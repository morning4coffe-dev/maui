using System;
using System.Threading.Tasks;
using Microsoft.Maui.DeviceTests.Stubs;
using Microsoft.Maui.Handlers;
using UIKit;
using Xunit;

namespace Microsoft.Maui.DeviceTests
{
	[Category(TestCategory.Button)]
	public class ExperimentalSwiftUIButtonHandlerTests : CoreHandlerTestBase
	{
		[Fact]
		public async Task MapsStateAndRoutesNativeCallback()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var clicks = 0;
				var presses = 0;
				var releases = 0;
				var eventOrder = new System.Collections.Generic.List<string>();
				var button = new ButtonStub
				{
					AutomationId = "swiftui-button",
					IsEnabled = false,
					Semantics = new Semantics
					{
						Description = "SwiftUI description",
						Hint = "SwiftUI hint",
					},
					Text = "SwiftUI text",
				};
				button.Clicked += (_, _) =>
				{
					clicks++;
					eventOrder.Add("clicked");
				};
				button.Pressed += (_, _) =>
				{
					presses++;
					eventOrder.Add("pressed");
				};
				button.Released += (_, _) =>
				{
					releases++;
					eventOrder.Add("released");
				};

				var handler = new ExperimentalSwiftUIButtonHandler();
				handler.SetMauiContext(MauiContext);
				handler.SetVirtualView(button);
				var controller = handler.Controller;

				try
				{
					Assert.Equal("SwiftUI text", controller.ButtonText);
					Assert.False(controller.ButtonEnabled);
					Assert.Equal("SwiftUI description", controller.SemanticsDescription);
					Assert.Equal("SwiftUI hint", controller.SemanticsHint);
					Assert.Equal("swiftui-button", controller.AutomationId);

					button.IsEnabled = true;
					handler.UpdateValue(nameof(IView.IsEnabled));
					Assert.True(controller.ButtonEnabled);

					controller.PerformPressGestureStateForDiagnostics(UIGestureRecognizerState.Began);
					Assert.True(controller.PressingForDiagnostics);
					controller.PerformClickForDiagnostics();
					Assert.False(controller.PressingForDiagnostics);
					controller.PerformPressGestureStateForDiagnostics(UIGestureRecognizerState.Ended);

					Assert.Equal(1, clicks);
					Assert.Equal(1, presses);
					Assert.Equal(1, releases);
					Assert.Equal(new[] { "pressed", "released", "clicked" }, eventOrder);

					controller.PerformPressGestureStateForDiagnostics(UIGestureRecognizerState.Began);
					Assert.True(controller.PressingForDiagnostics);
					controller.PerformPressGestureStateForDiagnostics(UIGestureRecognizerState.Cancelled);
					Assert.False(controller.PressingForDiagnostics);
					Assert.Equal(2, presses);
					Assert.Equal(2, releases);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}

				Assert.True(controller.DisconnectedForDiagnostics);
				Assert.False(controller.PressingForDiagnostics);
				controller.PerformClickForDiagnostics();
				controller.PerformPressedForDiagnostics();
				controller.PerformReleasedForDiagnostics();
				Assert.Equal(1, clicks);
				Assert.Equal(2, presses);
				Assert.Equal(2, releases);
			});
		}

		[Fact]
		public async Task DisconnectReleasesActivePress()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var presses = 0;
				var releases = 0;
				var button = new ButtonStub { IsEnabled = true, Text = "Disconnect pressed button" };
				button.Pressed += (_, _) => presses++;
				button.Released += (_, _) => releases++;

				var handler = new ExperimentalSwiftUIButtonHandler();
				handler.SetMauiContext(MauiContext);
				handler.SetVirtualView(button);
				var controller = handler.Controller;
				var disconnected = false;

				try
				{
					controller.PerformPressGestureStateForDiagnostics(UIGestureRecognizerState.Began);
					Assert.True(controller.PressingForDiagnostics);
					Assert.Equal(1, presses);
					Assert.Equal(0, releases);

					((IElementHandler)handler).DisconnectHandler();
					disconnected = true;

					Assert.True(controller.DisconnectedForDiagnostics);
					Assert.False(controller.PressingForDiagnostics);
					Assert.Equal(1, releases);

					controller.PerformPressGestureStateForDiagnostics(UIGestureRecognizerState.Began);
					Assert.Equal(1, presses);
					Assert.Equal(1, releases);
				}
				finally
				{
					if (!disconnected)
						((IElementHandler)handler).DisconnectHandler();
				}
			});
		}

		[Fact]
		public async Task ReconnectUsesCurrentVirtualView()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var firstClicks = 0;
				var secondClicks = 0;
				var firstButton = new ButtonStub { Text = "First" };
				var secondButton = new ButtonStub { Text = "Second" };
				firstButton.Clicked += (_, _) => firstClicks++;
				secondButton.Clicked += (_, _) => secondClicks++;

				var handler = new ExperimentalSwiftUIButtonHandler();
				handler.SetMauiContext(MauiContext);
				handler.SetVirtualView(firstButton);
				var controller = handler.Controller;

				try
				{
					controller.PerformClickForDiagnostics();
					handler.SetVirtualView(secondButton);
					controller.PerformClickForDiagnostics();

					Assert.Same(controller, handler.Controller);
					Assert.False(controller.DisconnectedForDiagnostics);
					Assert.Equal("Second", controller.ButtonText);
					Assert.Equal(1, firstClicks);
					Assert.Equal(1, secondClicks);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			});
		}

		[Fact]
		public async Task AttachesControllerAndReportsIntrinsicSize()
		{
			await InvokeOnMainThreadAsync(async () =>
			{
				var handler = new ExperimentalSwiftUIButtonHandler();
				handler.SetMauiContext(MauiContext);
				handler.SetVirtualView(new ButtonStub
				{
					AutomationId = "measured-swiftui-button",
					Text = "Measured SwiftUI button",
				});
				var controller = handler.Controller;

				try
				{
					await handler.PlatformView.AttachAndRun(async () =>
					{
						await AssertHelpers.AssertEventually(
							() => controller.ParentViewController is not null,
							message: "SwiftUI controller was not attached to a parent controller.");

						var size = controller.SizeThatFits(
							new CoreGraphics.CGSize(300, double.PositiveInfinity));
						Assert.True(size.Width > 0);
						Assert.True(size.Height > 0);

						var desiredSize = handler.GetDesiredSize(
							double.PositiveInfinity,
							double.PositiveInfinity);
						Assert.True(desiredSize.Width > 0);
						Assert.True(desiredSize.Height > 0);
					});

					Assert.Null(controller.ParentViewController);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			});
		}

		[Fact]
		public async Task ReparentsControllerWithinSameWindow()
		{
			await InvokeOnMainThreadAsync(async () =>
			{
				var handler = new ExperimentalSwiftUIButtonHandler();
				handler.SetMauiContext(MauiContext);
				handler.SetVirtualView(new ButtonStub { Text = "Reparented SwiftUI button" });
				var controller = handler.Controller;

				try
				{
					await handler.PlatformView.AttachAndRun(async () =>
					{
						var rootController = handler.PlatformView.Window?.RootViewController
							?? throw new InvalidOperationException("The attached view did not provide a root view controller.");
						using var firstController = new UIViewController();
						using var secondController = new UIViewController();

						rootController.AddChildViewController(firstController);
						rootController.AddChildViewController(secondController);
						rootController.View.AddSubview(firstController.View);
						rootController.View.AddSubview(secondController.View);
						firstController.DidMoveToParentViewController(rootController);
						secondController.DidMoveToParentViewController(rootController);

						try
						{
							firstController.View.AddSubview(handler.PlatformView);
							await AssertHelpers.AssertEventually(
								() => controller.ParentViewController == firstController,
								message: "SwiftUI controller did not attach to the first parent controller.");

							secondController.View.AddSubview(handler.PlatformView);
							await AssertHelpers.AssertEventually(
								() => controller.ParentViewController == secondController,
								message: "SwiftUI controller did not follow a same-window reparent.");
						}
						finally
						{
							handler.PlatformView.RemoveFromSuperview();
							RemoveChildController(firstController);
							RemoveChildController(secondController);
						}
					});
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}

				Assert.Null(controller.ParentViewController);
			});
		}

		static void RemoveChildController(UIViewController controller)
		{
			controller.WillMoveToParentViewController(null);
			controller.View.RemoveFromSuperview();
			controller.RemoveFromParentViewController();
		}
	}
}
