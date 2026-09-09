using System.Globalization;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Maui.Controls.Sample.Uno.Minimal;

/// <summary>The MAUI content this app embeds: an ordinary MAUI view, written the ordinary way.</summary>
/// <remarks>
/// Nothing here is Uno-aware. That is the point of the sample — the MAUI code is unchanged, and the only
/// thing that differs from a normal MAUI app is who owns the window.
/// </remarks>
public sealed class MinimalMauiContent : ContentView
{
	readonly Label _counter;
	int _count;

	public MinimalMauiContent()
	{
		_counter = new Label { Text = "Clicked 0 times", FontSize = 16 };

		var button = new Button { Text = "Click me" };
		button.Clicked += OnClicked;

		var entry = new Entry { Placeholder = "Type here" };
		var echo = new Label { FontSize = 13, Opacity = 0.75, Text = "You typed: (nothing yet)" };
		entry.TextChanged += (_, args) =>
			echo.Text = string.IsNullOrEmpty(args.NewTextValue)
				? "You typed: (nothing yet)"
				: $"You typed: {args.NewTextValue}";

		Content = new VerticalStackLayout
		{
			Spacing = 10,
			Padding = new Thickness(16),
			Children =
			{
				new Label
				{
					Text = "This panel is .NET MAUI",
					FontSize = 20,
					FontAttributes = FontAttributes.Bold,
					TextColor = Color.FromArgb("#512BD4"),
				},
				_counter,
				button,
				entry,
				echo,
			},
		};
	}

	void OnClicked(object? sender, EventArgs args)
	{
		_count++;
		_counter.Text = string.Format(CultureInfo.CurrentCulture, "Clicked {0} times", _count);
	}
}
