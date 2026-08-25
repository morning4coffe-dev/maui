using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;

using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiPage = Microsoft.Maui.Controls.Page;
using MauiVisualElement = Microsoft.Maui.Controls.VisualElement;
using MauiWindow = Microsoft.Maui.Controls.Window;
using PlatformView = Microsoft.UI.Xaml.FrameworkElement;
using PlatformWindow = Microsoft.UI.Xaml.Window;

namespace Maui.Controls.Sample.Uno;

/// <summary>
/// Owns the process-wide <see cref="MauiApp"/> and exactly one window-scoped <see cref="IMauiContext"/>
/// per Uno <see cref="PlatformWindow"/>.
/// </summary>
/// <remarks>
/// <para>
/// MAUI creates its window scope once per native window, retains it on the <c>MauiContext</c>, and releases
/// it from <see cref="IWindow.Destroying"/>. Every <see cref="MauiHost"/> inside one Uno window therefore
/// shares a single context, and hosts must not tear that scope down when they unload — unloading also
/// happens during navigation, reparenting, virtualization, and template changes.
/// </para>
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
	bool _windowActivated;
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
				_windowContext = SharedApp.CreateEmbeddedWindowContext(_platformWindow);

				// CreateEmbeddedWindowContext creates the synthetic EmbeddedWindow and appends it to the
				// application. EmbeddedWindowProvider is internal, so this is the supported way to reach it.
				var windows = MauiApplication.Current?.Windows;
				if (windows is { Count: > 0 })
				{
					_embeddedWindow = windows[windows.Count - 1];
				}
			}

			return _windowContext;
		}
	}

	/// <summary>
	/// Gets the synthetic MAUI window backing this Uno window, once the context has been created.
	/// </summary>
	public MauiWindow? EmbeddedWindow => _embeddedWindow;

	/// <summary>
	/// Realizes <paramref name="content"/> as an Uno <see cref="PlatformView"/> and parents it to this
	/// window's embedded MAUI window.
	/// </summary>
	/// <remarks>
	/// A <see cref="MauiPage"/> is promoted to the embedded window's <see cref="MauiWindow.Page"/>, which is
	/// what wires up window-scoped services such as <c>AlertManager</c> and modal navigation. Setting
	/// <c>Window.Page</c> already parents the page, so <c>ToPlatform</c> is used rather than
	/// <c>ToPlatformEmbedded</c> to avoid adding it as a logical child twice. Only the first page-based
	/// island becomes the window page; a window has exactly one.
	/// </remarks>
	public PlatformView Embed(MauiVisualElement content)
	{
		ArgumentNullException.ThrowIfNull(content);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		var context = WindowContext;

		PlatformView platformView;

		if (content is MauiPage page && _embeddedWindow is { Page: null })
		{
			_embeddedWindow.Page = page;
			platformView = page.ToPlatformEmbeddedWindowRoot(context);
			ActivateEmbeddedWindow(_embeddedWindow);
		}
		else
		{
			platformView = content.ToPlatformEmbedded(context);
		}

		_embeddedContent.Add(content);
		_embeddedWindow ??= content.Window;

		return platformView;
	}

	/// <summary>
	/// Raises the window lifecycle events that MAUI's window-scoped services wait on.
	/// </summary>
	/// <remarks>
	/// A standalone app gets these from <c>MauiWinUIWindow</c>. Nothing raises them for an embedded window,
	/// and modal navigation stays permanently queued until the window reports that it was activated.
	/// Both calls throw if repeated, and <c>Window.IsCreated</c>/<c>IsActivated</c> are internal, so the
	/// session tracks it.
	/// </remarks>
	void ActivateEmbeddedWindow(MauiWindow window)
	{
		if (_windowActivated)
		{
			return;
		}

		_windowActivated = true;

		var embedded = (IWindow)window;
		embedded.Created();
		embedded.Activated();
	}

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

		if (_embeddedWindow is { } window && ReferenceEquals(window.Page, content))
		{
			// Clearing Window.Page unparents the page and unsubscribes the window-scoped services that
			// setting it wired up.
			window.Page = null;
		}
		else
		{
			// ToPlatformEmbedded parents the element to the embedded window. Leaving it there would keep
			// the element and its handlers alive for the lifetime of the window.
			content.Window?.RemoveLogicalChild(content);
		}

		((IView)content).DisconnectHandlers();
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

		foreach (var content in _embeddedContent.ToArray())
		{
			Release(content);
		}

		// Destroying disposes the window service scope that MakeWindowScope created.
		(_embeddedWindow as IWindow)?.Destroying();

		_embeddedWindow = null;
		_windowContext = null;
	}
}
