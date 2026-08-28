using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;

using MauiPage = Microsoft.Maui.Controls.Page;
using MauiVisualElement = Microsoft.Maui.Controls.VisualElement;
using MauiWindow = Microsoft.Maui.Controls.Window;
using AppTheme = Microsoft.Maui.ApplicationModel.AppTheme;
using ElementTheme = Microsoft.UI.Xaml.ElementTheme;
using MauiApplication = Microsoft.Maui.Controls.Application;
using PlatformView = Microsoft.UI.Xaml.FrameworkElement;
using PlatformWindow = Microsoft.UI.Xaml.Window;

namespace Maui.Controls.Sample.Uno;

/// <summary>
/// Owns the process-wide <see cref="MauiApp"/> and exactly one window-scoped <see cref="IMauiContext"/>
/// per Uno <see cref="PlatformWindow"/>.
/// </summary>
/// <remarks>
/// MAUI creates its window scope once per native window, retains it on the <c>MauiContext</c>, and releases
/// it from <see cref="IWindow.Destroying"/>. Every <see cref="MauiHost"/> inside one Uno window therefore
/// shares a single context, and hosts must not tear that scope down when they unload — unloading also
/// happens during navigation, reparenting, virtualization, and template changes.
/// </remarks>
public sealed class MauiEmbeddingSession : IDisposable
{
	static readonly object Gate = new();
	static readonly Dictionary<PlatformWindow, MauiEmbeddingSession> Sessions = new();
	static MauiApp? sharedApp;

	readonly PlatformWindow _platformWindow;
	readonly List<MauiVisualElement> _embeddedContent = new();
	MauiWindow? _embeddedWindow;
	IMauiContext? _windowContext;
	EmbeddedWindowRoot? _windowRoot;
	PlatformView? _themeRoot;
	bool _isCreated;
	bool _isActivated;
	bool _isDisposed;

	MauiEmbeddingSession(PlatformWindow platformWindow) => _platformWindow = platformWindow;

	/// <summary>
	/// Gets the single embedded <see cref="MauiApp"/> for this process, creating it on first use.
	/// </summary>
	/// <remarks>
	/// MAUI's embedding bootstrap captures <c>Microsoft.UI.Xaml.Application.Current</c> while the builder is
	/// configured, and the initializers it registers dispatch onto the UI thread. This must therefore run on
	/// the UI thread, and only once the Uno application instance exists.
	/// </remarks>
	public static MauiApp SharedApp
	{
		get
		{
			lock (Gate)
			{
				if (sharedApp is not null)
				{
					return sharedApp;
				}

				if (Microsoft.UI.Xaml.Application.Current is null)
				{
					throw new InvalidOperationException(
						"The embedded MauiApp must be created after the Uno application instance exists. " +
						"Create it from OnLaunched rather than from Program.Main.");
				}

				sharedApp = MauiProgram.CreateMauiApp();
				return sharedApp;
			}
		}
	}

	/// <summary>Gets the session for <paramref name="platformWindow"/>, creating it on first use.</summary>
	public static MauiEmbeddingSession GetOrCreate(PlatformWindow platformWindow)
	{
		ArgumentNullException.ThrowIfNull(platformWindow);

		lock (Gate)
		{
			if (!Sessions.TryGetValue(platformWindow, out var session))
			{
				session = new MauiEmbeddingSession(platformWindow);
				Sessions.Add(platformWindow, session);
			}

			return session;
		}
	}

	/// <summary>Gets the window-scoped context, creating it on first use.</summary>
	public IMauiContext WindowContext
	{
		get
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			if (_windowContext is null)
			{
				// The out parameter hands back exactly the window that was created. Locating it by position
				// in Application.Windows would silently pick the wrong one if anything else registered a
				// window while this context was being built.
				_windowContext = SharedApp.CreateEmbeddedWindowContext(_platformWindow, out var window);
				_embeddedWindow = window;
				AttachThemeBridge();
			}

			return _windowContext;
		}
	}

	/// <summary>Gets the Uno window this session is bound to.</summary>
	public PlatformWindow PlatformWindow => _platformWindow;

	/// <summary>Gets the synthetic MAUI window backing this Uno window, once the context has been created.</summary>
	public MauiWindow? EmbeddedWindow => _embeddedWindow;

	/// <summary>Gets a value indicating whether this session already hosts a window-level page.</summary>
	public bool HasWindowPage => _windowRoot is not null;

	/// <summary>
	/// Realizes <paramref name="content"/> as an Uno <see cref="PlatformView"/> and parents it to this
	/// window's embedded MAUI window.
	/// </summary>
	/// <remarks>
	/// A <see cref="MauiPage"/> is promoted to the embedded window's <see cref="MauiWindow.Page"/>, which is
	/// what wires up window-scoped services such as <c>AlertManager</c> and modal navigation. Setting
	/// <c>Window.Page</c> already parents the page, so the window root path is used rather than
	/// <c>ToPlatformEmbedded</c>, which would add it as a logical child twice.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// A second page is supplied. A window has exactly one <c>Page</c>, and a second page would silently
	/// inherit the first page's navigation proxy and alert manager, so its dialogs and modals would render
	/// in the first island's region.
	/// </exception>
	public PlatformView Embed(MauiVisualElement content)
	{
		ArgumentNullException.ThrowIfNull(content);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		var context = WindowContext;

		// The window content exists by the time a host loads, which is not guaranteed when the context is
		// first created, so the theme bridge is attached here as well. Attaching is idempotent.
		AttachThemeBridge();

		PlatformView platformView;

		if (content is MauiPage page)
		{
			if (_windowRoot is not null)
			{
				throw new InvalidOperationException(
					"This window already hosts a page-based MAUI island. A window has a single Page, so a " +
					"second page would route its dialogs and modal navigation through the first island. " +
					"Host additional islands as views, or use a separate Uno window.");
			}

			_embeddedWindow!.Page = page;
			_windowRoot = page.CreateEmbeddedWindowRoot(context);
			platformView = _windowRoot.PlatformView;
		}
		else
		{
			platformView = content.ToPlatformEmbedded(context);
		}

		_embeddedContent.Add(content);

		return platformView;
	}

	/// <summary>
	/// Reports that the hosting Uno window was activated, which is what unblocks MAUI's window-scoped
	/// services.
	/// </summary>
	/// <remarks>
	/// A standalone app gets this from <c>MauiWinUIWindow</c>. Nothing raises it for an embedded window, and
	/// modal navigation stays permanently queued until the window reports activation. This must come from
	/// the real native activation event: raising it while the host is still being constructed would tell
	/// MAUI the window is live before the Uno window has any content or XamlRoot.
	/// </remarks>
	public void NotifyWindowActivated()
	{
		if (_isDisposed || _embeddedWindow is not { } window)
		{
			return;
		}

		var embedded = (IWindow)window;

		if (!_isCreated)
		{
			_isCreated = true;
			embedded.Created();
		}

		if (!_isActivated)
		{
			_isActivated = true;
			embedded.Activated();
		}
	}

	/// <summary>Reports that the hosting Uno window was deactivated.</summary>
	public void NotifyWindowDeactivated()
	{
		if (_isDisposed || !_isActivated || _embeddedWindow is not { } window)
		{
			return;
		}

		_isActivated = false;
		((IWindow)window).Deactivated();
	}

	/// <summary>
	/// Forwards the hosting application's effective theme to the embedded MAUI application.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Nothing reports theme changes to an embedded MAUI application. MAUI's Windows theme plumbing is a
	/// Win32 <c>WM_THEMECHANGE</c> hook that the Uno target compiles out, and embedding has no
	/// <c>MauiWinUIWindow</c> either, so <c>PlatformAppTheme</c> would stay <see cref="AppTheme.Unspecified"/>
	/// for the life of the process and <c>AppThemeBinding</c> would never resolve.
	/// </para>
	/// <para>
	/// The theme is read from the window root's <c>ActualTheme</c> rather than from
	/// <c>Application.RequestedTheme</c>. Uno rejects a runtime change to the application theme with
	/// <see cref="NotSupportedException"/>, so an Uno app switches theme by setting <c>RequestedTheme</c> on
	/// a root element; that moves <c>ActualTheme</c> while leaving the application theme untouched. Reading
	/// the application theme would therefore miss every in-app theme switch. <c>ActualTheme</c> also still
	/// reflects the system theme when no element override is in effect, so it covers both cases.
	/// </para>
	/// <para>
	/// The result is assigned to <c>UserAppTheme</c> because <c>PlatformAppTheme</c> is not settable and the
	/// only route into it, <c>IApplication.ThemeChanged</c>, re-reads the application theme this bridge is
	/// deliberately not using. Embedded content must therefore leave <c>UserAppTheme</c> alone: the host owns
	/// the theme.
	/// </para>
	/// </remarks>
	public void NotifyThemeChanged()
	{
		if (_isDisposed || sharedApp?.Services.GetService<IApplication>() is not { } application)
		{
			return;
		}

		// Keeps PlatformAppTheme in step with the host application for anything that reads it directly.
		application.ThemeChanged();

		if (_themeRoot is { } root && application is MauiApplication controlsApplication)
		{
			controlsApplication.UserAppTheme = ToAppTheme(root.ActualTheme);
		}
	}

	static AppTheme ToAppTheme(ElementTheme theme) => theme switch
	{
		ElementTheme.Dark => AppTheme.Dark,
		ElementTheme.Light => AppTheme.Light,
		_ => AppTheme.Unspecified,
	};

	void AttachThemeBridge()
	{
		if (_isDisposed || _themeRoot is not null || _platformWindow.Content is not PlatformView root)
		{
			return;
		}

		_themeRoot = root;
		root.ActualThemeChanged += OnActualThemeChanged;

		// The embedded application resolved its theme before the host existed, so seed it once on attach.
		NotifyThemeChanged();
	}

	void DetachThemeBridge()
	{
		if (_themeRoot is { } root)
		{
			root.ActualThemeChanged -= OnActualThemeChanged;
			_themeRoot = null;
		}
	}

	void OnActualThemeChanged(PlatformView sender, object args) => NotifyThemeChanged();

	/// <summary>
	/// Detaches <paramref name="content"/> without tearing down the window scope, which lives as long as the
	/// Uno window and is shared by every host inside it.
	/// </summary>
	public void Release(MauiVisualElement content)
	{
		ArgumentNullException.ThrowIfNull(content);

		if (!_embeddedContent.Remove(content))
		{
			return;
		}

		try
		{
			if (_embeddedWindow is { } window && ReferenceEquals(window.Page, content))
			{
				// Order matters: the root owns the modal stack and the navigation root, and both are
				// reached through the page. Unwind them before the page is detached.
				_windowRoot?.Dispose();
				_windowRoot = null;
				window.Page = null;
			}
			else
			{
				// ToPlatformEmbedded parents the element to the embedded window. Leaving it there would
				// keep the element and its handlers alive for the lifetime of the window.
				content.Window?.RemoveLogicalChild(content);
			}
		}
		finally
		{
			((IView)content).DisconnectHandlers();
		}
	}

	/// <summary>Destroys the embedded MAUI window for this Uno window exactly once.</summary>
	public void Dispose()
	{
		lock (Gate)
		{
			if (_isDisposed)
			{
				return;
			}

			_isDisposed = true;
			Sessions.Remove(_platformWindow);
		}

		try
		{
			DetachThemeBridge();

			foreach (var content in _embeddedContent.ToArray())
			{
				try
				{
					Release(content);
				}
				catch (Exception)
				{
					// One failing item must not strand the window scope destroyed below.
				}
			}

			_windowRoot?.Dispose();
			_windowRoot = null;
		}
		finally
		{
			// Destroying disposes the window service scope that MakeWindowScope created.
			(_embeddedWindow as IWindow)?.Destroying();

			_embeddedWindow = null;
			_windowContext = null;
		}
	}
}
