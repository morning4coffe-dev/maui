#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using WImageSource = Microsoft.UI.Xaml.Media.ImageSource;

namespace Microsoft.Maui
{
	public partial class FileImageSourceService
	{
		public override Task<IImageSourceServiceResult<WImageSource>?> GetImageSourceAsync(IImageSource imageSource, float scale = 1, CancellationToken cancellationToken = default) =>
			GetImageSourceAsync((IFileImageSource)imageSource, scale, cancellationToken);

		public async Task<IImageSourceServiceResult<WImageSource>?> GetImageSourceAsync(IFileImageSource imageSource, float scale = 1, CancellationToken cancellationToken = default)
		{
			if (imageSource.IsEmpty)
				return null;

			var filename = imageSource.File;

			try
			{
				var image = await GetLocal(filename) ?? GetAppPackage(filename);

				if (image == null)
					throw new InvalidOperationException("Unable to load image file.");

				var result = new ImageSourceServiceResult(image);

				return result;
			}
			catch (Exception ex)
			{
				Logger?.LogWarning(ex, "Unable to load image file '{File}'.", filename);
				throw;
			}
		}

		static BitmapImage GetAppPackage(string filename)
		{
			// Handle LogicalName with path separators (e.g., "challenges/groceries.png")
			// Extract just the filename since Windows app package has flattened resources
			var resourceName = Path.GetFileName(filename);
#if UNO
			if (OperatingSystem.IsBrowser())
			{
				var extension = Path.GetExtension(resourceName);
				var name = Path.GetFileNameWithoutExtension(resourceName);

				if (string.IsNullOrEmpty(extension))
				{
					extension = ".png";
					name = resourceName;
				}
				else if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
				{
					extension = ".png";
				}

				resourceName = $"{name}.scale-100{extension}";
			}
#endif
			return new BitmapImage(new Uri("ms-appx:///" + resourceName));
		}

		static async Task<BitmapImage?> GetLocal(string filename)
		{
			if (Path.IsPathRooted(filename))
			{
				try
				{
					var file = await StorageFile.GetFileFromPathAsync(filename);
					using var stream = await file.OpenReadAsync();

					var image = new BitmapImage();

					await image.SetSourceAsync(stream);

					return image;
				}
				catch
				{
				}
			}

			return null;
		}
	}
}