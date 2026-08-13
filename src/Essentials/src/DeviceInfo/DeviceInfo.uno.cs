using System;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Devices
{
	class DeviceInfoImplementation : IDeviceInfo
	{
		static readonly Version currentVersion = Environment.OSVersion.Version;

		public string Model => string.Empty;

		public string Manufacturer => string.Empty;

		public string Name =>
			OperatingSystem.IsBrowser()
				? "WebAssembly Host"
				: Environment.MachineName;

		public string VersionString => currentVersion.ToString();

		public Version Version => currentVersion;

		public DevicePlatform Platform =>
			OperatingSystem.IsAndroid() ? DevicePlatform.Android :
			OperatingSystem.IsIOS() ? DevicePlatform.iOS :
			OperatingSystem.IsMacCatalyst() ? DevicePlatform.MacCatalyst :
			OperatingSystem.IsMacOS() ? DevicePlatform.macOS :
			OperatingSystem.IsTvOS() ? DevicePlatform.tvOS :
			OperatingSystem.IsWatchOS() ? DevicePlatform.watchOS :
			OperatingSystem.IsWindows() ? DevicePlatform.WinUI :
			DevicePlatform.Unknown;

		public DeviceIdiom Idiom =>
			OperatingSystem.IsTvOS() ? DeviceIdiom.TV :
			OperatingSystem.IsWatchOS() ? DeviceIdiom.Watch :
			OperatingSystem.IsWindows() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsMacOS()
				? DeviceIdiom.Desktop
				: DeviceIdiom.Unknown;

		public DeviceType DeviceType =>
			OperatingSystem.IsBrowser() ? DeviceType.Virtual : DeviceType.Physical;
	}
}
