using Uno.UI.Hosting;
using Uno.UI.Runtime.Skia.Win32;

namespace Microsoft.Maui.Controls.Sample.Uno;

internal static class Program
{
	[STAThread]
	public static async Task Main(string[] args)
	{
		var host = UnoPlatformHostBuilder.Create()
			.App(() => new UnoMauiApplication())
			.UseWin32()
			.Build();

		await host.RunAsync();
	}
}
