namespace Microsoft.Maui.Controls.Sample.Uno.Automatic;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	private void OnButtonClicked(object? sender, EventArgs e)
	{
		ResultLabel.Text = "MAUI event handled through Uno";
	}
}
