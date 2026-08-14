#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
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
			if (OperatingSystem.IsAndroid())
				return OpenAndroidPackageAsset(filename);

			var file = await GetAppPackageFileAsync(filename);
			return await file.OpenStreamForReadAsync();
		}

		async Task<bool> PlatformAppPackageFileExistsAsync(string filename)
		{
			if (!TryNormalizePackageAssetPath(filename, out var normalized))
				return false;

			if (OperatingSystem.IsAndroid())
			{
				var entryName = GetAndroidAssetEntryName(normalized);
				foreach (var packagePath in GetAndroidPackagePaths())
				{
					using var archive = ZipFile.OpenRead(packagePath);
					if (archive.GetEntry(entryName) is not null)
						return true;
				}

				return false;
			}

			var uri = CreateAppPackageUri(normalized);
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
			if (!TryNormalizePackageAssetPath(filename, out var normalized))
				throw new FileNotFoundException($"Unable to find app package file '{filename}'.", filename);

			return await StorageFile.GetFileFromApplicationUriAsync(CreateAppPackageUri(normalized));
		}

		static Stream OpenAndroidPackageAsset(string filename)
		{
			if (!TryNormalizePackageAssetPath(filename, out var normalized))
				throw new FileNotFoundException($"Unable to find app package file '{filename}'.", filename);

			var entryName = GetAndroidAssetEntryName(normalized);
			foreach (var packagePath in GetAndroidPackagePaths())
			{
				var archive = ZipFile.OpenRead(packagePath);
				var entry = archive.GetEntry(entryName);
				if (entry is not null)
					return new AndroidPackageAssetStream(archive, entry.Open());

				archive.Dispose();
			}

			throw new FileNotFoundException($"Unable to find app package file '{filename}'.", filename);
		}

		static IEnumerable<string> GetAndroidPackagePaths()
		{
			const string assetsPrefix = "assets://";
			var installedPath = global::Windows.ApplicationModel.Package.Current.InstalledPath;

			if (!installedPath.StartsWith(assetsPrefix, StringComparison.Ordinal))
				throw new InvalidOperationException($"The Android package path '{installedPath}' is invalid.");

			var basePackagePath = installedPath.Substring(assetsPrefix.Length);
			yield return basePackagePath;

			var packageDirectory = Path.GetDirectoryName(basePackagePath);
			if (string.IsNullOrEmpty(packageDirectory))
				yield break;

			foreach (var packagePath in Directory.EnumerateFiles(packageDirectory, "*.apk"))
			{
				if (!string.Equals(packagePath, basePackagePath, StringComparison.Ordinal))
					yield return packagePath;
			}
		}

		static string GetAndroidAssetEntryName(string normalized) =>
			$"assets/{normalized}";

		static bool TryNormalizePackageAssetPath(string filename, [NotNullWhen(true)] out string? normalized)
		{
			normalized = null;

			if (filename == null)
				throw new ArgumentNullException(nameof(filename));

			if (string.IsNullOrWhiteSpace(filename) || filename == "." || filename == "..")
				return false;

			if (!FileSystemUtils.IsValidRelativePath(filename))
				return false;

			normalized = FileSystemUtils.NormalizePackageAssetPath(filename);
			var segments = normalized.Split('/');
			if (segments.Length == 0)
				return false;

			for (var i = 0; i < segments.Length; i++)
			{
				if (string.IsNullOrEmpty(segments[i]) || segments[i] == ".")
					return false;
			}

			return true;
		}

		static Uri CreateAppPackageUri(string normalized) =>
			new($"ms-appx:///{string.Join("/", normalized.Split('/').Select(Uri.EscapeDataString))}");

		sealed class AndroidPackageAssetStream(ZipArchive archive, Stream stream) : Stream
		{
			bool disposed;

			public override bool CanRead => stream.CanRead;
			public override bool CanSeek => stream.CanSeek;
			public override bool CanWrite => stream.CanWrite;
			public override long Length => stream.Length;
			public override long Position
			{
				get => stream.Position;
				set => stream.Position = value;
			}

			public override void Flush() => stream.Flush();

			public override int Read(byte[] buffer, int offset, int count) =>
				stream.Read(buffer, offset, count);

			public override int Read(Span<byte> buffer) =>
				stream.Read(buffer);

			public override Task<int> ReadAsync(
				byte[] buffer,
				int offset,
				int count,
				CancellationToken cancellationToken) =>
				stream.ReadAsync(buffer, offset, count, cancellationToken);

			public override ValueTask<int> ReadAsync(
				Memory<byte> buffer,
				CancellationToken cancellationToken = default) =>
				stream.ReadAsync(buffer, cancellationToken);

			public override long Seek(long offset, SeekOrigin origin) =>
				stream.Seek(offset, origin);

			public override void SetLength(long value) =>
				stream.SetLength(value);

			public override void Write(byte[] buffer, int offset, int count) =>
				stream.Write(buffer, offset, count);

			protected override void Dispose(bool disposing)
			{
				if (disposing && !disposed)
				{
					disposed = true;
					stream.Dispose();
					archive.Dispose();
				}

				base.Dispose(disposing);
			}

			public override async ValueTask DisposeAsync()
			{
				if (!disposed)
				{
					disposed = true;
					try
					{
						await stream.DisposeAsync();
					}
					finally
					{
						archive.Dispose();
					}
				}

				GC.SuppressFinalize(this);
			}
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
