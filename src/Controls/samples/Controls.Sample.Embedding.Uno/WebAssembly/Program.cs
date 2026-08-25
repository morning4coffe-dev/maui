using Uno.UI.Hosting;

namespace Maui.Controls.Sample.Uno;

internal static class Program
{
	public static async Task Main(string[] args)
	{
		var host = UnoPlatformHostBuilder.Create()
			.App(() => new UnoEmbeddingApplication())
			.UseWebAssembly()
			.Build();

		await host.RunAsync();
	}
}
