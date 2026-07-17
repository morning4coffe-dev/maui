using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Controls.Sample.Uno;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp() =>
		MauiApp.CreateBuilder()
			.UseMauiApp<App>()
			.Build();
}
