#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Maui.ApplicationModel;
using Windows.Networking.Connectivity;

using NetworkInformation = Windows.Networking.Connectivity.NetworkInformation;

namespace Microsoft.Maui.Networking
{
	partial class ConnectivityImplementation : IConnectivity
	{
		void StartListeners()
		{
			if (OperatingSystem.IsAndroid())
			{
				throw new FeatureNotSupportedException(
					"ConnectivityChanged notifications are not supported by the Uno Android host.");
			}

			try
			{
				ExecuteWithPermissionTranslation(
					() => NetworkInformation.NetworkStatusChanged += NetworkStatusChanged);
			}
			catch (PlatformNotSupportedException ex)
			{
				throw CreateUnsupportedListenerException(ex);
			}
			catch (NotImplementedException ex)
			{
				throw CreateUnsupportedListenerException(ex);
			}
		}

		void StopListeners() =>
			NetworkInformation.NetworkStatusChanged -= NetworkStatusChanged;

		void NetworkStatusChanged(object sender) =>
			OnConnectivityChanged();

		public NetworkAccess NetworkAccess
		{
			get
			{
				global::Windows.Networking.Connectivity.ConnectionProfile? profile;
				try
				{
					profile = ExecuteWithPermissionTranslation(
						NetworkInformation.GetInternetConnectionProfile);
				}
				catch (PlatformNotSupportedException)
				{
					return GetFallbackNetworkAccess();
				}
				catch (NotImplementedException)
				{
					return GetFallbackNetworkAccess();
				}

				if (profile is null)
				{
					return NetworkAccess.Unknown;
				}

				return profile.GetNetworkConnectivityLevel() switch
				{
					NetworkConnectivityLevel.LocalAccess => NetworkAccess.Local,
					NetworkConnectivityLevel.InternetAccess => NetworkAccess.Internet,
					NetworkConnectivityLevel.ConstrainedInternetAccess => NetworkAccess.ConstrainedInternet,
					_ => NetworkAccess.None,
				};
			}
		}

		public IEnumerable<ConnectionProfile> ConnectionProfiles
		{
			get
			{
				NetworkInterface[] networkInterfaces;
				try
				{
					networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
				}
				catch (NetworkInformationException ex)
				{
					Debug.WriteLine($"Unable to get network interfaces. Error: {ex.Message}");
					yield break;
				}
				catch (PlatformNotSupportedException)
				{
					yield break;
				}
				catch (UnauthorizedAccessException) when (OperatingSystem.IsAndroid())
				{
					throw CreateNetworkStatePermissionException();
				}

				foreach (var networkInterface in networkInterfaces)
				{
					if (networkInterface.OperationalStatus is not OperationalStatus.Up ||
						networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
					{
						continue;
					}

					yield return networkInterface.NetworkInterfaceType switch
					{
						NetworkInterfaceType.Ethernet => ConnectionProfile.Ethernet,
						NetworkInterfaceType.Wireless80211 => ConnectionProfile.WiFi,
						NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => ConnectionProfile.Cellular,
						_ => ConnectionProfile.Unknown,
					};
				}
			}
		}

		static T ExecuteWithPermissionTranslation<T>(Func<T> action)
		{
			try
			{
				return action();
			}
			catch (UnauthorizedAccessException) when (OperatingSystem.IsAndroid())
			{
				throw CreateNetworkStatePermissionException();
			}
		}

		static void ExecuteWithPermissionTranslation(Action action)
		{
			try
			{
				action();
			}
			catch (UnauthorizedAccessException) when (OperatingSystem.IsAndroid())
			{
				throw CreateNetworkStatePermissionException();
			}
		}

		static PermissionException CreateNetworkStatePermissionException() =>
			new("ACCESS_NETWORK_STATE must be declared in the Android manifest to use Connectivity.");

		static FeatureNotSupportedException CreateUnsupportedListenerException(Exception innerException) =>
			new("ConnectivityChanged notifications are not supported by this Uno host.", innerException);

		static NetworkAccess GetFallbackNetworkAccess()
		{
			try
			{
				return GetFallbackNetworkAccess(
					ExecuteWithPermissionTranslation(NetworkInterface.GetIsNetworkAvailable));
			}
			catch (PlatformNotSupportedException ex)
			{
				Debug.WriteLine($"Unable to determine fallback network availability. Error: {ex.Message}");
				return NetworkAccess.Unknown;
			}
			catch (NotImplementedException ex)
			{
				Debug.WriteLine($"Unable to determine fallback network availability. Error: {ex.Message}");
				return NetworkAccess.Unknown;
			}
		}
	}
}
