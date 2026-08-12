#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace Microsoft.Maui.Storage
{
	partial class FileSystemImplementation : IFileSystem
	{
		string PlatformCacheDirectory
			=> ApplicationData.Current.LocalCacheFolder.Path;

		string PlatformAppDataDirectory
			=> ApplicationData.Current.LocalFolder.Path;

		async Task<Stream> PlatformOpenAppPackageFileAsync(string filename)
		{
			var file = await GetAppPackageFileAsync(filename);
			return await file.OpenStreamForReadAsync();
		}

		async Task<bool> PlatformAppPackageFileExistsAsync(string filename)
		{
			if (!TryGetAppPackageUri(filename, out var uri))
				return false;

			try
			{
				await StorageFile.GetFileFromApplicationUriAsync(uri);
				return true;
			}
			catch (FileNotFoundException)
			{
				return false;
			}
		}

		static async Task<StorageFile> GetAppPackageFileAsync(string filename)
		{
			if (!TryGetAppPackageUri(filename, out var uri))
				throw new FileNotFoundException($"Unable to find app package file '{filename}'.", filename);

			return await StorageFile.GetFileFromApplicationUriAsync(uri);
		}

		static bool TryGetAppPackageUri(string filename, [NotNullWhen(true)] out Uri? uri)
		{
			uri = null;

			if (filename == null)
				throw new ArgumentNullException(nameof(filename));

			if (string.IsNullOrWhiteSpace(filename) || filename == "." || filename == "..")
				return false;

			if (!FileSystemUtils.IsValidRelativePath(filename))
				return false;

			var normalized = FileSystemUtils.NormalizePath(filename)
				.Replace(Path.DirectorySeparatorChar, '/');

			var segments = normalized.Split('/');
			if (segments.Length == 0)
				return false;

			for (var i = 0; i < segments.Length; i++)
			{
				if (string.IsNullOrEmpty(segments[i]))
					return false;

				segments[i] = Uri.EscapeDataString(segments[i]);
			}

			uri = new Uri($"ms-appx:///{string.Join("/", segments)}");
			return true;
		}
	}

	public partial class FileBase
	{
		internal FileBase(IStorageFile file)
			: this(GetFilePath(file))
		{
			ArgumentNullException.ThrowIfNull(file);

			File = file;
			ContentType = file.ContentType ?? DefaultContentType;
		}

		void PlatformInit(FileBase file)
		{
			File = file.File;
		}

		internal IStorageFile? File { get; set; }

		static string GetFilePath(IStorageFile file)
		{
			ArgumentNullException.ThrowIfNull(file);
			return file.Path;
		}

		string PlatformGetContentType(string extension) => string.Empty;

		internal async virtual Task<Stream> PlatformOpenReadAsync()
		{
			var file = File;
			if (file is null)
			{
				if (FullPath is null)
					throw new InvalidOperationException("Unable to open the file because the full path is missing.");

				file = await StorageFile.GetFileFromPathAsync(FullPath);
				File = file;
			}

			return await file.OpenStreamForReadAsync();
		}
	}

	public partial class FileResult
	{
		internal FileResult(IStorageFile file)
			: base(file)
		{
		}
	}
}
