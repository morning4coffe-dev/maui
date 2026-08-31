using Microsoft.Maui.Hosting;
using Syncfusion.Maui.Toolkit.Graphics.Internals;
using Syncfusion.Maui.Toolkit.Internals;

namespace Syncfusion.Maui.Toolkit.Uno;

/// <summary>
/// Registers the handlers the Syncfusion charts need.
/// </summary>
/// <remarks>
/// Upstream this is <c>ConfigureSyncfusionToolkit</c>, which also registers handlers for the carousel,
/// picker, bottom sheet and OTP entry — controls this build does not compile, because they wrap native
/// WinUI views or blur through Win2D. Only the three the charts use are registered here.
/// <para>
/// This lives inside the toolkit assembly rather than in the sample because
/// <c>WindowOverlayContainer</c> and <c>OverlayContainerHandler</c> are internal to it.
/// </para>
/// </remarks>
public static class SyncfusionUnoToolkit
{
	public static MauiAppBuilder ConfigureSyncfusionCharts(this MauiAppBuilder builder) =>
		builder.ConfigureMauiHandlers(handlers =>
		{
			handlers.AddHandler(typeof(IDrawableView), typeof(SfDrawableViewHandler));
			handlers.AddHandler(typeof(IDrawableLayout), typeof(SfViewHandler));
			handlers.AddHandler(typeof(WindowOverlayContainer), typeof(OverlayContainerHandler));
		});

	/// <summary>
	/// Gets how many points a series actually resolved from its <c>ItemsSource</c>.
	/// </summary>
	/// <remarks>
	/// The census cannot see whether a chart plotted anything, and a chart with no data still draws its
	/// axis gridlines, which looks close enough to working to be misleading. Syncfusion's
	/// <c>PointsCount</c> is internal, so it is surfaced from inside the assembly.
	/// </remarks>
	public static int GetPointCount(this Syncfusion.Maui.Toolkit.Charts.ChartSeries series) => series.PointsCount;
}
