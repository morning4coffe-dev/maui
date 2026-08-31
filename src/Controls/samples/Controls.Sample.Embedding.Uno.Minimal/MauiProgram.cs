using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Hosting;

namespace Maui.Controls.Sample.Uno.Minimal;

/// <summary>The embedded MAUI application.</summary>
/// <remarks>
/// Embedding never calls <c>CreateWindow</c>. This type exists so application-scoped MAUI state, resources
/// in particular, resolves through the logical tree for the embedded content.
/// </remarks>
public sealed class EmbeddedMauiApp : Microsoft.Maui.Controls.Application
{
}

public static class MauiProgram
{
	public static MauiApp CreateMauiApp() =>
		MauiApp.CreateBuilder()
			.UseMauiEmbeddedApp<EmbeddedMauiApp>()
			.Build();
}
