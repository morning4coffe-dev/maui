using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.DeviceBenchmarks;

public class ButtonConfigurationSteadyStateBenchmarkTests
{
	const int WarmupCount = 20;
	const int IterationCount = 100;
	const int TransactionsPerIteration = 100;
	const int ConnectionsPerIteration = 100;
	readonly ITestOutputHelper _output;

	public ButtonConfigurationSteadyStateBenchmarkTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	[Trait("Category", "Performance")]
	public async Task HandlerConnectCpu()
	{
		HandlerBenchmarkOutput.Writer = _output.WriteLine;

		try
		{
			HandlerBenchmarkOutput.WriteConnectCpuMetadata(
				WarmupCount,
				IterationCount,
				ConnectionsPerIteration);

			await RunConnectScenario(
				"ButtonLegacyConnectCpuNoImage",
				() => new ButtonHandler());
			await RunConnectScenario(
				"ButtonConfigurationConnectCpuNoImage",
				() => new ExperimentalConfigurationButtonHandler());
		}
		finally
		{
			HandlerBenchmarkOutput.Writer = null;
		}
	}

	[Fact]
	[Trait("Category", "Performance")]
	public async Task ConfigurationPropertyTransactions()
	{
		HandlerBenchmarkOutput.Writer = _output.WriteLine;

		try
		{
			HandlerBenchmarkOutput.WriteSteadyStateMetadata(
				WarmupCount,
				IterationCount,
				TransactionsPerIteration);

			await RunScenario(
				"ButtonLegacySingleProperty",
				() => new ButtonHandler(),
				RunSinglePropertyTransaction);
			await RunScenario(
				"ButtonConfigurationSingleProperty",
				() => new ExperimentalConfigurationButtonHandler(),
				RunSinglePropertyTransaction);
			await RunScenario(
				"ButtonLegacyFourProperties",
				() => new ButtonHandler(),
				RunFourPropertyTransaction);
			await RunScenario(
				"ButtonConfigurationFourProperties",
				() => new ExperimentalConfigurationButtonHandler(),
				RunFourPropertyTransaction);
			await RunScenario(
				"ButtonLegacyAllConfigurationProperties",
				() => new ButtonHandler(),
				RunAllPropertyTransaction);
			await RunScenario(
				"ButtonConfigurationAllProperties",
				() => new ExperimentalConfigurationButtonHandler(),
				RunAllPropertyTransaction);
		}
		finally
		{
			HandlerBenchmarkOutput.Writer = null;
		}
	}

	static async Task RunConnectScenario(
		string scenarioName,
		Func<IViewHandler> createHandler)
	{
		var scenario = new HandlerBenchmarkScenario(
			scenarioName,
			CreateButton,
			createHandler);
		var samples = await HandlerBenchmarkRunner.RunConnectCpuAsync(
			scenario,
			WarmupCount,
			IterationCount,
			ConnectionsPerIteration);

		foreach (var sample in samples)
			HandlerBenchmarkOutput.WriteSample(scenarioName, sample);

		HandlerBenchmarkOutput.WriteSummary(scenarioName, samples);
	}

	static async Task RunScenario(
		string scenarioName,
		Func<IViewHandler> createHandler,
		Action<View, int> runTransaction)
	{
		var result = await HandlerBenchmarkRunner.RunTransformSteadyStateAsync(
			scenarioName,
			CreateButton,
			createHandler,
			runTransaction,
			WarmupCount,
			IterationCount,
			TransactionsPerIteration);

		foreach (var sample in result.Samples)
			HandlerBenchmarkOutput.WriteSample(scenarioName, sample);

		HandlerBenchmarkOutput.WriteSummary(scenarioName, result.Samples);
		HandlerBenchmarkOutput.WriteDiagnostic(
			scenarioName,
			"configurationApplies",
			result.ConfigurationApplies);
		HandlerBenchmarkOutput.WriteDiagnostic(
			scenarioName,
			"configurationBatchFlushes",
			result.ConfigurationBatchFlushes);
	}

	static Button CreateButton() =>
		new()
		{
			BackgroundColor = Colors.Navy,
			BorderColor = Colors.Lime,
			BorderWidth = 3,
			CharacterSpacing = 3,
			CornerRadius = 9,
			FontSize = 19,
			Padding = new Thickness(20, 10, 40, 30),
			Text = "Configured button",
			TextColor = Colors.Orange,
		};

	static void RunSinglePropertyTransaction(View view, int transaction)
	{
		var button = (Button)view;
		button.BatchBegin();
		try
		{
			button.Text = (transaction & 1) == 0
				? "Configured button A"
				: "Configured button B";
		}
		finally
		{
			button.BatchCommit();
		}
	}

	static void RunFourPropertyTransaction(View view, int transaction)
	{
		var button = (Button)view;
		var alternate = (transaction & 1) != 0;

		button.BatchBegin();
		try
		{
			button.Text = alternate
				? "Configured button A"
				: "Configured button B";
			button.TextColor = alternate ? Colors.Orange : Colors.Purple;
			button.Padding = alternate
				? new Thickness(20, 10, 40, 30)
				: new Thickness(18, 12, 36, 24);
			button.BorderWidth = alternate ? 3 : 5;
		}
		finally
		{
			button.BatchCommit();
		}
	}

	static void RunAllPropertyTransaction(View view, int transaction)
	{
		var button = (Button)view;
		var alternate = (transaction & 1) != 0;

		button.BatchBegin();
		try
		{
			button.Text = alternate
				? "Configured button A"
				: "Configured button B";
			button.TextColor = alternate ? Colors.Orange : Colors.Purple;
			button.FontSize = alternate ? 19 : 21;
			button.CharacterSpacing = alternate ? 3 : 5;
			button.BackgroundColor = alternate ? Colors.Navy : Colors.Teal;
			button.Padding = alternate
				? new Thickness(20, 10, 40, 30)
				: new Thickness(18, 12, 36, 24);
			button.BorderColor = alternate ? Colors.Lime : Colors.Yellow;
			button.BorderWidth = alternate ? 3 : 5;
			button.CornerRadius = alternate ? 9 : 13;
		}
		finally
		{
			button.BatchCommit();
		}
	}
}
