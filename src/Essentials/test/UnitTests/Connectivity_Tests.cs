using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;
using Xunit;

namespace Tests
{
	public class Connectivity_Tests
	{
		[Fact]
		public void Network_Access_On_NetStandard() =>
			Assert.Throws<NotImplementedInReferenceAssemblyException>(() => Connectivity.NetworkAccess);

		[Fact]
		public void ConnectionProfiles_On_NetStandard() =>
			Assert.Throws<NotImplementedInReferenceAssemblyException>(() => Connectivity.ConnectionProfiles);

		[Fact]
		public void Connectivity_Changed_Event_On_NetStandard() =>
			Assert.Throws<NotImplementedInReferenceAssemblyException>(() => Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged);

		[Theory]
		[InlineData(true, NetworkAccess.Unknown)]
		[InlineData(false, NetworkAccess.None)]
		public void Uno_Fallback_Network_Access_Map_Is_Non_Internet(bool isNetworkAvailable, NetworkAccess expected) =>
			Assert.Equal(expected, ConnectivityImplementation.GetFallbackNetworkAccess(isNetworkAvailable));

		void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
		{
		}
	}
}
