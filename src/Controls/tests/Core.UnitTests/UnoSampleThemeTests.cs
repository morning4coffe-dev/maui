using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Sample.Uno;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Microsoft.Maui.Controls.Core.UnitTests;

public class UnoSampleThemeTests : BaseTestFixture
{
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Application.Current = null;
		}
		base.Dispose(disposing);
	}

	[Fact]
	public void ForegroundsAndSurfacesSwitchTogetherAndSwitchBack()
	{
		var application = new Application { UserAppTheme = AppTheme.Light };
		Application.Current = application;
		var label = new Label { Text = "Sample text" };
		var button = new Button { Text = "Sample button" };
		var entry = new Entry { Text = "Typed value" };
		var fixedColorLabel = new Label { TextColor = Colors.White };
		var page = new ContentPage
		{
			Content = new VerticalStackLayout { Children = { label, button, entry, fixedColorLabel } },
		};
		application.LoadPage(page);
		SampleTheme.ApplyTo(page);

		Assert.Equal(SampleTheme.LightBackground, page.BackgroundColor);
		Assert.Equal(SampleTheme.LightForeground, label.TextColor);
		Assert.Equal(Colors.White, button.BackgroundColor);

		application.UserAppTheme = AppTheme.Dark;

		Assert.Equal(SampleTheme.DarkBackground, page.BackgroundColor);
		Assert.Equal(SampleTheme.DarkForeground, label.TextColor);
		Assert.Equal(SampleTheme.DarkForeground, button.TextColor);
		Assert.Equal(SampleTheme.DarkSurface, button.BackgroundColor);
		Assert.Equal(SampleTheme.DarkForeground, entry.TextColor);
		Assert.Equal(SampleTheme.DarkSurface, entry.BackgroundColor);
		Assert.Equal("Typed value", entry.Text);
		Assert.Equal(Colors.White, fixedColorLabel.TextColor);

		application.UserAppTheme = AppTheme.Light;

		Assert.Equal(SampleTheme.LightBackground, page.BackgroundColor);
		Assert.Equal(SampleTheme.LightForeground, label.TextColor);
		Assert.Equal(SampleTheme.LightForeground, button.TextColor);
		Assert.Equal(SampleTheme.LightForeground, entry.TextColor);
		Assert.Equal(Colors.White, entry.BackgroundColor);
	}
}
