using Microsoft.UI.Xaml.Controls;

using PlatformApplication = Microsoft.UI.Xaml.Application;
using PlatformWindow = Microsoft.UI.Xaml.Window;

namespace Maui.Controls.Sample.Uno;

/// <summary>
/// The Uno application root. This is a plain <see cref="PlatformApplication"/>, not a
/// <c>MauiWinUIApplication</c>: Uno owns the application, the window, and the visual tree, and MAUI is a
/// guest inside it.
/// </summary>
public sealed class UnoEmbeddingApplication : PlatformApplication
{
	PlatformWindow? _window;
	MauiEmbeddingSession? _session;

	public UnoEmbeddingApplication() => Resources.MergedDictionaries.Add(new XamlControlsResources());

	protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
	{
		_window = new PlatformWindow { Title = "MAUI embedding on Uno" };

		// Created here, on the UI thread, and only once Application.Current exists: MAUI's embedding
		// bootstrap captures Application.Current while the MauiApp builder is being configured, so this
		// cannot move up into Program.Main.
		_session = MauiEmbeddingSession.GetOrCreate(_window);

		_window.Content = new MainShell(_session);
		_window.Closed += OnWindowClosed;
		_window.Activate();
	}

	void OnWindowClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
	{
		// Destroys the embedded MAUI window exactly once, which disposes the window service scope.
		_session?.Dispose();
		_session = null;
	}
}
