using Uno.UI.Hosting;

namespace Uno.Maui.Generated;

internal static class Program
{
	[STAThread]
	public static async Task Main(string[] args)
	{
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
