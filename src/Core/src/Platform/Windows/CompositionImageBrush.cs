#nullable disable
using System;
#if UNO
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
#else
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Windows.Foundation;
using Windows.Graphics.Imaging;
#endif

namespace Microsoft.Maui.Platform
{
#if UNO
	class CompositionImageBrush : IDisposable
	{
		readonly LoadedImageSurface _surface;
		readonly CompositionSurfaceBrush _brush;
		InMemoryRandomAccessStream _stream;

		CompositionImageBrush(
			LoadedImageSurface surface,
			CompositionSurfaceBrush brush,
			InMemoryRandomAccessStream stream)
		{
			_surface = surface;
			_brush = brush;
			_stream = stream;
			_surface.LoadCompleted += OnLoadCompleted;
		}

		public CompositionBrush Brush => _brush;

		public static async Task<CompositionImageBrush> FromBGRASoftwareBitmapAsync(
			Compositor compositor,
			SoftwareBitmap bitmap,
			Size outputSize)
		{
			var stream = new InMemoryRandomAccessStream();
			LoadedImageSurface surface = null;
			try
			{
				var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
				encoder.SetSoftwareBitmap(bitmap);
				await encoder.FlushAsync();
				stream.Seek(0);

				surface = LoadedImageSurface.StartLoadFromStream(stream, outputSize);
				return new CompositionImageBrush(
					surface,
					compositor.CreateSurfaceBrush(surface),
					stream);
			}
			catch
			{
				surface?.Dispose();
				stream.Dispose();
				throw;
			}
		}

		void OnLoadCompleted(
			LoadedImageSurface sender,
			LoadedImageSourceLoadCompletedEventArgs args)
		{
			sender.LoadCompleted -= OnLoadCompleted;
			_stream?.Dispose();
			_stream = null;
		}

		public void Dispose()
		{
			_surface.LoadCompleted -= OnLoadCompleted;
			_stream?.Dispose();
			_stream = null;
			_brush.Dispose();
			_surface.Dispose();
		}
	}
#else
	class CompositionImageBrush : IDisposable
	{
		CompositionGraphicsDevice _graphicsDevice;
		CompositionDrawingSurface _drawingSurface;
		CompositionSurfaceBrush _drawingBrush;

		public CompositionBrush Brush => _drawingBrush;

		public CompositionImageBrush()
		{
		}

		void CreateDevice(Compositor compositor)
		{
			_graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(
				compositor, CanvasDevice.GetSharedDevice());
		}

		void CreateDrawingSurface(global::Windows.Foundation.Size drawSize)
		{
			_drawingSurface = _graphicsDevice.CreateDrawingSurface(
				drawSize,
				DirectXPixelFormat.B8G8R8A8UIntNormalized,
				DirectXAlphaMode.Premultiplied);
		}

		void CreateSurfaceBrush(Compositor compositor)
		{
			_drawingBrush = compositor.CreateSurfaceBrush(_drawingSurface);
		}

		public static CompositionImageBrush FromBGRASoftwareBitmap(
			Compositor compositor,
			SoftwareBitmap bitmap,
			Size outputSize)
		{
			CompositionImageBrush brush = new CompositionImageBrush();

			brush.CreateDevice(compositor);

			brush.CreateDrawingSurface(outputSize);
			brush.DrawSoftwareBitmap(bitmap, outputSize);
			brush.CreateSurfaceBrush(compositor);

			return (brush);
		}

		void DrawSoftwareBitmap(SoftwareBitmap softwareBitmap, Size renderSize)
		{
			using (var drawingSession = CanvasComposition.CreateDrawingSession(_drawingSurface))
			using (var bitmap = CanvasBitmap.CreateFromSoftwareBitmap(drawingSession.Device, softwareBitmap))
			{
				drawingSession.DrawImage(bitmap,
					new Rect(0, 0, renderSize.Width, renderSize.Height));
			}
		}

		public void Dispose()
		{
			_drawingBrush.Dispose();
			_drawingSurface.Dispose();
			_graphicsDevice.Dispose();
		}
	}
#endif
}
