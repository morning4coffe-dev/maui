namespace Microsoft.Maui.Controls.Sample.Uno;

public sealed class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState) =>
		new(new MainPage())
		{
			Title = "MAUI WinUI handlers on Uno",
		};
}
