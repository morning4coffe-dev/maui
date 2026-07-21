using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.DeviceBenchmarks;

public class HandlerTransformSteadyStateBenchmarkTests
{
	const string NativeViewPropertyUpdateBatchingSwitch =
		"Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled";
	const int WarmupCount = 20;
	const int IterationCount = 100;
	const int TransactionsPerIteration = 100;
	readonly ITestOutputHelper _output;

	public HandlerTransformSteadyStateBenchmarkTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	[Trait("Category", "Performance")]
	public async Task ExplicitCompoundTransformTransactions()
	{
		HandlerBenchmarkOutput.Writer = _output.WriteLine;

		try
		{
			HandlerBenchmarkOutput.WriteSteadyStateMetadata(
				WarmupCount,
				IterationCount,
				TransactionsPerIteration);

			await RunScenario(
				"ContentViewSingleTransformBatch",
				RunSinglePropertyTransaction);
			await RunScenario(
				"ContentViewTranslateTransformBatch",
				RunTranslateTransaction);
			await RunScenario(
				"ContentViewCompositeTransformBatch",
				RunCompositeTransaction);
			await RunScenario(
				"ContentViewAllTransformPropertiesBatch",
				RunAllPropertiesTransaction);
		}
		finally
		{
			HandlerBenchmarkOutput.Writer = null;
		}
	}

	static Task RunScenario(
		string scenarioName,
		Action<View, int> runTransaction) =>
		RunScenarioCore(scenarioName, runTransaction);

	static async Task RunScenarioCore(
		string scenarioName,
		Action<View, int> runTransaction)
	{
		var result = await HandlerBenchmarkRunner.RunTransformSteadyStateAsync(
				scenarioName,
				() => new ContentView(),
				() => new ContentViewHandler(),
				runTransaction,
				WarmupCount,
				IterationCount,
				TransactionsPerIteration);

		foreach (var sample in result.Samples)
			HandlerBenchmarkOutput.WriteSample(scenarioName, sample);

		HandlerBenchmarkOutput.WriteSummary(scenarioName, result.Samples);
		HandlerBenchmarkOutput.WriteDiagnostic(
			scenarioName,
			"batchFlushes",
			result.BatchFlushes);

		var batchingEnabled =
			AppContext.TryGetSwitch(NativeViewPropertyUpdateBatchingSwitch, out bool isEnabled) &&
			isEnabled;
		Assert.Equal(
			batchingEnabled ? IterationCount * TransactionsPerIteration : 0,
			result.BatchFlushes);
	}

	static void RunSinglePropertyTransaction(View view, int transaction)
	{
		var alternate = (transaction & 1) != 0;

		view.BatchBegin();
		try
		{
			view.Scale = alternate ? 0.96 : 0.98;
		}
		finally
		{
			view.BatchCommit();
		}
	}

	static void RunTranslateTransaction(View view, int transaction)
	{
		var alternate = (transaction & 1) != 0;

		view.BatchBegin();
		try
		{
			view.TranslationX = alternate ? 3 : 5;
			view.TranslationY = alternate ? 7 : 11;
		}
		finally
		{
			view.BatchCommit();
		}
	}

	static void RunCompositeTransaction(View view, int transaction)
	{
		var alternate = (transaction & 1) != 0;

		view.BatchBegin();
		try
		{
			view.TranslationX = alternate ? 3 : 5;
			view.TranslationY = alternate ? 7 : 11;
			view.Scale = alternate ? 0.96 : 0.98;
			view.Rotation = alternate ? 1 : 2;
		}
		finally
		{
			view.BatchCommit();
		}
	}

	static void RunAllPropertiesTransaction(View view, int transaction)
	{
		var alternate = (transaction & 1) != 0;

		view.BatchBegin();
		view.BatchBegin();
		try
		{
			view.TranslationX = alternate ? 3 : 5;
			view.TranslationY = alternate ? 7 : 11;
			view.Scale = alternate ? 0.96 : 0.98;
			view.ScaleX = alternate ? 0.92 : 0.94;
			view.ScaleY = alternate ? 0.90 : 0.93;
			view.Rotation = alternate ? 1 : 2;
			view.RotationX = alternate ? 3 : 4;
			view.RotationY = alternate ? 5 : 6;
			view.AnchorX = alternate ? 0.45 : 0.55;
			view.AnchorY = alternate ? 0.40 : 0.60;
		}
		finally
		{
			view.BatchCommit();
			view.BatchCommit();
		}
	}
}
