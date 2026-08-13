using Microsoft.Maui.Media;

namespace Microsoft.Maui
{
	internal enum UnoScreenshotCaptureStrategy
	{
		RenderTargetBitmap,
		DirectWebView,
		Unsupported
	}

	internal static class UnoScreenshotCaptureSupport
	{
		const double BaseDpi = 96d;

		internal static UnoScreenshotCaptureStrategy ResolveStrategy(
			bool isWindowCapture,
			bool isDirectWebView,
			bool containsWebView,
			bool isWindows,
			bool hasCoreWebView)
		{
			if (isDirectWebView)
			{
				return isWindows && hasCoreWebView
					? UnoScreenshotCaptureStrategy.DirectWebView
					: UnoScreenshotCaptureStrategy.Unsupported;
			}

			if (isWindowCapture && containsWebView)
				return UnoScreenshotCaptureStrategy.Unsupported;

			return containsWebView
				? UnoScreenshotCaptureStrategy.Unsupported
				: UnoScreenshotCaptureStrategy.RenderTargetBitmap;
		}

		internal static double GetScaledDpi(double rasterizationScale) =>
			rasterizationScale > 0
				? BaseDpi * rasterizationScale
				: BaseDpi;

		internal static float NormalizeJpegQuality(int quality)
		{
			if (quality < 0)
				return 0f;

			if (quality > 100)
				return 1f;

			return quality / 100f;
		}

		internal static bool SupportsAlpha(ScreenshotFormat format) =>
			format == ScreenshotFormat.Png;
	}
}
