using System.Threading.Tasks;
using Microsoft.Maui.DeviceTests.Stubs;
using Microsoft.Maui.Handlers;
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
				button.Clicked += (_, _) => clicks++;
				button.Pressed += (_, _) => presses++;
				button.Released += (_, _) => releases++;

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

					controller.PerformClickForDiagnostics();
					controller.PerformPressedForDiagnostics();
					controller.PerformReleasedForDiagnostics();

					Assert.Equal(1, clicks);
					Assert.Equal(1, presses);
					Assert.Equal(1, releases);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}

				Assert.True(controller.DisconnectedForDiagnostics);
				controller.PerformClickForDiagnostics();
				controller.PerformPressedForDiagnostics();
				controller.PerformReleasedForDiagnostics();
				Assert.Equal(1, clicks);
				Assert.Equal(1, presses);
				Assert.Equal(1, releases);
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
				handler.SetVirtualView(new ButtonStub { Text = "Measured SwiftUI button" });
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
	}
}
