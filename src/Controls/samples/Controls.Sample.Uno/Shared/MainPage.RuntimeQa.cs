#if MAUI_UNO_RUNTIME_QA
using System.IO;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Storage;
using SkiaSharp;
using NativeBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using NativeTextBlock = Microsoft.UI.Xaml.Controls.TextBlock;

namespace Microsoft.Maui.Controls.Sample.Uno;

public sealed partial class MainPage
{
	async Task<string> RunRuntimeQaAsync(Entry entry)
	{
		var results = new List<string>();
		var application = Application.Current ?? throw new InvalidOperationException("The MAUI application is unavailable.");
		var originalTheme = application.UserAppTheme;
		var originalFlow = FlowDirection;
		var reportPath = Path.Combine(FileSystem.CacheDirectory, "uno-maui-runtime-qa.txt");
		await File.WriteAllTextAsync(reportPath, $"RUNNING: {DateTimeOffset.UtcNow:O}{Environment.NewLine}");

		void Check(bool passed, string name) => results.Add($"{(passed ? "PASS" : "FAIL")}: {name}");

		async Task SettleAsync()
		{
			await Task.Delay(150);
			await Dispatcher.DispatchAsync(() => { });
		}

		try
		{
			await SettleAsync();
			await Task.Delay(1500);
			await Dispatcher.DispatchAsync(() =>
			{
				results.Add(GetRuntimeDiagnostics(this));
				var fontVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(
					(Microsoft.UI.Xaml.UIElement)_fontImage.Handler!.PlatformView!) as Microsoft.UI.Composition.ContainerVisual;
				Check(fontVisual is not null && fontVisual.Children.Any(child => child.Size.X > 0 && child.Size.Y > 0),
					"Font visual arranged before any forced layout or capture");
				var nativeImage = (Microsoft.UI.Xaml.Controls.Image)_fontImage.Handler.PlatformView!;
				var bitmap = nativeImage.Source as Microsoft.UI.Xaml.Media.Imaging.BitmapSource;
				var imageRoot = nativeImage.XamlRoot ?? throw new InvalidOperationException("The font image is not attached to a XamlRoot.");
				var expectedWidth = Math.Min(_fontImage.WidthRequest, (bitmap?.PixelWidth ?? 0) / imageRoot.RasterizationScale);
				Check(expectedWidth > 0 && Math.Abs(nativeImage.ActualWidth - expectedWidth) < 1,
					$"Font image uses current display density ({nativeImage.ActualWidth:0.##} vs {expectedWidth:0.##})");
				entry.Text = "Renderer QA 123";
				((IButton)_commandButton).Clicked();
				Check(_commandStatus.Text.Contains("Entry: Renderer QA 123", StringComparison.Ordinal), "Entry-to-command binding");
			});

			var themeIndex = 0;
			foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark, AppTheme.Light })
			{
				await Dispatcher.DispatchAsync(() => application.UserAppTheme = theme);
				await SettleAsync();
				await Dispatcher.DispatchAsync(() =>
				{
					var expectedBackground = theme == AppTheme.Dark ? SampleTheme.DarkBackground : SampleTheme.LightBackground;
					var expectedForeground = theme == AppTheme.Dark ? SampleTheme.DarkForeground : SampleTheme.LightForeground;
					var nativeButton = _commandButton.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.Button;
					var nativeText = nativeButton?.GetContent<NativeTextBlock>();
					Check(BackgroundColor == expectedBackground, $"{theme} page background");
					Check(_commandButton.TextColor == expectedForeground, $"{theme} MAUI button color");
					Check(nativeText?.Foreground is NativeBrush brush && brush.Color == expectedForeground.ToWindowsColor(),
						$"{theme} rendered button caption");
					results.Add($"Button colors: control={nativeButton?.Foreground}, caption={nativeText?.Foreground}; actual={nativeText?.ActualTheme}");
				});
				var themeCapture = await this.CaptureAsync();
				if (themeCapture is not null)
				{
					using var imageStream = await themeCapture.OpenReadAsync(Microsoft.Maui.Media.ScreenshotFormat.Png);
					using var imageFile = File.Create(Path.Combine(FileSystem.CacheDirectory, $"uno-maui-theme-{themeIndex}-{theme}.png"));
					await imageStream.CopyToAsync(imageFile);
				}
				themeIndex++;
			}

			foreach (var flow in new[] { FlowDirection.RightToLeft, FlowDirection.LeftToRight })
			{
				await Dispatcher.DispatchAsync(() => FlowDirection = flow);
				await SettleAsync();
				await Dispatcher.DispatchAsync(() =>
				{
					var expected = flow == FlowDirection.RightToLeft
						? Microsoft.UI.Xaml.TextAlignment.Right
						: Microsoft.UI.Xaml.TextAlignment.Left;
					Check((_commandStatus.Handler?.PlatformView as NativeTextBlock)?.TextAlignment == expected, $"{flow} label alignment");
					Check((entry.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.TextBox)?.TextAlignment == expected, $"{flow} entry alignment");
				});
			}

			var originalBorderColor = _commandButton.BorderColor;
			var originalBorderWidth = _commandButton.BorderWidth;
			var originalCornerRadius = _commandButton.CornerRadius;
			try
			{
				await Dispatcher.DispatchAsync(() =>
				{
					entry.TextColor = Colors.Magenta;
					entry.BackgroundColor = Colors.LightYellow;
					_commandButton.BackgroundColor = Colors.DarkGreen;
					_commandButton.BorderColor = Colors.Orange;
					_commandButton.BorderWidth = 3;
					_commandButton.CornerRadius = 11;
				});
				await SettleAsync();
				await Dispatcher.DispatchAsync(() =>
				{
					var nativeEntry = (Microsoft.UI.Xaml.Controls.TextBox)entry.Handler!.PlatformView!;
					var nativeButton = (Microsoft.UI.Xaml.Controls.Button)_commandButton.Handler!.PlatformView!;
					Check(nativeEntry.Foreground is NativeBrush foreground && foreground.Color == Colors.Magenta.ToWindowsColor(),
						"Post-load entry foreground without changing theme");
					Check(nativeEntry.Background is NativeBrush entryBackground && entryBackground.Color == Colors.LightYellow.ToWindowsColor(),
						"Post-load entry background without changing theme");
					Check(nativeButton.Background is NativeBrush background && background.Color == Colors.DarkGreen.ToWindowsColor(),
						"Post-load button background without changing theme");
					Check(nativeButton.BorderBrush is NativeBrush border && border.Color == Colors.Orange.ToWindowsColor(),
						"Post-load button border color without changing theme");
					Check(nativeButton.BorderThickness == new Microsoft.UI.Xaml.Thickness(3),
						"Post-load button border thickness without changing theme");
					Check(nativeButton.CornerRadius == new Microsoft.UI.Xaml.CornerRadius(11),
						"Post-load button corner radius without changing theme");
				});
				await Dispatcher.DispatchAsync(() =>
				{
					entry.ClearValue(Entry.TextColorProperty);
					entry.ClearValue(VisualElement.BackgroundColorProperty);
					_commandButton.ClearValue(Button.CornerRadiusProperty);
				});
				await SettleAsync();
				await Dispatcher.DispatchAsync(() =>
				{
					var nativeEntry = (Microsoft.UI.Xaml.Controls.TextBox)entry.Handler!.PlatformView!;
					var nativeButton = (Microsoft.UI.Xaml.Controls.Button)_commandButton.Handler!.PlatformView!;
					Check(entry.TextColor is not null && nativeEntry.Foreground is NativeBrush foreground && foreground.Color == entry.TextColor.ToWindowsColor(),
						"Clearing manual entry text color restores its theme binding");
					Check(entry.BackgroundColor is not null && nativeEntry.Background is NativeBrush background && background.Color == entry.BackgroundColor.ToWindowsColor(),
						"Clearing manual entry background restores its theme binding");
					Check(nativeButton.ReadLocalValue(Microsoft.UI.Xaml.Controls.Button.CornerRadiusProperty) == Microsoft.UI.Xaml.DependencyProperty.UnsetValue,
						"Clearing button corner radius restores its default");
				});
				await Dispatcher.DispatchAsync(() =>
				{
					entry.SetValue(Entry.TextColorProperty, null);
					entry.SetValue(VisualElement.BackgroundColorProperty, null);
				});
				await SettleAsync();
				await Dispatcher.DispatchAsync(() =>
				{
					var nativeEntry = (Microsoft.UI.Xaml.Controls.TextBox)entry.Handler!.PlatformView!;
					Check(entry.TextColor is null && nativeEntry.ReadLocalValue(Microsoft.UI.Xaml.Controls.TextBox.ForegroundProperty) == Microsoft.UI.Xaml.DependencyProperty.UnsetValue,
						"Null entry text color clears its native override");
					Check(entry.BackgroundColor is null && nativeEntry.ReadLocalValue(Microsoft.UI.Xaml.Controls.TextBox.BackgroundProperty) == Microsoft.UI.Xaml.DependencyProperty.UnsetValue,
						"Null entry background clears its native override");
				});
			}
			finally
			{
				await Dispatcher.DispatchAsync(() =>
				{
					_commandButton.BorderColor = originalBorderColor;
					_commandButton.BorderWidth = originalBorderWidth;
					_commandButton.CornerRadius = originalCornerRadius;
					entry.ClearValue(Entry.TextColorProperty);
					entry.ClearValue(VisualElement.BackgroundColorProperty);
					_commandButton.ClearValue(VisualElement.BackgroundColorProperty);
					SampleTheme.ApplyTo(this);
				});
			}

			var screenshot = await _fontImage.CaptureAsync();
			var visiblePixels = 0;
			if (screenshot is not null)
			{
				using var stream = await screenshot.OpenReadAsync(Microsoft.Maui.Media.ScreenshotFormat.Png);
				using var bitmap = SKBitmap.Decode(stream);
				if (bitmap is not null)
				{
					foreach (var pixel in bitmap.Pixels)
					{
						if (pixel.Alpha > 0 && pixel.Blue > pixel.Red + 25)
						{
							visiblePixels++;
						}
					}
				}
			}
			Check(visiblePixels > 0, $"Rendered font glyph ({visiblePixels} blue pixels)");

			var essentials = await RunEssentialsProbeAsync();
			results.Add(essentials);
			Check(!essentials.Contains(": failed", StringComparison.OrdinalIgnoreCase) &&
				!essentials.Contains("unexpected", StringComparison.OrdinalIgnoreCase), "Essentials compatibility probes");
			var screenshotResult = await RunScreenshotProbeAsync(this);
			Check(screenshotResult.Contains("valid PNG stream", StringComparison.Ordinal) &&
				!screenshotResult.Contains("invalid PNG", StringComparison.Ordinal), $"Page screenshot ({screenshotResult})");
			results.Add(await Dispatcher.DispatchAsync(() => GetRuntimeDiagnostics(this)));
			results.Add("COMPLETE");
		}
		catch (Exception error)
		{
			results.Add($"FAIL: runtime QA aborted: {error}");
			throw;
		}
		finally
		{
			await Dispatcher.DispatchAsync(() =>
			{
				application.UserAppTheme = originalTheme;
				FlowDirection = originalFlow;
			});
			await File.WriteAllLinesAsync(reportPath, results);
		}

		var failures = results.Count(line => line.StartsWith("FAIL:", StringComparison.Ordinal));
		return $"Runtime QA: {failures} failures. Report: {reportPath}";
	}
}
#endif
