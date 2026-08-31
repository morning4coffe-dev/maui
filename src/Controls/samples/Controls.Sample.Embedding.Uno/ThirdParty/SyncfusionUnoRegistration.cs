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
}
