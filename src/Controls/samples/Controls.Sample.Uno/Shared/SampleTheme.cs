using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Controls.Sample.Uno;

internal static class SampleTheme
{
	internal static readonly Color LightBackground = Color.FromArgb("#FAFAFA");
	internal static readonly Color DarkBackground = Color.FromArgb("#121212");
	internal static readonly Color LightForeground = Color.FromArgb("#1B1B1B");
	internal static readonly Color DarkForeground = Color.FromArgb("#F5F5F5");
	internal static readonly Color DarkSurface = Color.FromArgb("#242424");
	internal static readonly Color LightAccent = Color.FromArgb("#173EAE");
	internal static readonly Color DarkAccent = Color.FromArgb("#9EC5FF");

	internal static void ApplyTo(IVisualTreeElement element)
	{
		switch (element)
		{
			case ContentPage page:
				page.SetAppThemeColor(VisualElement.BackgroundColorProperty, LightBackground, DarkBackground);
				break;
			case Label { TextColor: null } label:
				label.SetAppThemeColor(Label.TextColorProperty, LightForeground, DarkForeground);
				break;
			case Button button:
				button.SetAppThemeColor(Button.TextColorProperty, LightForeground, DarkForeground);
				button.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, DarkSurface);
				break;
			case Entry entry:
				entry.SetAppThemeColor(Entry.TextColorProperty, LightForeground, DarkForeground);
				entry.SetAppThemeColor(Entry.PlaceholderColorProperty, Color.FromArgb("#595959"), Color.FromArgb("#BABABA"));
				entry.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, DarkSurface);
				break;
		}

		foreach (var child in element.GetVisualChildren())
		{
			ApplyTo(child);
		}
	}
}
