using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Hosting;

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
			return _windowContext ??= SharedApp.CreateEmbeddedWindowContext(_platformWindow);
		}
	}

	/// <summary>
	/// Realizes <paramref name="content"/> as an Uno <see cref="PlatformView"/> and parents it to this
	/// window's embedded MAUI window.
	/// </summary>
	public PlatformView Embed(MauiVisualElement content)
	{
		ArgumentNullException.ThrowIfNull(content);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		var platformView = content.ToPlatformEmbedded(WindowContext);

		_embeddedContent.Add(content);
		_embeddedWindow ??= content.Window;

		return platformView;
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

		// ToPlatformEmbedded parents the element to the embedded window. Leaving it there would keep the
		// element and its handlers alive for the lifetime of the window.
		content.Window?.RemoveLogicalChild(content);
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
