using Uno.UI.Hosting;
using Uno.UI;

namespace Uno.Maui.Generated;

internal static class Program
{
	[STAThread]
	public static async Task Main(string[] args)
	{
#if UNO_MAUI_AUTO_ENABLE_ACCESSIBILITY
		FeatureConfiguration.AutomationPeer.AutoEnableAccessibility = true;
#endif

		var host = UnoPlatformHostBuilder.Create()
			.App(() => new UnoMauiApplication())
			.UseWin32()
			.UseX11()
			.UseLinuxFrameBuffer()
			.UseMacOS()
			.Build();

		await host.RunAsync();
	}
}
