using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Xunit;

namespace Microsoft.Maui.DeviceBenchmarks;

public class HandlerSteadyStateBenchmarkTests
{
	const int WarmupCount = 20;
	const int IterationCount = 100;
	const int TransactionsPerIteration = 100;

	[Fact]
	[Trait("Category", "Performance")]
	// Run once with MAUI_NATIVE_VIEW_PROPERTY_UPDATE_BATCHING=0 and once with it set to 1.
	public async Task ExplicitSteadyStatePropertyTransactions()
	{
		HandlerBenchmarkOutput.WriteSteadyStateMetadata(
			WarmupCount,
			IterationCount,
			TransactionsPerIteration);

		await HandlerBenchmarkRunner.RunSteadyStateAsync(
			"ContentViewSteadyStateProperties",
			() => new ContentView(),
			() => new ContentViewHandler(),
			RunTransaction,
			WarmupCount,
			IterationCount,
			TransactionsPerIteration);
	}

	static void RunTransaction(View view, int transaction)
	{
		var alternate = (transaction & 1) != 0;

		view.BatchBegin();
		view.BatchBegin();
		try
		{
			view.IsEnabled = alternate;
			view.Opacity = alternate ? 0.65 : 0.85;
			view.TranslationX = alternate ? 3 : 5;
			view.TranslationY = alternate ? 7 : 11;
			view.Scale = alternate ? 0.96 : 0.98;
			view.ScaleX = alternate ? 0.92 : 0.94;
			view.ScaleY = alternate ? 0.90 : 0.93;
			view.Rotation = alternate ? 1 : 2;
			view.RotationX = alternate ? 3 : 4;
			view.RotationY = alternate ? 5 : 6;
		}
		finally
		{
			view.BatchCommit();
			view.BatchCommit();
		}
	}
}
