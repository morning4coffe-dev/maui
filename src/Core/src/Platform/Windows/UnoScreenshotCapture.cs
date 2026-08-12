#if UNO
#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.Maui.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Microsoft.Maui
{
	static class UnoScreenshotCapture
	{
		public static Task<IScreenshotResult?> CaptureAsync(object platformView)
		{
			var element = platformView switch
			{
				Window window => window.Content as UIElement,
				UIElement view => view,
				_ => null,
			};
			if (element is null)
				return Task.FromResult<IScreenshotResult?>(null);

			if (element.DispatcherQueue.HasThreadAccess)
				return CaptureElementAsync(element);

			var completion = new TaskCompletionSource<IScreenshotResult?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			if (!element.DispatcherQueue.TryEnqueue(() =>
			{
				_ = CaptureElementAsync(element).ContinueWith(
					task =>
					{
						if (task.IsCanceled)
							completion.TrySetCanceled();
						else if (task.IsFaulted)
							completion.TrySetException(task.Exception.InnerExceptions);
						else
							completion.TrySetResult(task.Result);
					},
					TaskScheduler.Default);
			}))
			{
				completion.SetException(new InvalidOperationException("Unable to dispatch Uno screenshot capture."));
			}

			return completion.Task;
		}

		static async Task<IScreenshotResult?> CaptureElementAsync(UIElement element)
		{
			if (OperatingSystem.IsWindows() &&
				element is global::Microsoft.UI.Xaml.Controls.WebView2 { CoreWebView2: { } coreWebView })
			{
				using var stream = new InMemoryRandomAccessStream();
				await coreWebView.CapturePreviewAsync(
					global::Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,
					stream);

				var decoder = await BitmapDecoder.CreateAsync(stream);
				var provider = await decoder.GetPixelDataAsync();
				return new UnoScreenshotResult(
					(int)decoder.PixelWidth,
					(int)decoder.PixelHeight,
					provider.DetachPixelData(),
					decoder.DpiX,
					decoder.DpiY);
			}

			var bitmap = new RenderTargetBitmap();
			await bitmap.RenderAsync(element);

			var pixels = await bitmap.GetPixelsAsync().AsTask().ConfigureAwait(false);
			return new UnoScreenshotResult(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
		}

		sealed class UnoScreenshotResult : IScreenshotResult
		{
			readonly byte[] _bytes;
			readonly double _dpiX;
			readonly double _dpiY;

			public UnoScreenshotResult(int width, int height, IBuffer pixels)
			{
				Width = width;
				Height = height;
				_bytes = pixels.ToArray() ?? throw new ArgumentNullException(nameof(pixels));
				_dpiX = 96;
				_dpiY = 96;
			}

			public UnoScreenshotResult(
				int width,
				int height,
				byte[] bytes,
				double dpiX,
				double dpiY)
			{
				Width = width;
				Height = height;
				_bytes = bytes;
				_dpiX = dpiX;
				_dpiY = dpiY;
			}

			public int Width { get; }

			public int Height { get; }

			public async Task<Stream> OpenReadAsync(
				ScreenshotFormat format = ScreenshotFormat.Png,
				int quality = 100)
			{
				var stream = new InMemoryRandomAccessStream();
				await EncodeAsync(format, stream).ConfigureAwait(false);
				return stream.AsStreamForRead();
			}

			public Task CopyToAsync(
				Stream destination,
				ScreenshotFormat format = ScreenshotFormat.Png,
				int quality = 100) =>
				EncodeAsync(format, destination.AsRandomAccessStream());

			async Task EncodeAsync(ScreenshotFormat format, IRandomAccessStream stream)
			{
				var encoder = await BitmapEncoder.CreateAsync(ToBitmapEncoder(format), stream)
					.AsTask()
					.ConfigureAwait(false);
				encoder.SetPixelData(
					BitmapPixelFormat.Bgra8,
					BitmapAlphaMode.Ignore,
					(uint)Width,
					(uint)Height,
					_dpiX,
					_dpiY,
					_bytes);
				await encoder.FlushAsync().AsTask().ConfigureAwait(false);
			}

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
