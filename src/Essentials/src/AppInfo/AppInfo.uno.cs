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
		static readonly Lazy<Package?> CurrentPackage = new(GetCurrentPackage);

		public string PackageName =>
			CurrentPackage.Value?.Id.Name ??
			LaunchingAssembly.GetName().Name ??
			string.Empty;

		public string Name =>
			string.IsNullOrWhiteSpace(CurrentPackage.Value?.DisplayName)
				? LaunchingAssembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? LaunchingAssembly.GetName().Name ?? string.Empty
				: CurrentPackage.Value.DisplayName;

		public Version Version
		{
			get
			{
				var displayVersion = GetMetadataValue(DisplayVersionMetadataKey);
				if (!string.IsNullOrWhiteSpace(displayVersion))
				{
					return Utils.ParseVersion(displayVersion);
				}

				if (CurrentPackage.Value is { } package)
				{
					var version = package.Id.Version;
					return new Version(version.Major, version.Minor, version.Build, version.Revision);
				}

				return LaunchingAssembly.GetName().Version ?? new Version(1, 0);
			}
		}

		public string VersionString =>
			GetMetadataValue(DisplayVersionMetadataKey) ??
			Version.ToString();

		public string BuildString =>
			GetMetadataValue(BuildStringMetadataKey) ??
			Math.Max(0, Version.Revision).ToString(CultureInfo.InvariantCulture);

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
			CurrentPackage.Value is null
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

		static Package? GetCurrentPackage()
		{
			if (OperatingSystem.IsWindows() && !HasPackageIdentity())
			{
				return null;
			}

			try
			{
				return Package.Current;
			}
			catch (InvalidOperationException)
			{
				return null;
			}
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
