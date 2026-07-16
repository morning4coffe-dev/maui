namespace Microsoft.Maui.Controls.Sample.Uno;

public sealed class MainPage : ContentPage
{
	public MainPage()
	{
		Title = "MAUI on Uno";

		var count = 0;
		var status = new Label
		{
			Text = "Rendered by MAUI's WinUI handlers on Uno",
			FontSize = 20,
		};

		var entry = new Entry
		{
			AutomationId = "MauiEntry",
			Placeholder = "Type into a MAUI Entry",
		};

		var button = new Button
		{
			AutomationId = "MauiButton",
			Text = "Click me",
		};

		var formattedLabel = new Label
		{
			FormattedText = new FormattedString
			{
				Spans =
				{
					new Span { Text = "Formatted ", TextColor = Microsoft.Maui.Graphics.Colors.DarkBlue },
					new Span { Text = "MAUI text", FontAttributes = FontAttributes.Bold },
				},
			},
		};

		var fontImage = new Image
		{
			AutomationId = "MauiFontImage",
			HeightRequest = 32,
			HorizontalOptions = LayoutOptions.Start,
			Source = new FontImageSource
			{
				Glyph = "★",
				FontFamily = "Segoe UI Symbol",
				Size = 32,
				Color = Microsoft.Maui.Graphics.Colors.DarkBlue,
			},
			WidthRequest = 32,
		};

		button.Clicked += (_, _) =>
		{
			count++;
			status.Text = $"Clicked {count} time{(count == 1 ? string.Empty : "s")}. Entry: {entry.Text}";
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(32),
				Spacing = 16,
				Children =
				{
					new Label
					{
						Text = ".NET MAUI WinUI target running on Uno Platform",
						FontSize = 28,
						FontAttributes = FontAttributes.Bold,
					},
					status,
					formattedLabel,
					fontImage,
					entry,
					button,
					new Slider
					{
						AutomationId = "MauiSlider",
						Minimum = 0,
						Maximum = 100,
						Value = 40,
					},
					new ProgressBar
					{
						Progress = 0.4,
					},
				},
			},
		};
	}
}
