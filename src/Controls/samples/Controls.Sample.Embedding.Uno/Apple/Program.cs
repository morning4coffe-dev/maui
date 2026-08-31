using Uno.UI.Hosting;

namespace Maui.Controls.Sample.Uno;

internal static class Program
{
	public static void Main(string[] args)
	{
#if TIER2_PROBE
		Environment.SetEnvironmentVariable(Tier2Probe.EnableVariable, "1");
#endif

		var host = UnoPlatformHostBuilder.Create()
			.App(() => new UnoEmbeddingApplication())
			.UseAppleUIKit()
			.Build();

		host.Run();
	}
}
