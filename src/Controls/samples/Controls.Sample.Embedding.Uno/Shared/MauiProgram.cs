using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Hosting;

namespace Maui.Controls.Sample.Uno;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp() =>
		MauiApp.CreateBuilder()
			.UseMauiEmbeddedApp<App>()
			.ConfigureMauiHandlers(handlers => handlers.AddHandler<LifecycleRegressionProbe.FailingEmbeddingView,
				LifecycleRegressionProbe.FailingEmbeddingViewHandler>())
			.ConfigureImageSources(sources => sources.AddService<ImageDensityRegressionProbe.DensitySource,
				ImageDensityRegressionProbe.DensityService>())
			.Build();
}
