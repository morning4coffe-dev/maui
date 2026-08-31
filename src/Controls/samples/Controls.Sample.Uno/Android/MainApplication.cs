using Android.App;
using Android.Runtime;

namespace Microsoft.Maui.Controls.Sample.Uno;

[Application(
	Label = "MAUI on Uno",
	HardwareAccelerated = true,
	ExtractNativeLibs = true,
	Theme = "@style/AppTheme")]
public sealed class MainApplication : Microsoft.UI.Xaml.NativeApplication
{
	public MainApplication(IntPtr javaReference, JniHandleOwnership transfer)
		: base(() => new UnoMauiApplication(), javaReference, transfer)
	{
	}
}
