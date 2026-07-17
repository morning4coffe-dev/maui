using Uno.UI.Hosting;

namespace Microsoft.Maui.Controls.Sample.Uno;

internal static class Program
{
	public static void Main(string[] args)
	{
		var host = UnoPlatformHostBuilder.Create()
			.App(() => new UnoMauiApplication())
			.UseAppleUIKit()
			.Build();

		host.Run();
	}
}
