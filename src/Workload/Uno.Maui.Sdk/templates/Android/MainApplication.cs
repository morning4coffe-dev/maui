using Android.App;
using Android.Runtime;

namespace Uno.Maui.Generated;

[Application(
	Label = "MAUI on Uno",
	HardwareAccelerated = true,
#if UNO_MAUI_ANDROID_EXTRACT_NATIVE_LIBS
	ExtractNativeLibs = true,
#else
	ExtractNativeLibs = false,
#endif
#if UNO_MAUI_ANDROID_LARGE_HEAP
	LargeHeap = true,
#endif
	Theme = "@style/AppTheme")]
public sealed class MainApplication : Microsoft.UI.Xaml.NativeApplication
{
	public MainApplication(IntPtr javaReference, JniHandleOwnership transfer)
		: base(() => new UnoMauiApplication(), javaReference, transfer)
	{
	}
}
