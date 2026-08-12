using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Platform;
using System.Threading.Tasks;
using System.Threading;
using System;

#if IOS || MACCATALYST
using PlatformView = UIKit.UIView;
#elif ANDROID
using PlatformView = Android.Views.View;
#elif WINDOWS
using PlatformView = Microsoft.UI.Xaml.FrameworkElement;
#elif TIZEN
using PlatformView = Tizen.NUI.BaseComponents.View;
#elif (NETSTANDARD || !PLATFORM) || (NET6_0_OR_GREATER && !IOS && !ANDROID && !TIZEN)
using PlatformView = System.Object;
using IPlatformViewHandler = Microsoft.Maui.IViewHandler;
#endif

namespace Microsoft.Maui;

/// <summary>
/// Extension methods for interacting with a platform's Soft Input Pane
/// </summary>
public static partial class SoftInputExtensions
{
	/// <summary>
	/// If a soft input pane is currently showing, this will attempt to hide it.
	/// </summary>
	/// <param name="targetView"></param>
	/// <param name="token">Cancellation token</param>
	/// <returns>
	/// Returns <c>true</c> if the platform was able to hide the soft input pane.</returns>
	public static Task<bool> HideSoftInputAsync(this ITextInput targetView, CancellationToken token)
	{
#if NETSTANDARD
		return Task.FromResult(false);
#else
		token.ThrowIfCancellationRequested();
		if (!targetView.TryGetPlatformView(out var platformView, out var handler, out _))
		{
			return Task.FromResult(false);
		}

		if (handler.MauiContext?.Services.GetService<IDispatcher>() is not IDispatcher dispatcher)
		{
			return Task.FromResult(false);
		}

		return InvokeOnDispatcherAsync(dispatcher, () => platformView.HideSoftInput()).WaitAsync(token);
#endif
	}

	/// <summary>
	/// If a soft input pane is currently hiding, this will attempt to show it.
	/// </summary>
	/// <param name="targetView"></param>
	/// <param name="token">Cancellation token</param>
	/// <returns>
	/// Returns <c>true</c> if the platform was able to show the soft input pane.</returns>
	public static Task<bool> ShowSoftInputAsync(this ITextInput targetView, CancellationToken token)
	{
#if NETSTANDARD
		return Task.FromResult(false);
#else
		token.ThrowIfCancellationRequested();

		if (!targetView.TryGetPlatformView(out var platformView, out var handler, out var view))
		{
			return Task.FromResult(false);
		}

		if (handler.MauiContext?.Services.GetService<IDispatcher>() is not IDispatcher dispatcher)
		{
			return Task.FromResult(false);
		}

		return ShowSoftInputAsyncCore(dispatcher, platformView, handler, view, token);
#endif
	}

	/// <summary>
	/// Checks to see if the platform is currently showing the soft input pane
	/// </summary>
	/// <param name="targetView"></param>
	/// <returns>
	/// Returns <c>true</c> if the soft input pane is currently showing.</returns>
	public static bool IsSoftInputShowing(this ITextInput targetView)
	{
		if (!targetView.TryGetPlatformView(out PlatformView? platformView, out var handler, out _))
		{
			return false;
		}

		if (handler.MauiContext?.Services.GetService<IDispatcher>() is not IDispatcher dispatcher ||
			dispatcher.IsDispatchRequired)
		{
			return false;
		}

		return platformView.IsSoftInputShowing();
	}

	internal static Task<bool> InvokeOnDispatcherAsync(IDispatcher? dispatcher, Func<bool> action)
	{
		_ = action ?? throw new ArgumentNullException(nameof(action));

		if (dispatcher is null)
		{
			return Task.FromResult(false);
		}

		var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!dispatcher.Dispatch(() =>
		{
			try
			{
				tcs.TrySetResult(action());
			}
			catch (Exception e)
			{
				tcs.TrySetException(e);
			}
		}))
		{
			return Task.FromResult(false);
		}

		return tcs.Task;
	}

#if !NETSTANDARD
	static async Task<bool> ShowSoftInputAsyncCore(IDispatcher dispatcher, PlatformView platformView, IPlatformViewHandler handler, IView view, CancellationToken token)
	{
		var isFocused = await InvokeOnDispatcherAsync(dispatcher, () => view.IsFocused).WaitAsync(token).ConfigureAwait(false);
		if (!isFocused)
		{
			await InvokeOnDispatcherAsync(dispatcher, () =>
			{
#pragma warning disable CS0618
				handler.Invoke(nameof(IView.Focus), new FocusRequest(false));
#pragma warning restore CS0618
				return true;
			}).WaitAsync(token).ConfigureAwait(false);
		}

		return await InvokeOnDispatcherAsync(dispatcher, () => platformView.ShowSoftInput()).WaitAsync(token).ConfigureAwait(false);
	}
#endif

	static bool TryGetPlatformView(this ITextInput textInput,
									[NotNullWhen(true)] out PlatformView? platformView,
									[NotNullWhen(true)] out IPlatformViewHandler? handler,
									[NotNullWhen(true)] out IView? view)
	{
		if (textInput is not IView iView ||
			iView.Handler is not IPlatformViewHandler platformViewHandler)
		{
			platformView = null;
			handler = null;
			view = null;

			return false;
		}

		if (iView.Handler?.PlatformView is not PlatformView platform)
		{
			platformView = null;
			handler = null;
			view = null;

			return false;
		}

		handler = platformViewHandler;
		platformView = platform;
		view = iView;

		return true;
	}
}

#if NETSTANDARD || !PLATFORM
public static partial class SoftInputExtensions
{
	static bool HideSoftInput(this object _) => throw new NotSupportedException();

	static bool ShowSoftInput(this object _) => throw new NotSupportedException();

	static bool IsSoftInputShowing(this object _) => throw new NotSupportedException();
}
#endif