using Microsoft.Maui.Media;
using Xunit;

namespace Microsoft.Maui.UnitTests.Platform
{
	[Category(TestCategory.Core)]
	public class UnoScreenshotCaptureSupportTests
	{
		[Fact]
		public void ResolveStrategy_UsesDirectWebViewCapture_ForSupportedViewCapture()
		{
			var strategy = UnoScreenshotCaptureSupport.ResolveStrategy(
				isWindowCapture: false,
				isDirectWebView: true,
				containsWebView: true,
				isWindows: true,
				hasCoreWebView: true);

			Assert.Equal(UnoScreenshotCaptureStrategy.DirectWebView, strategy);
		}

		[Theory]
		[InlineData(false, false)]
		[InlineData(true, true)]
		public void ResolveStrategy_UsesRenderTargetBitmap_ForNonDirectCaptureWithoutWebView(
			bool isWindows,
			bool hasCoreWebView)
		{
			var strategy = UnoScreenshotCaptureSupport.ResolveStrategy(
				isWindowCapture: false,
				isDirectWebView: false,
				containsWebView: false,
				isWindows: isWindows,
				hasCoreWebView: hasCoreWebView);

			Assert.Equal(UnoScreenshotCaptureStrategy.RenderTargetBitmap, strategy);
		}

		[Theory]
		[InlineData(false, false)]
		[InlineData(false, true)]
		[InlineData(true, false)]
		[InlineData(true, true)]
		public void ResolveStrategy_ReturnsUnsupported_ForNonDirectCaptureContainingWebView(
			bool isWindows,
			bool hasCoreWebView)
		{
			var strategy = UnoScreenshotCaptureSupport.ResolveStrategy(
				isWindowCapture: false,
				isDirectWebView: false,
				containsWebView: true,
				isWindows: isWindows,
				hasCoreWebView: hasCoreWebView);

			Assert.Equal(UnoScreenshotCaptureStrategy.Unsupported, strategy);
		}

		[Theory]
		[InlineData(false, true)]
		[InlineData(true, false)]
		public void ResolveStrategy_ReturnsUnsupported_ForDirectWebViewWithoutSupportedPlatform(
			bool isWindows,
			bool hasCoreWebView)
		{
			var strategy = UnoScreenshotCaptureSupport.ResolveStrategy(
				isWindowCapture: false,
				isDirectWebView: true,
				containsWebView: true,
				isWindows: isWindows,
				hasCoreWebView: hasCoreWebView);

			Assert.Equal(UnoScreenshotCaptureStrategy.Unsupported, strategy);
		}

		[Fact]
		public void ResolveStrategy_ReturnsUnsupported_ForWindowCaptureContainingWebView()
		{
			var strategy = UnoScreenshotCaptureSupport.ResolveStrategy(
				isWindowCapture: true,
				isDirectWebView: false,
				containsWebView: true,
				isWindows: true,
				hasCoreWebView: false);

			Assert.Equal(UnoScreenshotCaptureStrategy.Unsupported, strategy);
		}

		[Theory]
		[InlineData(-1, 96)]
		[InlineData(0, 96)]
		[InlineData(1, 96)]
		[InlineData(1.5, 144)]
		[InlineData(2, 192)]
		public void GetScaledDpi_UsesRasterizationScale(double rasterizationScale, double expectedDpi)
		{
			Assert.Equal(expectedDpi, UnoScreenshotCaptureSupport.GetScaledDpi(rasterizationScale));
		}

		[Theory]
		[InlineData(-20, 0f)]
		[InlineData(0, 0f)]
		[InlineData(1, 0.01f)]
		[InlineData(80, 0.8f)]
		[InlineData(100, 1f)]
		[InlineData(250, 1f)]
		public void NormalizeJpegQuality_ClampsToSupportedRange(int quality, float expected)
		{
			Assert.Equal(expected, UnoScreenshotCaptureSupport.NormalizeJpegQuality(quality));
		}

		[Fact]
		public void SupportsAlpha_IsEnabledOnlyForPng()
		{
			Assert.True(UnoScreenshotCaptureSupport.SupportsAlpha(ScreenshotFormat.Png));
			Assert.False(UnoScreenshotCaptureSupport.SupportsAlpha(ScreenshotFormat.Jpeg));
		}
	}
}
