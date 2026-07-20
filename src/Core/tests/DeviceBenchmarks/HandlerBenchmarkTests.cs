using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Xunit;

namespace Microsoft.Maui.DeviceBenchmarks;

public class HandlerBenchmarkTests
{
	const int WarmupCount = 20;
	const int IterationCount = 100;

	[Fact]
	[Trait("Category", "Performance")]
	public async Task HandlerConnectToFirstLayout()
	{
		HandlerBenchmarkOutput.WriteMetadata(WarmupCount, IterationCount);

		foreach (var scenario in CreateScenarios())
			await HandlerBenchmarkRunner.RunAsync(scenario, WarmupCount, IterationCount);
	}

	static IEnumerable<HandlerBenchmarkScenario> CreateScenarios()
	{
		yield return new(
			"ContentViewBaseProperties",
			() => ConfigureBaseProperties(new ContentView()),
			() => new ContentViewHandler());

		yield return new(
			"BorderBaseProperties",
			() => ConfigureBaseProperties(new Border()),
			() => new BorderHandler());
	}

	static TView ConfigureBaseProperties<TView>(TView view)
		where TView : View
	{
		view.FlowDirection = FlowDirection.RightToLeft;
		view.MinimumHeightRequest = 24;
		view.MinimumWidthRequest = 32;
		view.IsEnabled = false;
		view.Opacity = 0.73;
		view.TranslationX = 3;
		view.TranslationY = 4;
		view.Scale = 0.99;
		view.ScaleX = 0.98;
		view.ScaleY = 0.97;
		view.Rotation = 1.5;
		view.RotationX = 2.5;
		view.RotationY = 3.5;
		view.AnchorX = 0.4;
		view.AnchorY = 0.6;

		return view;
	}
}
