using Uno.UI.Hosting;

namespace Microsoft.Maui.Controls.Sample.Uno;

internal static class Program
{
	public static async Task Main(string[] args)
	{
		var host = UnoPlatformHostBuilder.Create()
			.App(() => new UnoMauiApplication())
			.UseWebAssembly()
			.Build();

		await host.RunAsync();
	}
}
