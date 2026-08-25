using System.Globalization;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Maui.Controls.Sample.Uno;

/// <summary>
/// MAUI content designed to prove that embedded MAUI renders, lays out, themes, resolves resources, and
/// handles input while hosted inside an Uno visual tree.
/// </summary>
public sealed class MyMauiContent : ContentView
{
	readonly Label _counterLabel;
	readonly Label _echoLabel;
	readonly Label _sliderLabel;
	int _clickCount;

	public MyMauiContent(string title)
	{
		var heading = new Label { Text = title };

		// Resolved through the logical tree: content -> EmbeddedWindow -> Application.Resources.
		heading.SetDynamicResource(VisualElement.StyleProperty, "EmbeddedHeadline");

		_counterLabel = new Label { Text = "Clicked 0 times" };

		var counterButton = new Button { Text = "Click me" };
		counterButton.Clicked += OnCounterClicked;

		var entry = new Entry { Placeholder = "Type here to prove text input" };
		entry.TextChanged += OnEntryTextChanged;

		_echoLabel = new Label { Text = "You typed: (nothing yet)" };

		var slider = new Slider { Minimum = 0, Maximum = 100, Value = 25 };
		var progress = new ProgressBar { Progress = 0.25 };
		_sliderLabel = new Label { Text = FormatSlider(25) };
		slider.ValueChanged += (_, args) =>
		{
			progress.Progress = args.NewValue / 100d;
			_sliderLabel.Text = FormatSlider(args.NewValue);
		};

		// Exercises the composition clip path that the Uno target implements through RectangleClip.
		var border = new Border
		{
			Stroke = new SolidColorBrush(Color.FromArgb("#512BD4")),
			StrokeThickness = 2,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12, 12, 4, 4) },
			Padding = new Thickness(12),
			Content = new Label { Text = "Border with independent corner radii" },
		};

		Content = new VerticalStackLayout
		{
			Spacing = 10,
			Padding = new Thickness(16),
			Children =
			{
				heading,
				_counterLabel,
				counterButton,
				entry,
				_echoLabel,
				slider,
				progress,
				_sliderLabel,
				border,
			},
		};
	}

	void OnCounterClicked(object? sender, System.EventArgs args)
	{
		_clickCount++;
		_counterLabel.Text = _clickCount == 1 ? "Clicked 1 time" : $"Clicked {_clickCount} times";
	}

	void OnEntryTextChanged(object? sender, TextChangedEventArgs args) =>
		_echoLabel.Text = string.IsNullOrEmpty(args.NewTextValue)
			? "You typed: (nothing yet)"
			: $"You typed: {args.NewTextValue}";

	static string FormatSlider(double value) =>
		string.Format(CultureInfo.CurrentCulture, "Slider value: {0:0}", value);
}
