using System.Threading.Tasks;
using Microsoft.Maui.DeviceTests.Stubs;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Xunit;

namespace Microsoft.Maui.DeviceTests
{
	[Category(TestCategory.Button)]
	public class ExperimentalComposeButtonHandlerTests : CoreHandlerTestBase
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
					AutomationId = "compose-button",
					IsEnabled = false,
					Semantics = new Semantics
					{
						Description = "Compose description",
					},
					Text = "Compose text",
				};
				button.Clicked += (_, _) => clicks++;
				button.Pressed += (_, _) => presses++;
				button.Released += (_, _) => releases++;

				var handler = new ExperimentalComposeButtonHandler();
				handler.SetMauiContext(MauiContext);
				handler.SetVirtualView(button);
				var platformView = handler.PlatformView;

				try
				{
					Assert.Equal("Compose text", platformView.ButtonTextForDiagnostics);
					Assert.False(platformView.ButtonEnabledForDiagnostics);
					Assert.Equal(
						"Compose description",
						platformView.SemanticsDescriptionForDiagnostics);
					Assert.Equal("compose-button", platformView.AutomationIdForDiagnostics);

					platformView.PerformClickForDiagnostics();
					platformView.PerformPressedForDiagnostics();
					platformView.PerformReleasedForDiagnostics();

					Assert.Equal(1, clicks);
					Assert.Equal(1, presses);
					Assert.Equal(1, releases);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}

				Assert.True(platformView.DisconnectedForDiagnostics);
				platformView.PerformClickForDiagnostics();
				platformView.PerformPressedForDiagnostics();
				platformView.PerformReleasedForDiagnostics();
				Assert.Equal(1, clicks);
				Assert.Equal(1, presses);
				Assert.Equal(1, releases);
			});
		}

		[Fact]
		public async Task ReconnectCreatesCompositionAndUsesCurrentVirtualView()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var firstClicks = 0;
				var secondClicks = 0;
				var firstButton = new ButtonStub { Text = "First" };
				var secondButton = new ButtonStub { Text = "Second" };
				firstButton.Clicked += (_, _) => firstClicks++;
				secondButton.Clicked += (_, _) => secondClicks++;

				var handler = new ExperimentalComposeButtonHandler();
				handler.SetMauiContext(MauiContext);
				handler.SetVirtualView(firstButton);
				var platformView = handler.PlatformView;

				try
				{
					platformView.PerformClickForDiagnostics();
					handler.SetVirtualView(secondButton);
					platformView.PerformClickForDiagnostics();

					Assert.Same(platformView, handler.PlatformView);
					Assert.False(platformView.DisconnectedForDiagnostics);
					Assert.Equal("Second", platformView.ButtonTextForDiagnostics);
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
		public async Task ReportsNonZeroComposedContentSizeWhenAttached()
		{
			await InvokeOnMainThreadAsync(async () =>
			{
				var handler = new ExperimentalComposeButtonHandler();
				handler.SetMauiContext(MauiContext);
				handler.SetVirtualView(new ButtonStub { Text = "Measured Compose button" });
				var platformView = handler.PlatformView;

				try
				{
					// ComponentActivity.SetContentView normally initializes these owners, but the
					// headless test activity attaches test views without setting content.
					Assert.IsAssignableFrom<AndroidX.Activity.ComponentActivity>(
						platformView.Context.GetActivity()).InitializeViewTreeOwners();

					await platformView.AttachAndRun(async () =>
					{
						await AssertHelpers.AssertEventually(
							() =>
								platformView.MeasuredContentWidthForDiagnostics > 0 &&
								platformView.MeasuredContentHeightForDiagnostics > 0,
							message: "Compose content did not report a nonzero size.");

						var desiredSize = handler.GetDesiredSize(
							double.PositiveInfinity,
							double.PositiveInfinity);

						Assert.True(desiredSize.Width > 0);
						Assert.True(desiredSize.Height > 0);
					});
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			});
		}
	}
}
