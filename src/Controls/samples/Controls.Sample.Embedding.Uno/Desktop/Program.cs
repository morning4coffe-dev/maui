using Uno.UI.Hosting;

namespace Maui.Controls.Sample.Uno;

internal static class Program
{
	[STAThread]
	public static async Task Main(string[] args)
	{
		var host = UnoPlatformHostBuilder.Create()
			.App(() => new UnoEmbeddingApplication())
			.UseWin32()
			.UseX11()
			.UseLinuxFrameBuffer()
			.UseMacOS()
			.Build();

		await host.RunAsync();
	}
}
