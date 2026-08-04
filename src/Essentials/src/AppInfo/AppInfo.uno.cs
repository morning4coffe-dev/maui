#nullable enable
using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;

namespace Microsoft.Maui.ApplicationModel
{
	class AppInfoImplementation : IAppInfo
	{
		const string BuildStringMetadataKey = "Microsoft.Maui.ApplicationModel.AppInfo.BuildString";
		const string DisplayVersionMetadataKey = "Microsoft.Maui.ApplicationModel.AppInfo.DisplayVersion";
		const int ErrorInsufficientBuffer = 122;
		const int AppModelErrorNoPackage = 15700;

		static readonly Assembly LaunchingAssembly =
			Assembly.GetEntryAssembly() ?? typeof(AppInfoImplementation).Assembly;

		public string PackageName => Package.Current.Id.Name;

		public string Name =>
			string.IsNullOrWhiteSpace(Package.Current.DisplayName)
				? LaunchingAssembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? LaunchingAssembly.GetName().Name ?? string.Empty
				: Package.Current.DisplayName;

		public Version Version
		{
			get
			{
				var displayVersion = GetMetadataValue(DisplayVersionMetadataKey);
				if (!string.IsNullOrWhiteSpace(displayVersion))
				{
					return Utils.ParseVersion(displayVersion);
				}

				var version = Package.Current.Id.Version;
				return new Version(version.Major, version.Minor, version.Build, version.Revision);
			}
		}

		public string VersionString =>
			GetMetadataValue(DisplayVersionMetadataKey) ??
			Version.ToString();

		public string BuildString =>
			GetMetadataValue(BuildStringMetadataKey) ??
			Version.Revision.ToString(CultureInfo.InvariantCulture);

		public void ShowSettingsUI() =>
			throw new FeatureNotSupportedException("Opening application settings is not supported by the Uno Essentials projection.");

		public AppTheme RequestedTheme =>
			Application.Current?.RequestedTheme switch
			{
				ApplicationTheme.Dark => AppTheme.Dark,
				ApplicationTheme.Light => AppTheme.Light,
				_ => AppTheme.Unspecified,
			};

		public AppPackagingModel PackagingModel =>
			OperatingSystem.IsWindows() && !HasPackageIdentity()
				? AppPackagingModel.Unpackaged
				: AppPackagingModel.Packaged;

		public LayoutDirection RequestedLayoutDirection =>
			CultureInfo.CurrentCulture.TextInfo.IsRightToLeft
				? LayoutDirection.RightToLeft
				: LayoutDirection.LeftToRight;

		static string? GetMetadataValue(string key)
		{
			foreach (var attribute in LaunchingAssembly.GetCustomAttributes<AssemblyMetadataAttribute>())
			{
				if (attribute.Key == key)
				{
					return attribute.Value;
				}
			}

			return null;
		}

		static bool HasPackageIdentity()
		{
			var packageFullNameLength = 0;
			var result = GetCurrentPackageFullName(ref packageFullNameLength, null);
			return result switch
			{
				0 or ErrorInsufficientBuffer => true,
				AppModelErrorNoPackage => false,
				_ => throw new Win32Exception(result),
			};
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		static extern int GetCurrentPackageFullName(
			ref int packageFullNameLength,
			StringBuilder? packageFullName);
	}
}
