using Android.App;
using Android.Content.PM;
using Android.Views;

namespace Microsoft.Maui.Controls.Sample.Uno;

[Activity(
	ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
	Exported = true,
	MainLauncher = true,
	Theme = "@style/AppTheme",
	WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateHidden)]
public sealed class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
}
