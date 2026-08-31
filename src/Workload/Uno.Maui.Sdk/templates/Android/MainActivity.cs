using Android.App;
using Android.Content.Res;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui;

namespace Uno.Maui.Generated;

[Activity(
	ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
	Exported = true,
	MainLauncher = true,
	Theme = "@style/AppTheme",
	WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateHidden)]
public sealed class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		global::Uno.UI.FeatureConfiguration.AndroidSettings.IsEdgeToEdgeEnabled = false;
		var window = Window ?? throw new InvalidOperationException("The Uno Android host window was not created.");
		WindowCompat.SetDecorFitsSystemWindows(window, true);
		base.OnCreate(savedInstanceState);

		ApplySystemBarAppearance();
	}

	protected override void OnResume()
	{
		base.OnResume();
		ApplySystemBarAppearance();
	}

	public override void OnConfigurationChanged(Configuration newConfig)
	{
		base.OnConfigurationChanged(newConfig);
		var decorView = Window?.DecorView;
		if (decorView is not null)
		{
			decorView.Post(() =>
			{
				if (IPlatformApplication.Current is IPlatformApplication platformApplication)
				{
					platformApplication.Application?.ThemeChanged();
				}
			});
		}

		ApplySystemBarAppearance();
	}

	void ApplySystemBarAppearance()
	{
		var window = Window;
		var decorView = window?.DecorView;
		if (window is null || decorView is null)
		{
			return;
		}

		var fallbackToLightBars =
			(Resources?.Configuration?.UiMode & UiMode.NightMask) != UiMode.NightYes;
		var insetsController = WindowCompat.GetInsetsController(window, decorView);
		if (insetsController is not null)
		{
			insetsController.AppearanceLightStatusBars = ResolveThemeBoolean(
				global::Android.Resource.Attribute.WindowLightStatusBar,
				fallbackToLightBars);
			if (OperatingSystem.IsAndroidVersionAtLeast(27))
			{
				insetsController.AppearanceLightNavigationBars = ResolveThemeBoolean(
					global::Android.Resource.Attribute.WindowLightNavigationBar,
					fallbackToLightBars);
			}
		}
	}

	bool ResolveThemeBoolean(int attribute, bool fallback)
	{
		var value = new TypedValue();
		return Theme?.ResolveAttribute(attribute, value, true) == true
			? value.Data != 0
			: fallback;
	}

}
