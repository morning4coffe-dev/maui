#if ANDROID || IOS || MACCATALYST || WINDOWS
using System;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Embedding;
using Microsoft.Maui.Hosting;
#if UNO
using Microsoft.Maui.Platform;
#endif

#if ANDROID
using PlatformView = Android.Views.View;
using PlatformWindow = Android.App.Activity;
#elif IOS || MACCATALYST
using PlatformView = UIKit.UIView;
using PlatformWindow = UIKit.UIWindow;
#elif WINDOWS
using PlatformView = Microsoft.UI.Xaml.FrameworkElement;
using PlatformWindow = Microsoft.UI.Xaml.Window;
#endif

namespace Microsoft.Maui.Controls.Embedding;

/// <summary>
/// A set of extension methods that allow for embedding a MAUI view within a native application.
/// </summary>
public static class EmbeddingExtensions
{
	/// <summary>
	/// Enables MAUI to be embedded in native platform application by injecting embedded handlers into the service collection.
	/// </summary>
	/// <param name="builder">The <see cref="MauiAppBuilder"/> instance.</param>
	/// <returns>The <see cref="MauiAppBuilder"/> instance.</returns>
	/// <remarks>
	/// This is internal as it is exposed in Controls.Xaml since it needs to setup XAML defaults.
	/// </remarks>
	internal static MauiAppBuilder UseMauiEmbedding(this MauiAppBuilder builder)
	{
#if ANDROID
		var platformApplication = (global::Android.App.Application)global::Android.App.Application.Context;
#elif IOS || MACCATALYST
		var platformApplication = UIKit.UIApplication.SharedApplication.Delegate;
#elif WINDOWS
		var platformApplication = Microsoft.UI.Xaml.Application.Current;
#endif

		// Enable Core embedded features.
		builder.UseMauiEmbedding(platformApplication);

		// Register the embedded window handler.
		builder.ConfigureMauiHandlers(handlers =>
		{
			handlers.AddHandler<EmbeddedWindow, EmbeddedWindowHandler>();
		});

		return builder;
	}

	/// <summary>
	/// Creates a window-scoped <see cref="IMauiContext"/> for the provided native platform window.
	/// </summary>
	/// <param name="mauiApp">The <see cref="MauiApp"/> instance.</param>
	/// <param name="platformWindow">The native platform window instance to create the context for.</param>
	/// <returns>The window-scoped <see cref="IMauiContext"/> instance.</returns>
	/// <remarks>
	/// In addition to the context being created, a new Window instance is created and attached to the app.
	/// </remarks>
	public static IMauiContext CreateEmbeddedWindowContext(this MauiApp mauiApp, PlatformWindow platformWindow)
	{
		return mauiApp.CreateEmbeddedWindowContext(platformWindow, out _);
	}

	/// <summary>
	/// Creates a window-scoped <see cref="IMauiContext"/> for the provided native platform window, and hands
	/// back the synthetic window it created.
	/// </summary>
	/// <param name="mauiApp">The <see cref="MauiApp"/> instance.</param>
	/// <param name="platformWindow">The native platform window instance to create the context for.</param>
	/// <param name="window">The synthetic window that was created and attached to the application.</param>
	/// <returns>The window-scoped <see cref="IMauiContext"/> instance.</returns>
	/// <remarks>
	/// Hosts need the created window in order to set <see cref="Window.Page"/> and to drive the window
	/// lifecycle. Without this overload the only way to reach it is to guess at
	/// <c>Application.Windows</c>, which is not reliable.
	/// </remarks>
	public static IMauiContext CreateEmbeddedWindowContext(this MauiApp mauiApp, PlatformWindow platformWindow, out Window window)
	{
		var embeddedWindow = new EmbeddedWindow();

		// Create the Core embedded window scope.
		var windowContext = mauiApp.CreateEmbeddedWindowContext(platformWindow, embeddedWindow);

		// If the app is an embedded app then we need to add the window to the app.
		var embeddedApp = mauiApp.Services.GetRequiredService<EmbeddedPlatformApplication>();
		if (embeddedApp.Application is Application app && !app.Windows.Contains(embeddedWindow))
		{
			app.AddWindow(embeddedWindow);
		}

		window = embeddedWindow;
		return windowContext;
	}

	/// <summary>
	/// Similar to <see cref="ElementExtensions.ToPlatform(IElement, IMauiContext)"/>, but also adds the element as
	/// a logical child to the embedded window.
	/// </summary>
	/// <param name="element">The element to use when creating the native platform view.</param>
	/// <param name="context">The context to use when creating the native platform view.</param>
	/// <returns>The native platform view that represents the element.</returns>
	/// <remarks>
	/// Only if the window is an embedded window and the element is a <see cref="VisualElement"/> will the element
	/// be added as a logical child of that window.
	/// </remarks>
	public static PlatformView ToPlatformEmbedded(this IElement element, IMauiContext context)
	{
		// If the window is an embedded window, then we need to add the element as a logical child.
		var wndProvider = context.Services.GetService<EmbeddedWindowProvider>();
		if (wndProvider is not null && wndProvider.Window is EmbeddedWindow wnd && element is VisualElement visual)
			wnd.AddLogicalChild(visual);

		return element.ToPlatform(context);
	}

	/// <summary>
	/// Similar to <see cref="ElementExtensions.ToPlatform(IElement, IMauiContext)"/>, but also adds the element as
	/// a logical child to a new embedded window.
	/// </summary>
	/// <param name="element">The element to use when creating the native platform view.</param>
	/// <param name="mauiApp">The <see cref="MauiApp"/> instance.</param>
	/// <param name="platformWindow">The native platform window that will host this element.</param>
	/// <returns>The native platform view that represents the element.</returns>
	/// <remarks>
	/// Only if the window is an embedded window and the element is a <see cref="VisualElement"/> will the element
	/// be added as a logical child of that window.
	/// </remarks>
	public static PlatformView ToPlatformEmbedded(this IElement element, MauiApp mauiApp, PlatformWindow platformWindow)
	{
		var windowContext = mauiApp.CreateEmbeddedWindowContext(platformWindow);
		return element.ToPlatformEmbedded(windowContext);
	}

#if UNO
	/// <summary>
	/// Realizes <paramref name="page"/> as a window-level embedded root, so that window-scoped MAUI features
	/// such as modal navigation work inside the embedded content.
	/// </summary>
	/// <param name="page">The page to host. It must already be the embedded window's <c>Page</c>.</param>
	/// <param name="windowContext">The window-scoped context created for the hosting native window.</param>
	/// <returns>A disposable root whose <see cref="EmbeddedWindowRoot.PlatformView"/> the host displays.</returns>
	/// <exception cref="ArgumentException">
	/// <paramref name="page"/> is not the embedded window's page, or <paramref name="windowContext"/> is not
	/// a window-scoped context created by <c>CreateEmbeddedWindowContext</c>.
	/// </exception>
	/// <remarks>
	/// A standalone MAUI app puts a <c>WindowRootViewContainer</c> in the native window's content, and
	/// window-scoped features locate it from there. An embedded window does not own the native window's
	/// content, so the container is created here, registered on the window-scoped context, and returned for
	/// the host to display. Disposing the result unwinds all of that.
	/// </remarks>
	public static EmbeddedWindowRoot CreateEmbeddedWindowRoot(this Page page, IMauiContext windowContext)
	{
		ArgumentNullException.ThrowIfNull(page);
		ArgumentNullException.ThrowIfNull(windowContext);

		// Silently skipping the registration would return a view that looks fine until the first modal push
		// fails deep inside ModalNavigationManager.
		if (windowContext is not MauiContext mauiContext)
		{
			throw new ArgumentException(
				$"An embedded window root requires a {nameof(MauiContext)}, but got {windowContext.GetType()}.",
				nameof(windowContext));
		}

		if (page.GetParentWindow() is not Window window || !ReferenceEquals(window.Page, page))
		{
			throw new ArgumentException(
				"The page must already be assigned to the embedded window's Page before its root is created.",
				nameof(page));
		}

		var container = new WindowRootViewContainer();
		mauiContext.AddSpecific(container);

		var rootManager = windowContext.GetNavigationRootManager();
		rootManager.Connect(page.ToPlatform(windowContext));
		container.AddPage(rootManager.RootView);

		return new EmbeddedWindowRoot(mauiContext, container, rootManager, window);
	}
#endif
}

#if UNO
/// <summary>
/// The window-level root created for embedded MAUI content. Disposing it unwinds the modal stack, the
/// navigation root, and the container registration for the window scope.
/// </summary>
public sealed class EmbeddedWindowRoot : IDisposable
{
	readonly MauiContext _context;
	readonly WindowRootViewContainer _container;
	readonly NavigationRootManager _rootManager;
	readonly Window _window;
	bool _isDisposed;

	internal EmbeddedWindowRoot(
		MauiContext context,
		WindowRootViewContainer container,
		NavigationRootManager rootManager,
		Window window)
	{
		_context = context;
		_container = container;
		_rootManager = rootManager;
		_window = window;
	}

	/// <summary>Gets the native view the host should display.</summary>
	public PlatformView PlatformView => _container;

	/// <summary>
	/// Releases the embedded root. Modal pages are torn down first, because they are parented to this
	/// container and would otherwise be stranded in a detached tree while the modal stack still believes
	/// they are live.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;

		try
		{
			foreach (var modal in _window.Navigation.ModalStack.ToArray())
			{
				((IView)modal).DisconnectHandlers();
			}

			_window.ModalNavigationManager.ClearModalPages(xplat: true, platform: true);
			_rootManager.Disconnect();
			_container.CachedChildren.Clear();
		}
		finally
		{
			_context.RemoveSpecific<WindowRootViewContainer>();
		}
	}
}
#endif

#endif
