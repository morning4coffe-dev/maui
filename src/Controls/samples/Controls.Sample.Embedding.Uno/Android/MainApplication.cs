using Android.App;
using Android.Runtime;

namespace Maui.Controls.Sample.Uno;

[Application(
	Label = "MAUI embedded in Uno",
	HardwareAccelerated = true,
	LargeHeap = true,
	Theme = "@style/AppTheme")]
public sealed class MainApplication : Microsoft.UI.Xaml.NativeApplication
{
	public MainApplication(IntPtr javaReference, JniHandleOwnership transfer)
		: base(() => new UnoEmbeddingApplication(), javaReference, transfer)
	{
#if TIER2_PROBE
		// Android has no Main to configure the environment from, and the Uno application is constructed by
		// the factory above, so the probe switch is applied before that factory can run.
		Environment.SetEnvironmentVariable(Tier2Probe.EnableVariable, "1");
#endif
	}
}
