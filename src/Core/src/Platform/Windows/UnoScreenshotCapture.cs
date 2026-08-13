#if UNO
#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.Maui.Media;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Microsoft.Maui
{
	static class UnoScreenshotCapture
	{
		public static Task<IScreenshotResult?> CaptureAsync(object platformView)
		{
			var target = platformView switch
			{
				Window window => new CaptureTarget(window.Content as UIElement, CaptureTargetKind.Window),
				UIElement view => new CaptureTarget(view, CaptureTargetKind.View),
				_ => default,
			};
			if (target.Element is null)
				return Task.FromResult<IScreenshotResult?>(null);

			if (target.Element.DispatcherQueue.HasThreadAccess)
				return CaptureElementAsync(target);

			var completion = new TaskCompletionSource<IScreenshotResult?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			if (!target.Element.DispatcherQueue.TryEnqueue(() =>
			{
				_ = CompleteCaptureAsync(target, completion);
			}))
			{
				completion.SetException(new InvalidOperationException("Unable to dispatch Uno screenshot capture."));
			}

			return completion.Task;
		}

		static async Task CompleteCaptureAsync(
			CaptureTarget target,
			TaskCompletionSource<IScreenshotResult?> completion)
		{
			try
			{
				completion.TrySetResult(await CaptureElementAsync(target).ConfigureAwait(false));
			}
			catch (OperationCanceledException)
			{
				completion.TrySetCanceled();
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
			}
		}

		static async Task<IScreenshotResult?> CaptureElementAsync(CaptureTarget target)
		{
			var element = target.Element!;
			var webView = element as global::Microsoft.UI.Xaml.Controls.WebView2;
			var strategy = UnoScreenshotCaptureSupport.ResolveStrategy(
				target.Kind == CaptureTargetKind.Window,
				webView is not null,
				ContainsWebView(element),
				OperatingSystem.IsWindows(),
				webView?.CoreWebView2 is not null);

			if (strategy == UnoScreenshotCaptureStrategy.Unsupported)
				return null;

			if (strategy == UnoScreenshotCaptureStrategy.DirectWebView)
			{
				return await CaptureWebViewAsync(webView!.CoreWebView2!).ConfigureAwait(false);
			}

			var bitmap = new RenderTargetBitmap();
			await bitmap.RenderAsync(element);
			var dpi = UnoScreenshotCaptureSupport.GetScaledDpi(element.XamlRoot?.RasterizationScale ?? 0);

			var pixels = await bitmap.GetPixelsAsync().AsTask().ConfigureAwait(false);
			return new UnoScreenshotResult(bitmap.PixelWidth, bitmap.PixelHeight, pixels, dpi, dpi);
		}

		static async Task<IScreenshotResult> CaptureWebViewAsync(
			global::Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView)
		{
			const BitmapPixelFormat pixelFormat = BitmapPixelFormat.Bgra8;
			const BitmapAlphaMode alphaMode = BitmapAlphaMode.Premultiplied;

			using var stream = new InMemoryRandomAccessStream();
			await coreWebView.CapturePreviewAsync(
				global::Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,
				stream);
			stream.Seek(0);

			var decoder = await BitmapDecoder.CreateAsync(stream);
			var provider = await decoder.GetPixelDataAsync(
				pixelFormat,
				alphaMode,
				new BitmapTransform(),
				ExifOrientationMode.IgnoreExifOrientation,
				ColorManagementMode.DoNotColorManage);
			return new UnoScreenshotResult(
				(int)decoder.PixelWidth,
				(int)decoder.PixelHeight,
				provider.DetachPixelData(),
				decoder.DpiX,
				decoder.DpiY,
				pixelFormat,
				alphaMode);
		}

		static bool ContainsWebView(UIElement element) =>
			element is global::Microsoft.UI.Xaml.Controls.WebView2 ||
			element.GetFirstDescendant<global::Microsoft.UI.Xaml.Controls.WebView2>() is not null;

		readonly struct CaptureTarget
		{
			public CaptureTarget(UIElement? element, CaptureTargetKind kind)
			{
				Element = element;
				Kind = kind;
			}

			public UIElement? Element { get; }

			public CaptureTargetKind Kind { get; }
		}

		enum CaptureTargetKind
		{
			View,
			Window
		}

		sealed class UnoScreenshotResult : IScreenshotResult
		{
			readonly byte[] _bytes;
			readonly double _dpiX;
			readonly double _dpiY;
			readonly BitmapAlphaMode _alphaMode;
			readonly BitmapPixelFormat _pixelFormat;

			public UnoScreenshotResult(int width, int height, IBuffer pixels, double dpiX, double dpiY)
				: this(width, height, pixels.ToArray() ?? throw new ArgumentNullException(nameof(pixels)), dpiX, dpiY)
			{
			}

			public UnoScreenshotResult(
				int width,
				int height,
				byte[] bytes,
				double dpiX,
				double dpiY)
				: this(
					width,
					height,
					bytes,
					dpiX,
					dpiY,
					BitmapPixelFormat.Bgra8,
					BitmapAlphaMode.Premultiplied)
			{
			}

			public UnoScreenshotResult(
				int width,
				int height,
				byte[] bytes,
				double dpiX,
				double dpiY,
				BitmapPixelFormat pixelFormat,
				BitmapAlphaMode alphaMode)
			{
				Width = width;
				Height = height;
				_bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
				_dpiX = dpiX;
				_dpiY = dpiY;
				_pixelFormat = pixelFormat;
				_alphaMode = alphaMode;
			}

			public int Width { get; }

			public int Height { get; }

			public async Task<Stream> OpenReadAsync(
				ScreenshotFormat format = ScreenshotFormat.Png,
				int quality = 100)
			{
				var stream = new InMemoryRandomAccessStream();
				await EncodeAsync(format, quality, stream).ConfigureAwait(false);
				stream.Seek(0);
				return stream.AsStreamForRead();
			}

			public Task CopyToAsync(
				Stream destination,
				ScreenshotFormat format = ScreenshotFormat.Png,
				int quality = 100) =>
				EncodeAsync(format, quality, destination.AsRandomAccessStream());

			async Task EncodeAsync(ScreenshotFormat format, int quality, IRandomAccessStream stream)
			{
				var encoder = await CreateEncoderAsync(format, quality, stream).ConfigureAwait(false);
				encoder.SetPixelData(
					_pixelFormat,
					GetEncoderAlphaMode(format),
					(uint)Width,
					(uint)Height,
					_dpiX,
					_dpiY,
					_bytes);
				await encoder.FlushAsync().AsTask().ConfigureAwait(false);
			}

			static async Task<BitmapEncoder> CreateEncoderAsync(
				ScreenshotFormat format,
				int quality,
				IRandomAccessStream stream)
			{
				var encoderId = ToBitmapEncoder(format);
				var encoderProperties = CreateEncoderProperties(format, quality);
				if (encoderProperties is not null)
				{
					try
					{
						return await BitmapEncoder.CreateAsync(encoderId, stream, encoderProperties)
							.AsTask()
							.ConfigureAwait(false);
					}
					catch (Exception)
					{
					}
				}

				return await BitmapEncoder.CreateAsync(encoderId, stream)
					.AsTask()
					.ConfigureAwait(false);
			}

			static BitmapPropertySet? CreateEncoderProperties(ScreenshotFormat format, int quality)
			{
				if (format != ScreenshotFormat.Jpeg)
					return null;

				var encoderProperties = new BitmapPropertySet();
				encoderProperties.Add(
					"ImageQuality",
					new BitmapTypedValue(
						UnoScreenshotCaptureSupport.NormalizeJpegQuality(quality),
						PropertyType.Single));
				return encoderProperties;
			}

			BitmapAlphaMode GetEncoderAlphaMode(ScreenshotFormat format) =>
				UnoScreenshotCaptureSupport.SupportsAlpha(format)
					? _alphaMode
					: BitmapAlphaMode.Ignore;

			static Guid ToBitmapEncoder(ScreenshotFormat format) =>
				format switch
				{
					ScreenshotFormat.Jpeg => BitmapEncoder.JpegEncoderId,
					ScreenshotFormat.Png => BitmapEncoder.PngEncoderId,
					_ => throw new ArgumentOutOfRangeException(nameof(format)),
				};
		}
	}
}
#endif
