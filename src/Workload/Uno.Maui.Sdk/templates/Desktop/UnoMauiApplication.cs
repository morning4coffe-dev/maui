using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Uno.Maui.Generated;

public sealed class UnoMauiApplication : MauiWinUIApplication
{
	FrameworkElement? _themeRoot;

	public UnoMauiApplication()
	{
		Resources.MergedDictionaries.Add(new XamlControlsResources());
	}

	protected override MauiApp CreateMauiApp() => AppFactory.Create();

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		base.OnLaunched(args);

		if (_themeRoot is not null)
		{
			_themeRoot.ActualThemeChanged -= OnActualThemeChanged;
			_themeRoot = null;
		}

		foreach (var window in Microsoft.Maui.Controls.Application.Current!.Windows)
		{
			if (window.Handler?.PlatformView is Window { Content: FrameworkElement root })
			{
				_themeRoot = root;
				_themeRoot.ActualThemeChanged += OnActualThemeChanged;
				break;
			}
		}
	}

	void OnActualThemeChanged(FrameworkElement sender, object args)
	{
		if (IPlatformApplication.Current is IPlatformApplication platformApplication)
		{
			platformApplication.Application?.ThemeChanged();
		}
	}
}
