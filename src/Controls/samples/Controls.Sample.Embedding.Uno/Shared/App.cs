using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Maui.Controls.Sample.Uno;

/// <summary>
/// The embedded MAUI application.
/// </summary>
/// <remarks>
/// Embedding never calls <c>CreateWindow</c>: <c>CreateEmbeddedWindowContext</c> creates a synthetic
/// <c>EmbeddedWindow</c> instead. This type exists so that application-scoped MAUI state — resources in
/// particular — resolves through the logical tree for embedded content.
/// </remarks>
public sealed class App : Application
{
	public App()
	{
		Resources.Add("EmbeddedHeadline", new Style(typeof(Label))
		{
			Setters =
			{
				new Setter { Property = Label.FontSizeProperty, Value = 20d },
				new Setter { Property = Label.FontAttributesProperty, Value = FontAttributes.Bold },
				new Setter { Property = Label.TextColorProperty, Value = Color.FromArgb("#512BD4") },
			},
		});
	}
}
