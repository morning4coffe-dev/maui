#if UNO
#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Skia;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using Windows.Storage;
using WImageSource = Microsoft.UI.Xaml.Media.ImageSource;

namespace Microsoft.Maui
{
	public partial class FontImageSourceService
	{
		public override Task<IImageSourceServiceResult<WImageSource>?> GetImageSourceAsync(
			IImageSource imageSource,
			float scale = 1,
			CancellationToken cancellationToken = default) =>
			GetImageSourceAsync((IFontImageSource)imageSource, scale, cancellationToken);

		public async Task<IImageSourceServiceResult<WImageSource>?> GetImageSourceAsync(
			IFontImageSource imageSource,
			float scale = 1,
			CancellationToken cancellationToken = default)
		{
			if (imageSource.IsEmpty)
				return null;

			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				scale = scale > 0 ? scale : 1;
				var image = await RenderImageSourceAsync(imageSource, scale, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				return new ImageSourceServiceResult(image, true);
			}
			catch (Exception ex)
			{
				Logger?.LogWarning(ex, "Unable to generate font image '{Glyph}'.", imageSource.Glyph);
				throw;
			}
		}

		internal async Task<BitmapImage> RenderImageSourceAsync(
			IFontImageSource imageSource,
			float scale,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			using var typeface = ResolveTypeface(imageSource.Font);
			using var font = new SKFont
			{
				Typeface = typeface,
				Size = (float)FontManager.GetFontSize(imageSource.Font) * scale,
			};
			using var paint = new SKPaint
			{
				Color = (imageSource.Color ?? Colors.White).AsSKColor(),
				IsAntialias = true,
			};

			font.MeasureText(imageSource.Glyph, out var bounds, paint);
			var padding = Math.Max(1, scale);
			var width = Math.Max(1, (int)Math.Ceiling(bounds.Width + padding * 2));
			var height = Math.Max(1, (int)Math.Ceiling(bounds.Height + padding * 2));

			using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
			using var canvas = new SKCanvas(bitmap);
			canvas.Clear(SKColors.Transparent);
			canvas.DrawText(imageSource.Glyph, padding - bounds.Left, padding - bounds.Top, font, paint);

			using var image = SKImage.FromBitmap(bitmap);
			using var data = image.Encode(SKEncodedImageFormat.Png, 100);
			using var stream = new MemoryStream();
			data.SaveTo(stream);
			stream.Position = 0;

			cancellationToken.ThrowIfCancellationRequested();

			var result = new BitmapImage();
			using var randomAccessStream = stream.AsRandomAccessStream();
			await result.SetSourceAsync(randomAccessStream);
			return result;
		}

		SKTypeface ResolveTypeface(Font font)
		{
			var sources = FontManager.GetFontFamily(font).Source
				.Split(',', StringSplitOptions.RemoveEmptyEntries);
			var source = sources.Length > 0 ? sources[0].Trim() : font.Family ?? string.Empty;
			var fragmentIndex = source.IndexOf('#', StringComparison.Ordinal);
			var familyName = fragmentIndex >= 0 ? source[(fragmentIndex + 1)..] : font.Family;
			var sourcePath = fragmentIndex >= 0 ? source[..fragmentIndex] : source;

			var filePath = ResolveFontFilePath(sourcePath);
			if (filePath is not null)
			{
				var fileTypeface = SKTypeface.FromFile(filePath, 0);
				if (fileTypeface is not null)
					return fileTypeface;
			}

			return SKTypeface.FromFamilyName(
				familyName,
				(int)font.Weight,
				(int)SKFontStyleWidth.Normal,
				font.Slant switch
				{
					FontSlant.Italic => SKFontStyleSlant.Italic,
					FontSlant.Oblique => SKFontStyleSlant.Oblique,
					_ => SKFontStyleSlant.Upright,
				}) ?? SKTypeface.CreateDefault();
		}

		static string? ResolveFontFilePath(string source)
		{
			if (string.IsNullOrWhiteSpace(source))
				return null;

			if (!Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out var uri))
				return null;

			if (uri.IsAbsoluteUri && uri.IsFile && File.Exists(uri.LocalPath))
				return uri.LocalPath;

			if (uri.IsAbsoluteUri && uri.Scheme.Equals("ms-appdata", StringComparison.OrdinalIgnoreCase))
			{
				var appDataPath = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/', '\\');
				const string tempPrefix = "temp/";
				if (appDataPath.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase))
					appDataPath = appDataPath[tempPrefix.Length..];

				return ResolveUnderRoot(ApplicationData.Current.TemporaryFolder.Path, appDataPath);
			}

			var relativePath = uri.IsAbsoluteUri
				? uri.LocalPath.TrimStart('/', '\\')
				: source.TrimStart('/', '\\');
			var fullPath = ResolveUnderRoot(AppContext.BaseDirectory, relativePath);
			if (fullPath is not null)
				return fullPath;

			var flattenedPath = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(relativePath));
			return File.Exists(flattenedPath) ? flattenedPath : null;
		}

		static string? ResolveUnderRoot(string root, string relativePath)
		{
			var fullRoot = Path.GetFullPath(root);
			var fullPath = Path.GetFullPath(Path.Combine(
				fullRoot,
				relativePath.Replace('/', Path.DirectorySeparatorChar)));
			var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
				? fullRoot
				: fullRoot + Path.DirectorySeparatorChar;
			var comparison = OperatingSystem.IsWindows()
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal;

			if (!fullPath.StartsWith(rootPrefix, comparison))
				return null;

			return File.Exists(fullPath) ? fullPath : null;
		}
	}
}
#endif
