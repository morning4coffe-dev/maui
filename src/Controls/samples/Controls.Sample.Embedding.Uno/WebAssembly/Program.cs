using Uno.UI.Hosting;

namespace Maui.Controls.Sample.Uno;

internal static class Program
{
	public static async Task Main(string[] args)
	{
#if TIER2_PROBE
		// The browser head has no environment to configure, so the verification build turns the probe on
		// for the shared code that reads this variable.
		Environment.SetEnvironmentVariable(Tier2Probe.EnableVariable, "1");
#endif

#if FULL_HANDLERS
		Environment.SetEnvironmentVariable("MAUI_UNO_HANDLER_MODE", "full");
#endif

#if PRODUCTION_PROBE
		Environment.SetEnvironmentVariable("MAUI_UNO_PRODUCTION_PROBE", "1");
#endif

		var host = UnoPlatformHostBuilder.Create()
			.App(() => new UnoEmbeddingApplication())
			.UseWebAssembly()
			.Build();

		await host.RunAsync();
	}
}
