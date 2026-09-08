using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.UI.Xaml.Controls;
using Uno.Foundation.Extensibility;
using Uno.UI.Lottie;

namespace Uno.Maui.Generated;

public sealed class UnoMauiApplication : MauiWinUIApplication
{
	public UnoMauiApplication()
	{
		ApiExtensibility.Register(typeof(ILottieVisualSourceProvider), owner => new LottieVisualSourceProvider(owner));
		Resources.MergedDictionaries.Add(new XamlControlsResources());
	}

	protected override MauiApp CreateMauiApp() => AppFactory.Create();
}
