#if UNO
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Skia;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
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
			var fontStyle = CreateFontStyle(font);
			var candidates = GetFontCandidates(font);

			foreach (var candidate in candidates)
			{
				if (candidate.FilePath is { Length: > 0 } filePath)
				{
					var fileTypeface = CreateFileTypeface(filePath);
					if (fileTypeface is not null)
						return fileTypeface;
				}
			}

			foreach (var candidate in candidates)
			{
				if (candidate.FamilyName is { Length: > 0 } familyName)
				{
					var familyTypeface = CreateFamilyTypeface(familyName, fontStyle);
					if (familyTypeface is not null)
						return familyTypeface;
				}
			}

			return SKTypeface.FromFamilyName(null, fontStyle) ?? SKTypeface.CreateDefault();
		}

		internal SkiaFontCandidate[] GetFontCandidates(Font font)
		{
			var candidates = new List<SkiaFontCandidate>();
			var seenFamilyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var rawCandidate in FontManager.GetFontFamily(font).Source.Split(',', StringSplitOptions.RemoveEmptyEntries))
			{
				var source = rawCandidate.Trim();
				if (source.Length == 0)
					continue;

				var filePath = FontSourceResolver.ResolveFilePath(source);
				var familyName = FontSourceResolver.ResolveFamilyName(source) ?? FontSourceResolver.GetFamilyNameFragment(source);

				if (string.IsNullOrWhiteSpace(familyName)
					&& filePath is null
					&& !FontSourceResolver.LooksLikeFileReference(source))
				{
					familyName = FontSourceResolver.GetSourcePath(source);
				}

				if (!string.IsNullOrWhiteSpace(familyName))
				{
					if (!seenFamilyNames.Add(familyName))
						familyName = null;
				}

				if (!string.IsNullOrWhiteSpace(familyName) || filePath is not null)
					candidates.Add(new SkiaFontCandidate(familyName, filePath));
			}

			return candidates.ToArray();
		}

		static SKFontStyle CreateFontStyle(Font font) =>
			new(
				(SKFontStyleWeight)(int)font.Weight,
				SKFontStyleWidth.Normal,
				font.Slant switch
				{
					FontSlant.Italic => SKFontStyleSlant.Italic,
					FontSlant.Oblique => SKFontStyleSlant.Oblique,
					_ => SKFontStyleSlant.Upright,
				});

		static SKTypeface? CreateFamilyTypeface(string familyName, SKFontStyle fontStyle)
		{
			try
			{
				using var fontStyles = SKFontManager.Default.GetFontStyles(familyName);
				if (fontStyles.Count == 0)
					return null;

				return fontStyles.CreateTypeface(fontStyle);
			}
			catch
			{
				return null;
			}
		}

		static SKTypeface? CreateFileTypeface(string filePath)
		{
			try
			{
				return SKTypeface.FromFile(filePath, 0);
			}
			catch
			{
				return null;
			}
		}

		internal readonly struct SkiaFontCandidate
		{
			public SkiaFontCandidate(string? familyName, string? filePath)
			{
				FamilyName = familyName;
				FilePath = filePath;
			}

			public string? FamilyName { get; }
			public string? FilePath { get; }
		}
	}
}
#endif
