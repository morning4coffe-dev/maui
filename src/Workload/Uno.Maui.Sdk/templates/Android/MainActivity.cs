using Android.App;
using Android.Content.Res;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using AndroidX.Core.Graphics;
using AndroidX.Core.View;
using AView = Android.Views.View;

namespace Uno.Maui.Generated;

[Activity(
	ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
	Exported = true,
	MainLauncher = true,
	Theme = "@style/AppTheme",
	WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateHidden)]
public sealed class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
	AView? _hostRoot;
	SystemBarInsetsListener? _systemBarInsetsListener;

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		var window = Window ?? throw new InvalidOperationException("The Uno Android host window was not created.");
		WindowCompat.SetDecorFitsSystemWindows(
			window,
			Build.VERSION.SdkInt < BuildVersionCodes.VanillaIceCream);
		base.OnCreate(savedInstanceState);

		ApplySystemBarAppearance();

		if (Build.VERSION.SdkInt >= BuildVersionCodes.VanillaIceCream)
		{
			_hostRoot = FindViewById<AView>(global::Android.Resource.Id.Content)
				?? throw new InvalidOperationException("The Uno Android host content view was not created.");
			_systemBarInsetsListener = new SystemBarInsetsListener(_hostRoot);
			ViewCompat.SetOnApplyWindowInsetsListener(_hostRoot, _systemBarInsetsListener);
			ViewCompat.RequestApplyInsets(_hostRoot);
		}
	}

	protected override void OnResume()
	{
		base.OnResume();
		ApplySystemBarAppearance();
	}

	public override void OnConfigurationChanged(Configuration newConfig)
	{
		base.OnConfigurationChanged(newConfig);
		ApplySystemBarAppearance();
	}

	protected override void OnDestroy()
	{
		if (_hostRoot is not null)
		{
			ViewCompat.SetOnApplyWindowInsetsListener(_hostRoot, null);
			_systemBarInsetsListener?.RestorePadding(_hostRoot);
			_hostRoot = null;
		}

		_systemBarInsetsListener?.Dispose();
		_systemBarInsetsListener = null;
		base.OnDestroy();
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

	sealed class SystemBarInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
	{
		readonly int _left;
		readonly int _top;
		readonly int _right;
		readonly int _bottom;

		public SystemBarInsetsListener(AView hostRoot)
		{
			_left = hostRoot.PaddingLeft;
			_top = hostRoot.PaddingTop;
			_right = hostRoot.PaddingRight;
			_bottom = hostRoot.PaddingBottom;
		}

		public WindowInsetsCompat? OnApplyWindowInsets(AView? view, WindowInsetsCompat? insets)
		{
			if (view is null || insets is null)
			{
				return insets;
			}

			var systemBars = insets.GetInsets(
				WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
			var ime = insets.GetInsets(WindowInsetsCompat.Type.Ime());
			view.SetPadding(
				_left + (systemBars?.Left ?? 0),
				_top + (systemBars?.Top ?? 0),
				_right + (systemBars?.Right ?? 0),
				_bottom + Math.Max(systemBars?.Bottom ?? 0, ime?.Bottom ?? 0));

			var remainingInsets = new WindowInsetsCompat.Builder(insets);
			remainingInsets.SetInsets(WindowInsetsCompat.Type.SystemBars(), Insets.None);
			remainingInsets.SetInsets(WindowInsetsCompat.Type.DisplayCutout(), Insets.None);
			remainingInsets.SetInsets(WindowInsetsCompat.Type.Ime(), Insets.None);
			return remainingInsets.Build();
		}

		public void RestorePadding(AView hostRoot) =>
			hostRoot.SetPadding(_left, _top, _right, _bottom);
	}
}
