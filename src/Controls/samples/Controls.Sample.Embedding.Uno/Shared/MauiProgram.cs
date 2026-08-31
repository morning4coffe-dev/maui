using System;
using System.Linq;
using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Controls.Embedding.Uno;
using Microsoft.Maui.Hosting;
using Syncfusion.Maui.Toolkit.Uno;

namespace Maui.Controls.Sample.Uno;

public static class MauiProgram
{
	/// <summary>
	/// Selects the handler set. Unset means <see cref="UnoHandlerMode.Default"/>, so the default path stays
	/// exactly what it was.
	/// </summary>
	/// <remarks>
	/// Both an environment variable and the command line are honoured. The browser head has neither, so it
	/// bakes the choice in with the <c>MauiUnoFullHandlers</c> build switch; on Desktop either
	/// <c>MAUI_UNO_HANDLER_MODE=full</c> or a <c>handlers=full</c> argument works without a rebuild.
	/// </remarks>
	public static UnoHandlerMode HandlerMode =>
		IsFullRequestedByEnvironment() || IsFullRequestedByCommandLine()
			? UnoHandlerMode.Full
			: UnoHandlerMode.Default;

	static bool IsFullRequestedByEnvironment() =>
		string.Equals(
			Environment.GetEnvironmentVariable("MAUI_UNO_HANDLER_MODE"),
			"full",
			StringComparison.OrdinalIgnoreCase);

	static bool IsFullRequestedByCommandLine()
	{
		try
		{
			return Environment.GetCommandLineArgs()
				.Any(a => a.Contains("handlers=full", StringComparison.OrdinalIgnoreCase));
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static MauiApp CreateMauiApp() =>
		MauiApp.CreateBuilder()
			.UseMauiEmbeddedApp<App>()
			// After UseMauiEmbeddedApp, which is what registers MAUI's own handlers: registration is
			// last-one-wins, so replacing a handler only works from here.
			.UseUnoHandlers(HandlerMode)
			.ConfigureSyncfusionCharts()
			.Build();
}
