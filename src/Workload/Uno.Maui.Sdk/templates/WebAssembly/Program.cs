using Uno.UI.Hosting;

namespace Uno.Maui.Generated;

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
