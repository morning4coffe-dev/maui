using System.Diagnostics;
using Uno.UI.Hosting;
using Uno.UI;

namespace Uno.Maui.Generated;

internal static class Program
{
	public static async Task Main(string[] args)
	{
#if UNO_MAUI_STARTUP_TRACING
		var startupTimestamp = Stopwatch.GetTimestamp();
		TracePhase("host_build_start", startupTimestamp);
#endif

#if UNO_MAUI_AUTO_ENABLE_ACCESSIBILITY
		FeatureConfiguration.AutomationPeer.AutoEnableAccessibility = true;
#endif

		var host = UnoPlatformHostBuilder.Create()
			.App(() => new UnoMauiApplication())
			.UseWebAssembly()
			.Build();

#if UNO_MAUI_STARTUP_TRACING
		TracePhase("host_build_complete", startupTimestamp);
		var runTimestamp = Stopwatch.GetTimestamp();
		TracePhase("host_run_start", runTimestamp);
#endif

		try
		{
			await host.RunAsync();
		}
		finally
		{
#if UNO_MAUI_STARTUP_TRACING
			TracePhase("host_run_complete", runTimestamp);
#endif
		}
	}

#if UNO_MAUI_STARTUP_TRACING
	private static void TracePhase(string phase, long startTimestamp) =>
		Console.WriteLine(
			$"uno_maui_startup phase={phase} elapsed_ms={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F3}");
#endif
}
