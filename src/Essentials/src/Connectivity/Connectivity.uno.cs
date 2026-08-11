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
		void StartListeners() =>
			ExecuteWithPermissionTranslation(
				() => NetworkInformation.NetworkStatusChanged += NetworkStatusChanged);

		void StopListeners() =>
			NetworkInformation.NetworkStatusChanged -= NetworkStatusChanged;

		void NetworkStatusChanged(object sender) =>
			OnConnectivityChanged();

		public NetworkAccess NetworkAccess
		{
			get
			{
				var profile = ExecuteWithPermissionTranslation(
					NetworkInformation.GetInternetConnectionProfile);
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
	}
}
