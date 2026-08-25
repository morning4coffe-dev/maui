using System;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using PlatformBorder = Microsoft.UI.Xaml.Controls.Border;

namespace Maui.Controls.Sample.Uno;

/// <summary>
/// The Uno-owned UI. Everything here is ordinary Uno XAML content; the two <see cref="MauiHost"/> panels are
/// the only MAUI islands, and they are interleaved with Uno content to show that both trees compose.
/// </summary>
internal sealed class MainShell : UserControl
{
	readonly MauiEmbeddingSession _session;
	readonly MauiHost _firstHost;
	readonly MauiHost _secondHost;
	readonly PlatformBorder _secondHostContainer;
	readonly StackPanel _hostPanel;
	readonly TextBlock _diagnostics;

	int _replacementCount;

	public MainShell(MauiEmbeddingSession session)
	{
		_session = session ?? throw new ArgumentNullException(nameof(session));

		_firstHost = CreateHost();
		_secondHost = CreateHost();
		_secondHostContainer = CreateHostContainer(_secondHost);

		_diagnostics = new TextBlock
		{
			TextWrapping = TextWrapping.Wrap,
			Opacity = 0.8,
			Margin = new Thickness(0, 8, 0, 0),
		};

		var replaceButton = new Button { Content = "Replace content in island 1" };
		replaceButton.Click += OnReplaceClicked;

		var toggleButton = new Button { Content = "Detach island 2" };
		toggleButton.Click += OnToggleClicked;

		_hostPanel = new StackPanel
		{
			Spacing = 12,
			Children =
			{
				CreateHostContainer(_firstHost),
				new TextBlock
				{
					Text = "This line is Uno content sitting between two MAUI islands.",
					TextWrapping = TextWrapping.Wrap,
					Opacity = 0.8,
				},
				_secondHostContainer,
			},
		};

		var header = new StackPanel
		{
			Spacing = 8,
			Padding = new Thickness(16),
			Children =
			{
				new TextBlock
				{
					Text = "Uno application root",
					FontSize = 22,
				},
				new TextBlock
				{
					Text = "The chrome and buttons on this screen are Uno. The bordered panels are embedded MAUI.",
					TextWrapping = TextWrapping.Wrap,
					Opacity = 0.8,
				},
				new StackPanel
				{
					Orientation = Orientation.Horizontal,
					Spacing = 8,
					Children = { replaceButton, toggleButton },
				},
				_diagnostics,
			},
		};

		var root = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition { Height = GridLength.Auto },
				new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
			},
		};

		Grid.SetRow(header, 0);
		root.Children.Add(header);

		var scroller = new ScrollViewer
		{
			Padding = new Thickness(16, 0, 16, 16),
			Content = _hostPanel,
		};

		Grid.SetRow(scroller, 1);
		root.Children.Add(scroller);

		Content = root;

		// An Uno application root normally supplies its own themed background. TryGetValue rather than the
		// indexer: theme brushes are not guaranteed to be present as plain application resources.
		if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("ApplicationPageBackgroundThemeBrush", out var background) &&
			background is Brush backgroundBrush)
		{
			Background = backgroundBrush;
		}

		// Assigned only once the whole tree exists: setting MauiContent realizes it synchronously and
		// raises MauiContentRealized, which reads the fields initialized above.
		_firstHost.MauiContent = new MyMauiContent("First MAUI island");
		_secondHost.MauiContent = new MyMauiContent("Second MAUI island");

		UpdateDiagnostics();
	}

	MauiHost CreateHost()
	{
		var host = new MauiHost { Session = _session };
		host.MauiContentRealized += OnMauiContentRealized;
		return host;
	}

	static PlatformBorder CreateHostContainer(MauiHost host) =>
		new()
		{
			BorderBrush = new SolidColorBrush(Colors.SlateGray),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(8),
			Child = host,
		};

	void OnMauiContentRealized(object? sender, MauiContentRealizedEventArgs args) => UpdateDiagnostics();

	void OnReplaceClicked(object sender, RoutedEventArgs args)
	{
		_replacementCount++;

		// Exercises handler disconnection and logical-child removal for the previous content.
		_firstHost.MauiContent = new MyMauiContent(
			string.Format(CultureInfo.CurrentCulture, "First MAUI island (replaced {0}x)", _replacementCount));
	}

	void OnToggleClicked(object sender, RoutedEventArgs args)
	{
		// Detaching unloads the host. The window scope is owned by the session and must survive it, so
		// re-attaching has to keep working against the same embedded MAUI window.
		if (_hostPanel.Children.Contains(_secondHostContainer))
		{
			_hostPanel.Children.Remove(_secondHostContainer);
			((Button)sender).Content = "Re-attach island 2";
		}
		else
		{
			_hostPanel.Children.Add(_secondHostContainer);
			((Button)sender).Content = "Detach island 2";
		}

		UpdateDiagnostics();
	}

	void UpdateDiagnostics()
	{
		var firstWindow = _firstHost.MauiContent?.Window;
		var secondWindow = _secondHost.MauiContent?.Window;
		var sharesWindow = firstWindow is not null && ReferenceEquals(firstWindow, secondWindow);

		_diagnostics.Text = string.Format(
			CultureInfo.CurrentCulture,
			"Shared embedded MAUI window: {0}. Island 2 attached: {1}. Root theme: {2}. App theme: {3}.",
			sharesWindow ? "yes" : "no",
			_hostPanel.Children.Contains(_secondHostContainer) ? "yes" : "no",
			(Content as FrameworkElement)?.RequestedTheme,
			Microsoft.UI.Xaml.Application.Current?.RequestedTheme);
	}
}
