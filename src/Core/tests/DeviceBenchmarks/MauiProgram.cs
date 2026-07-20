using Microsoft.Maui.Hosting;
using Microsoft.Maui.TestUtils.DeviceTests.Runners;

namespace Microsoft.Maui.DeviceBenchmarks;

public static class MauiProgram
{
	const string NativeViewPropertyBatchingSwitch = "Microsoft.Maui.Experimental.NativeViewPropertyBatching";
	const string NativeViewPropertyBatchingEnvironmentVariable = "MAUI_NATIVE_VIEW_PROPERTY_BATCHING";
	const string NativeViewPropertyUpdateBatchingSwitch =
		"Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled";
	const string NativeViewPropertyUpdateBatchingEnvironmentVariable =
		"MAUI_NATIVE_VIEW_PROPERTY_UPDATE_BATCHING";

	public static MauiApp CreateMauiApp()
	{
#if IOS || MACCATALYST
		if (TryGetEnvironmentSwitch(NativeViewPropertyBatchingEnvironmentVariable, out bool isEnabled))
			AppContext.SetSwitch(NativeViewPropertyBatchingSwitch, isEnabled);
#endif
#if ANDROID
		if (TryGetEnvironmentSwitch(NativeViewPropertyUpdateBatchingEnvironmentVariable, out bool updateBatchingEnabled))
			AppContext.SetSwitch(NativeViewPropertyUpdateBatchingSwitch, updateBatchingEnabled);
#endif

		var appBuilder = MauiApp.CreateBuilder();

		appBuilder
			.ConfigureTests(new TestOptions
			{
				Assemblies =
				{
					typeof(MauiProgram).Assembly,
				},
			})
			.UseHeadlessRunner(new HeadlessRunnerOptions
			{
				RequiresUIContext = true,
			})
			.UseVisualRunner();

		return appBuilder.Build();
	}

	static bool TryGetEnvironmentSwitch(string name, out bool isEnabled)
	{
		var value = Environment.GetEnvironmentVariable(name);

		if (bool.TryParse(value, out isEnabled))
			return true;

		if (value == "1")
		{
			isEnabled = true;
			return true;
		}

		if (value == "0")
		{
			isEnabled = false;
			return true;
		}

		isEnabled = false;
		return false;
	}
}
