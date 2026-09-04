using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.UI.Xaml.Controls;

namespace Uno.Maui.Generated;

public sealed class UnoMauiApplication : MauiWinUIApplication
{
	public UnoMauiApplication()
	{
		Resources.MergedDictionaries.Add(new XamlControlsResources());
	}

	protected override MauiApp CreateMauiApp()
	{
		var app = AppFactory.Create();
		Resources["ContentControlThemeFontFamily"] = new Microsoft.UI.Xaml.Media.FontFamily("Arial");
		return app;
	}
}
