using Uno.UI.Hosting;

namespace Maui.Controls.Sample.Uno.Minimal;

internal static class Program
{
	public static async Task Main(string[] args)
	{
		var host = UnoPlatformHostBuilder.Create()
			.App(() => new MinimalUnoApp())
			.UseWebAssembly()
			.Build();

		await host.RunAsync();
	}
}
