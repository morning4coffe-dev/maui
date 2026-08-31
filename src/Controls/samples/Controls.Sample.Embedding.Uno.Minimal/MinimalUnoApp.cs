using Microsoft.Maui.Controls.Embedding.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using PlatformApplication = Microsoft.UI.Xaml.Application;
using PlatformWindow = Microsoft.UI.Xaml.Window;

namespace Maui.Controls.Sample.Uno.Minimal;

/// <summary>
/// A plain Uno application that hosts one embedded MAUI island.
/// </summary>
/// <remarks>
/// <para>
/// This is an ordinary <see cref="PlatformApplication"/>, not a <c>MauiWinUIApplication</c>: Uno owns the
/// application, the window and the visual tree, and MAUI is a guest inside it. There are only four moving
/// parts, marked below.
/// </para>
/// </remarks>
public sealed class MinimalUnoApp : PlatformApplication
{
	PlatformWindow? _window;
	MauiEmbeddingSession? _session;

	public MinimalUnoApp()
	{
		Resources.MergedDictionaries.Add(new XamlControlsResources());

		UnhandledException += (_, args) => Trace("Application.UnhandledException", args.Exception);

		// (1) Say how the embedded MauiApp is built. Registering is cheap; the MauiApp itself is built
		// lazily on the UI thread when the first island is realized.
		MauiEmbeddingSession.UseMauiApp(MauiProgram.CreateMauiApp);
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		_window = new PlatformWindow { Title = "Minimal MAUI embedding on Uno" };

		// (2) One session per Uno window. It owns the MAUI window scope that alerts and navigation need.
		_session = MauiEmbeddingSession.GetOrCreate(_window);

		// Embedding faults are easy to miss on platforms with no attached debugger: an exception thrown
		// here would otherwise leave a blank window, so it is shown in the window instead.
		try
		{
			_window.Content = BuildContent();
		}
		catch (Exception ex)
		{
			Trace("OnLaunched", ex);
			_window.Content = new TextBlock
			{
				Text = $"Embedding failed: {ex}",
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(20),
			};
		}

		// (4) MAUI's window-scoped services wait on real activation, so it is relayed from the native
		// window rather than raised while the content is still being constructed.
		_window.Activated += OnWindowActivated;
		_window.Closed += OnWindowClosed;
		_window.Activate();
	}

	void OnWindowActivated(object sender, WindowActivatedEventArgs args)
	{
		if (args.WindowActivationState == global::Windows.UI.Core.CoreWindowActivationState.Deactivated)
		{
			_session?.NotifyWindowDeactivated();
		}
		else
		{
			_session?.NotifyWindowActivated();
		}
	}

	void OnWindowClosed(object sender, WindowEventArgs args)
	{
		if (_window is not null)
		{
			_window.Activated -= OnWindowActivated;
			_window.Closed -= OnWindowClosed;
		}

		_session?.Dispose();
		_session = null;
	}

	UIElement BuildContent()
	{
		var host = new MauiHost
		{
			Session = _session,
			// (3) Hand it a MAUI element. From here down it is an ordinary Uno visual tree.
			MauiContent = new MinimalMauiContent(),
		};

		var panel = new StackPanel
		{
			Padding = new Thickness(20),
			Spacing = 12,
			Children =
			{
				new TextBlock { Text = "Uno application root", FontSize = 22 },
				new TextBlock
				{
					Text = "Everything outside the border is Uno. The bordered panel is embedded MAUI.",
					TextWrapping = TextWrapping.Wrap,
					Opacity = 0.8,
				},
				new Border
				{
					BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.SlateGray),
					BorderThickness = new Thickness(1),
					CornerRadius = new CornerRadius(8),
					Child = host,
				},
			},
		};

		// A UserControl, not a bare panel, so the whole island sits under one themed root. Uno resolves a
		// plain TextBlock's default foreground from the application theme only once it inherits one, so
		// both brushes are applied here explicitly. TryGetValue rather than the indexer: theme brushes are
		// not guaranteed to be present as plain application resources.
		var root = new UserControl { Content = panel };

		if (Resources.TryGetValue("ApplicationPageBackgroundThemeBrush", out var background) &&
			background is Brush backgroundBrush)
		{
			panel.Background = backgroundBrush;
		}

		if ((Resources.TryGetValue("TextFillColorPrimaryBrush", out var foreground) ||
			Resources.TryGetValue("DefaultTextForegroundThemeBrush", out foreground)) &&
			foreground is Brush foregroundBrush)
		{
			root.Foreground = foregroundBrush;
		}

		root.Loaded += (_, _) => Console.WriteLine(
			$"MINIMAL-EMBEDDING ready: root={root.ActualWidth}x{root.ActualHeight}, mauiHost={host.ActualWidth}x{host.ActualHeight}");

		return root;
	}

	static void Trace(string origin, Exception? exception) =>
		Console.WriteLine($"MINIMAL-EMBEDDING {origin}: {exception}");
}
