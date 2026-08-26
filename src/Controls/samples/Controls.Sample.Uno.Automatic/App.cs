namespace Microsoft.Maui.Controls.Sample.Uno.Automatic;

public sealed class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState) =>
		new(new MainPage())
		{
			Title = "Automatic MAUI on Uno",
		};
}
