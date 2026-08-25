using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Hosting;

namespace Maui.Controls.Sample.Uno;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp() =>
		MauiApp.CreateBuilder()
			.UseMauiEmbeddedApp<App>()
			.Build();
}
