using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.DeviceBenchmarks;

public class HandlerBenchmarkTests
{
	const int WarmupCount = 20;
	const int IterationCount = 100;
	readonly ITestOutputHelper _output;

	public HandlerBenchmarkTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	[Trait("Category", "Performance")]
	public async Task HandlerConnectToFirstLayout()
	{
		HandlerBenchmarkOutput.Writer = _output.WriteLine;

		try
		{
			HandlerBenchmarkOutput.WriteMetadata(WarmupCount, IterationCount);

			foreach (var scenario in CreateScenarios())
				await HandlerBenchmarkRunner.RunAsync(scenario, WarmupCount, IterationCount);
		}
		finally
		{
			HandlerBenchmarkOutput.Writer = null;
		}
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

#if IOS || MACCATALYST
		yield return new(
			"ContentViewAppleBatchedProperties",
			() => ConfigureAppleBatchedProperties(new ContentView()),
			() => new ContentViewHandler());

		yield return new(
			"BorderAppleBatchedProperties",
			() => ConfigureAppleBatchedProperties(new Border()),
			() => new BorderHandler());

		yield return new(
			"ButtonLegacyConfigurationProperties",
			() => ConfigureButtonProperties(new Button()),
			() => new ButtonHandler());

		yield return new(
			"ButtonModernConfigurationProperties",
			() => ConfigureButtonProperties(new Button()),
			() => new ExperimentalConfigurationButtonHandler());
#endif
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

#if IOS || MACCATALYST
	static TView ConfigureAppleBatchedProperties<TView>(TView view)
		where TView : View
	{
		view.FlowDirection = FlowDirection.RightToLeft;
		view.IsEnabled = false;
		view.IsVisible = false;
		view.Opacity = 0.73;

		return view;
	}

	static Button ConfigureButtonProperties(Button button)
	{
		button.BackgroundColor = Colors.Navy;
		button.BorderColor = Colors.Lime;
		button.BorderWidth = 3;
		button.CharacterSpacing = 3;
		button.CornerRadius = 9;
		button.FlowDirection = FlowDirection.RightToLeft;
		button.FontSize = 19;
		button.Padding = new Thickness(20, 10, 40, 30);
		button.Text = "Configured button";
		button.TextColor = Colors.Orange;

		return button;
	}
#endif
}
