#nullable enable
using System;
using System.Threading.Tasks;
using WinLauncher = Windows.System.Launcher;

namespace Microsoft.Maui.ApplicationModel
{
	partial class LauncherImplementation
	{
		async Task<bool> PlatformCanOpenAsync(Uri uri)
		{
			if (OperatingSystem.IsBrowser())
				return uri.IsAbsoluteUri && !uri.IsFile;

			var supported = await WinLauncher.QueryUriSupportAsync(
				uri,
				global::Windows.System.LaunchQuerySupportType.Uri);
			return supported == global::Windows.System.LaunchQuerySupportStatus.Available;
		}

		Task<bool> PlatformOpenAsync(Uri uri) =>
			WinLauncher.LaunchUriAsync(uri).AsTask();

		Task<bool> PlatformOpenAsync(OpenFileRequest request) =>
			Task.FromException<bool>(
				new FeatureNotSupportedException(
					"Opening files through Launcher is not supported by the Uno hosts."));

		async Task<bool> PlatformTryOpenAsync(Uri uri)
		{
			if (OperatingSystem.IsBrowser())
				return await PlatformOpenAsync(uri);

			return await PlatformCanOpenAsync(uri) && await PlatformOpenAsync(uri);
		}
	}
}
