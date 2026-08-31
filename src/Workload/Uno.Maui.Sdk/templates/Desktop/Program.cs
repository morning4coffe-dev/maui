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
#if UNO_MAUI_HOST_WIN32
			.UseWin32()
#endif
#if UNO_MAUI_HOST_X11
			.UseX11()
#endif
#if UNO_MAUI_HOST_FRAMEBUFFER
			.UseLinuxFrameBuffer()
#endif
#if UNO_MAUI_HOST_MACOS
			.UseMacOS()
#endif
			.Build();

		await host.RunAsync();
	}
}
