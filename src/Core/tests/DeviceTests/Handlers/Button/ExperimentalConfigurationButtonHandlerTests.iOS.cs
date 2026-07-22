using System;
using System.Threading.Tasks;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.DeviceTests.Stubs;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using UIKit;
using Xunit;

namespace Microsoft.Maui.DeviceTests
{
	[Category(TestCategory.Button)]
	public class ExperimentalConfigurationButtonHandlerTests : CoreHandlerTestBase
	{
		const string NativeViewPropertyUpdateBatchingSwitch =
			"Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled";

		[Fact]
		public async Task MapsCoherentConfigurationAndRoutesClick()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var clicks = 0;
				var button = CreateConfiguredButton();
				button.Clicked += (_, _) => clicks++;
				var handler = CreateHandler(button);

				try
				{
					var configuration = Assert.IsType<UIButtonConfiguration>(
						handler.PlatformView.Configuration);
					var attributedTitle = Assert.IsType<NSAttributedString>(
						configuration.AttributedTitle);
					var nativeFont = Assert.IsType<UIFont>(
						attributedTitle.GetAttribute(
							UIStringAttributeKey.Font,
							0,
							out _));

					Assert.Equal("Configured button", attributedTitle.Value);
					Assert.Equal(3, attributedTitle.GetCharacterSpacing());
					Assert.Equal(19, (double)nativeFont.PointSize);
					Assert.Equal(Colors.Orange, configuration.BaseForegroundColor.ToColor());
					Assert.Equal(
						Colors.Navy,
						configuration.Background.BackgroundColor.ToColor());
					Assert.Equal(13, (double)configuration.ContentInsets.Top);
					Assert.Equal(23, (double)configuration.ContentInsets.Leading);
					Assert.Equal(33, (double)configuration.ContentInsets.Bottom);
					Assert.Equal(43, (double)configuration.ContentInsets.Trailing);
					Assert.Equal(Colors.Lime, configuration.Background.StrokeColor.ToColor());
					Assert.Equal(3, (double)configuration.Background.StrokeWidth);
					Assert.Equal(9, (double)configuration.Background.CornerRadius);
					Assert.Equal(1, handler.ConfigurationApplyCount);

					handler.PlatformView.SendActionForControlEvents(
						UIControlEvent.TouchUpInside);

					Assert.Equal(1, clicks);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			});
		}

		[Fact]
		public async Task PreservesNativeDefaultsWhenValuesAreUnset()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var button = new ButtonStub
				{
					CornerRadius = -1,
					Font = Font.Default,
					Padding = new Thickness(double.NaN),
					StrokeThickness = -1,
					Text = "Default button",
				};
				var handler = CreateHandler(button);

				try
				{
					var configuration = handler.PlatformView.Configuration;
					var desiredSize = handler.GetDesiredSize(
						double.PositiveInfinity,
						double.PositiveInfinity);

					Assert.Null(configuration.BaseForegroundColor);
					Assert.Null(configuration.BaseBackgroundColor);
					Assert.NotNull(handler.PlatformView.CurrentTitleColor);
					Assert.Equal(7, (double)configuration.ContentInsets.Top);
					Assert.Equal(12, (double)configuration.ContentInsets.Leading);
					Assert.Equal(7, (double)configuration.ContentInsets.Bottom);
					Assert.Equal(12, (double)configuration.ContentInsets.Trailing);
					Assert.True(desiredSize.Width > 0);
					Assert.True(desiredSize.Height > 0);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			});
		}

		[Fact]
		public async Task PropertyUpdatesRemainSynchronousOutsideExplicitBatch()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var button = CreateConfiguredButton();
				var handler = CreateHandler(button);

				try
				{
					handler.ResetConfigurationDiagnostics();
					button.Text = "Updated immediately";
					handler.UpdateValue(nameof(IText.Text));

					Assert.Equal(
						"Updated immediately",
						handler.PlatformView.Configuration.AttributedTitle.Value);
					Assert.Equal(1, handler.ConfigurationApplyCount);
					Assert.Equal(0, handler.ConfigurationBatchFlushCount);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			});
		}

		[Fact]
		public async Task ImageSourceUpdatesConfiguration()
		{
			await InvokeOnMainThreadAsync(async () =>
			{
				var button = CreateConfiguredButton();
				button.ImageSource = new FileImageSourceStub("black.png");
				var handler = CreateHandler(button);

				try
				{
					await AssertHelpers.AssertEventually(
						() => handler.PlatformView.Configuration?.Image is not null,
						message: "The configuration image did not load.");
					Assert.Equal(2, handler.ConfigurationApplyCount);

					button.ImageSource = null;
					handler.UpdateValue(nameof(IImage.Source));

					Assert.Null(handler.PlatformView.Configuration.Image);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			});
		}

		[Fact]
		public async Task ClearingImageSourceCancelsPendingLoad()
		{
			await InvokeOnMainThreadAsync(async () =>
			{
				var button = CreateConfiguredButton();
				var handler = CreateHandler(button);
				var provider = handler.Services.GetRequiredService<IImageSourceServiceProvider>();
				var imageService =
					provider.GetRequiredImageSourceService<CountedImageSourceStub>();
				var countedService = Assert.IsType<CountedImageSourceServiceStub>(imageService);

				try
				{
					handler.ResetConfigurationDiagnostics();
					button.ImageSource = new CountedImageSourceStub(Colors.Blue, wait: true);
					handler.UpdateValue(nameof(IImage.Source));

					var pendingToken = handler.ImageSourceLoader.SourceManager.Token;
					Assert.True(handler.ImageSourceLoader.SourceManager.IsLoading);
					Assert.False(pendingToken.IsCancellationRequested);

					button.ImageSource = null;
					handler.UpdateValue(nameof(IImage.Source));

					Assert.True(pendingToken.IsCancellationRequested);
					Assert.False(handler.ImageSourceLoader.SourceManager.IsLoading);
					Assert.Null(handler.PlatformView.Configuration.Image);
					Assert.Equal(1, handler.ConfigurationApplyCount);

					countedService.DoWork.Set();
					var completed = await Task.Run(
						() => countedService.Finishing.WaitOne(TimeSpan.FromSeconds(5)));

					Assert.True(completed);
					Assert.Null(handler.PlatformView.Configuration.Image);
					Assert.Equal(1, handler.ConfigurationApplyCount);
				}
				finally
				{
					countedService.DoWork.Set();
					((IElementHandler)handler).DisconnectHandler();
				}
			});
		}

		[Fact]
		public async Task ExplicitBatchCoalescesConfigurationUpdates()
		{
			AppContext.TryGetSwitch(
				NativeViewPropertyUpdateBatchingSwitch,
				out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, true);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var button = CreateConfiguredButton();
					var handler = CreateHandler(button);

					try
					{
						handler.ResetConfigurationDiagnostics();
						handler.Invoke(
							ViewHandler.BeginNativePropertyUpdateBatchCommand,
							null);

						button.Text = "Batched text";
						handler.UpdateValue(nameof(IText.Text));
						button.Padding = new Thickness(1, 2, 3, 4);
						handler.UpdateValue(nameof(IButton.Padding));
						button.StrokeThickness = 5;
						handler.UpdateValue(nameof(IButtonStroke.StrokeThickness));

						Assert.Equal(
							"Configured button",
							handler.PlatformView.Configuration.AttributedTitle.Value);
						Assert.Equal(0, handler.ConfigurationApplyCount);

						handler.Invoke(
							ViewHandler.CommitNativePropertyUpdateBatchCommand,
							null);

						var configuration = handler.PlatformView.Configuration;
						Assert.Equal(
							"Batched text",
							configuration.AttributedTitle.Value);
						Assert.Equal(7, (double)configuration.ContentInsets.Top);
						Assert.Equal(6, (double)configuration.ContentInsets.Leading);
						Assert.Equal(9, (double)configuration.ContentInsets.Bottom);
						Assert.Equal(8, (double)configuration.ContentInsets.Trailing);
						Assert.Equal(1, handler.ConfigurationApplyCount);
						Assert.Equal(1, handler.ConfigurationBatchFlushCount);
					}
					finally
					{
						((IElementHandler)handler).DisconnectHandler();
					}
				});
			}
			finally
			{
				AppContext.SetSwitch(
					NativeViewPropertyUpdateBatchingSwitch,
					originalSwitchValue);
			}
		}

		[Fact]
		public async Task ExplicitBatchRemainsSynchronousWhenSwitchIsDisabled()
		{
			AppContext.TryGetSwitch(
				NativeViewPropertyUpdateBatchingSwitch,
				out bool originalSwitchValue);
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, false);

			try
			{
				await InvokeOnMainThreadAsync(() =>
				{
					var button = CreateConfiguredButton();
					var handler = CreateHandler(button);

					try
					{
						handler.ResetConfigurationDiagnostics();
						handler.Invoke(
							ViewHandler.BeginNativePropertyUpdateBatchCommand,
							null);
						button.Text = "Updated synchronously";
						handler.UpdateValue(nameof(IText.Text));

						Assert.Equal(
							"Updated synchronously",
							handler.PlatformView.Configuration.AttributedTitle.Value);

						handler.Invoke(
							ViewHandler.CommitNativePropertyUpdateBatchCommand,
							null);

						Assert.Equal(1, handler.ConfigurationApplyCount);
						Assert.Equal(0, handler.ConfigurationBatchFlushCount);
					}
					finally
					{
						((IElementHandler)handler).DisconnectHandler();
					}
				});
			}
			finally
			{
				AppContext.SetSwitch(
					NativeViewPropertyUpdateBatchingSwitch,
					originalSwitchValue);
			}
		}

		[Fact]
		public async Task MapperOverrideObservesInitialConfigurationSnapshot()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var button = CreateConfiguredButton();
				var mapper = new PropertyMapper<
					ButtonStub,
					ExperimentalConfigurationButtonHandler>();
				var observedConfiguredState = false;

				mapper[nameof(IText.Text)] = (handler, _) =>
				{
					observedConfiguredState =
						handler.PlatformView.Configuration?.AttributedTitle?.Value ==
						"Configured button";
				};
				button.PropertyMapperOverrides = mapper;

				var handler = CreateHandler(button);

				try
				{
					Assert.True(observedConfiguredState);
					Assert.Equal(1, handler.ConfigurationApplyCount);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			});
		}

		[Fact]
		public async Task ReconnectAppliesNewVirtualViewSnapshot()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var firstClicks = 0;
				var secondClicks = 0;
				var firstButton = CreateConfiguredButton();
				var secondButton = CreateConfiguredButton();
				secondButton.Text = "Second button";
				secondButton.TextColor = Colors.Purple;
				firstButton.Clicked += (_, _) => firstClicks++;
				secondButton.Clicked += (_, _) => secondClicks++;
				var handler = CreateHandler(firstButton);

				try
				{
					handler.PlatformView.SendActionForControlEvents(
						UIControlEvent.TouchUpInside);
					handler.ResetConfigurationDiagnostics();
					handler.SetVirtualView(secondButton);
					handler.PlatformView.SendActionForControlEvents(
						UIControlEvent.TouchUpInside);

					Assert.Equal(
						"Second button",
						handler.PlatformView.Configuration.AttributedTitle.Value);
					Assert.Equal(
						Colors.Purple,
						handler.PlatformView.Configuration.BaseForegroundColor.ToColor());
					Assert.Equal(1, handler.ConfigurationApplyCount);
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
		public async Task RightToLeftPreservesPhysicalPadding()
		{
			await InvokeOnMainThreadAsync(() =>
			{
				var button = CreateConfiguredButton();
				button.FlowDirection = FlowDirection.RightToLeft;
				button.Padding = new Thickness(11, 2, 23, 4);
				button.StrokeThickness = 0;
				var handler = CreateHandler(button);

				try
				{
					var insets = handler.PlatformView.Configuration.ContentInsets;

					Assert.Equal(23, (double)insets.Leading);
					Assert.Equal(11, (double)insets.Trailing);
					Assert.Equal(
						UISemanticContentAttribute.ForceRightToLeft,
						handler.PlatformView.SemanticContentAttribute);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			});
		}

		ExperimentalConfigurationButtonHandler CreateHandler(ButtonStub button)
		{
			var handler = new ExperimentalConfigurationButtonHandler();
			handler.SetMauiContext(MauiContext);
			handler.SetVirtualView(button);
			return handler;
		}

		static ButtonStub CreateConfiguredButton() =>
			new()
			{
				Background = new SolidPaint(Colors.Navy),
				CharacterSpacing = 3,
				CornerRadius = 9,
				Font = Font.SystemFontOfSize(19),
				Padding = new Thickness(20, 10, 40, 30),
				StrokeColor = Colors.Lime,
				StrokeThickness = 3,
				Text = "Configured button",
				TextColor = Colors.Orange,
			};
	}
}
