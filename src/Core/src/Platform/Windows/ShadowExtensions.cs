#nullable disable

using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics.Imaging;


namespace Microsoft.Maui.Platform
{
	internal static class ShadowExtensions
	{
		public static async Task<AlphaMaskResult> GetAlphaMaskAsync(this UIElement element)
		{
			AlphaMaskResult mask = null;

			try
			{
				//For some reason, using  TextBlock and getting the AlphaMask
				//generates a shadow with a size more smaller than the control size. 
				if (element is TextBlock textElement)
				{
					return new AlphaMaskResult(textElement.GetAlphaMask());
				}
				if (element is Image image)
				{
					return new AlphaMaskResult(image.GetAlphaMask());
				}
				if (element is Shape shape)
				{
					return new AlphaMaskResult(shape.GetAlphaMask());
				}
				else if (element is FrameworkElement frameworkElement)
				{
					var height = (int)frameworkElement.ActualHeight;
					var width = (int)frameworkElement.ActualWidth;

					if (height > 0 && width > 0)
					{
						var visual = ElementCompositionPreview.GetElementVisual(element);
						var elementVisual = visual.Compositor.CreateSpriteVisual();
						elementVisual.Size = element.RenderSize.ToVector2();
						var bitmap = new RenderTargetBitmap();

						await bitmap.RenderAsync(
							element,
							width,
							height);

						var pixels = await bitmap.GetPixelsAsync();

						using (var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
							pixels,
							BitmapPixelFormat.Bgra8,
							bitmap.PixelWidth,
							bitmap.PixelHeight,
							BitmapAlphaMode.Premultiplied))
						{
#if UNO
							var brush = await CompositionImageBrush.FromBGRASoftwareBitmapAsync(
#else
							var brush = CompositionImageBrush.FromBGRASoftwareBitmap(
#endif
								visual.Compositor,
								softwareBitmap,
								new Size(bitmap.PixelWidth, bitmap.PixelHeight));
							mask = new AlphaMaskResult(brush.Brush, brush);
						}
					}
				}
			}
			catch (Exception exc)
			{
				Debug.WriteLine($"Failed to get AlphaMask {exc}");
				mask = null;
			}

			return mask;
		}
	}

	internal sealed class AlphaMaskResult : IDisposable
	{
		readonly IDisposable _owner;

		public AlphaMaskResult(CompositionBrush brush, IDisposable owner = null)
		{
			Brush = brush;
			_owner = owner;
		}

		public CompositionBrush Brush { get; }

		public void Dispose() => _owner?.Dispose();
	}
}