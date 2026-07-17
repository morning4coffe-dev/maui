using Microsoft.Maui.Hosting;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.Maui.Controls.Sample.Uno;

public sealed class UnoMauiApplication : MauiWinUIApplication
{
	public UnoMauiApplication()
	{
		Resources.MergedDictionaries.Add(new XamlControlsResources());
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
