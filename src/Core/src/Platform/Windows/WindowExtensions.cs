using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Graphics.Display;
using WinRT.Interop;

namespace Microsoft.Maui.Platform
{
	public static partial class WindowExtensions
	{
		internal static Rect[]? GetDefaultTitleBarDragRectangles(this UI.Xaml.Window platformWindow, IWindow window)
		{
			if (window?.Handler?.MauiContext is IMauiContext mauiContext)
			{
				return platformWindow.GetDefaultTitleBarDragRectangles(mauiContext);
			}

			return null;
		}

		internal static Rect[]? GetDefaultTitleBarDragRectangles(this UI.Xaml.Window platformWindow, IMauiContext mauiContext)
		{
			if (!AppWindowTitleBar.IsCustomizationSupported())
				return null;

			if (mauiContext?.GetNavigationRootManager()?.RootView is WindowRootView rootView &&
				rootView.AppTitleBarContainer is FrameworkElement element)
			{
				return new[]
				{
					new Rect((int)element.Margin.Left, 0, (int)element.ActualWidth, (int)element.ActualHeight),
				};
			}

			return null;
		}

		public static void UpdateTitle(this UI.Xaml.Window platformWindow, IWindow window)
		{
			platformWindow.UpdateTitle(window, window.Handler?.MauiContext);
		}

		internal static void UpdateTitle(this UI.Xaml.Window platformWindow, IWindow window, IMauiContext? mauiContext)
		{
			platformWindow.Title = window.Title ?? string.Empty;
			mauiContext?
				.GetNavigationRootManager()?
				.SetTitle(window.Title);
		}

		internal static void UpdateTitleBar(this UI.Xaml.Window platformWindow, IWindow window, IMauiContext? mauiContext)
		{
			mauiContext?.GetNavigationRootManager().SetTitleBar(window.TitleBar, mauiContext);
		}

		public static void UpdateX(this UI.Xaml.Window platformWindow, IWindow window) =>
			platformWindow.UpdatePosition(window);

		public static void UpdateY(this UI.Xaml.Window platformWindow, IWindow window) =>
			platformWindow.UpdatePosition(window);

		public static void UpdatePosition(this UI.Xaml.Window platformWindow, IWindow window)
		{
			var appWindow = platformWindow.GetAppWindow();
			if (appWindow is null)
				return;

			var density = platformWindow.GetDisplayDensity();
			var x = window.X;
			var y = window.Y;

			var currPos = appWindow.Position;
			x = Primitives.Dimension.IsExplicitSet(x)
				? Math.Round(x * density)
				: currPos.X;
			y = Primitives.Dimension.IsExplicitSet(y)
				? Math.Round(y * density)
				: currPos.Y;

			var pos = CreatePoint((int)x, (int)y);

			if (!AreEqual(pos, currPos))
				appWindow.Move(pos);
		}

		public static void UpdateWidth(this UI.Xaml.Window platformWindow, IWindow window) =>
			platformWindow.UpdateSize(window);

		public static void UpdateHeight(this UI.Xaml.Window platformWindow, IWindow window) =>
			platformWindow.UpdateSize(window);

		public static void UpdateSize(this UI.Xaml.Window platformWindow, IWindow window)
		{
			var appWindow = platformWindow.GetAppWindow();
			if (appWindow is null)
				return;

			var density = platformWindow.GetDisplayDensity();
			var width = window.Width;
			var height = window.Height;

			var currSize = appWindow.Size;
			width = Primitives.Dimension.IsExplicitSet(width)
				? Math.Round(width * density)
				: currSize.Width;
			height = Primitives.Dimension.IsExplicitSet(height)
				? Math.Round(height * density)
				: currSize.Height;

			var size = CreateSize((int)width, (int)height);

			if (!AreEqual(size, currSize))
				appWindow.Resize(size);
		}

		public static void UpdateMinimumWidth(this UI.Xaml.Window platformWindow, IWindow window) =>
			platformWindow.UpdateMinimumSize(window);

		public static void UpdateMinimumHeight(this UI.Xaml.Window platformWindow, IWindow window) =>
			platformWindow.UpdateMinimumSize(window);

		public static void UpdateMinimumSize(this UI.Xaml.Window platformWindow, IWindow window)
		{
			if (platformWindow is not IPlatformSizeRestrictedWindow restrictedWindow)
				return;

			var density = platformWindow.GetDisplayDensity();
			var minWidth = window.MinimumWidth;
			var minHeight = window.MinimumHeight;

			var actualMinWidth = double.IsFinite(minWidth)
				? (int)Math.Clamp(minWidth * density, 0, int.MaxValue)
				: 0;

			var actualMinHeight = double.IsFinite(minHeight)
				? (int)Math.Clamp(minHeight * density, 0, int.MaxValue)
				: 0;

			var minSize = CreateSize(actualMinWidth, actualMinHeight);

			restrictedWindow.MinimumSize = minSize;

			var appWindow = platformWindow.GetAppWindow();
			if (appWindow is null)
				return;

			var currentSize = appWindow.Size;
			var temp = currentSize;
			if (currentSize.Width < actualMinWidth)
				temp.Width = actualMinWidth;
			if (currentSize.Height < actualMinHeight)
				temp.Height = actualMinHeight;
			if (!AreEqual(currentSize, temp))
				appWindow.Resize(temp);
		}

		public static void UpdateMaximumWidth(this UI.Xaml.Window platformWindow, IWindow window) =>
			platformWindow.UpdateMaximumSize(window);

		public static void UpdateMaximumHeight(this UI.Xaml.Window platformWindow, IWindow window) =>
			platformWindow.UpdateMaximumSize(window);

		public static void UpdateMaximumSize(this UI.Xaml.Window platformWindow, IWindow window)
		{
			if (platformWindow is not IPlatformSizeRestrictedWindow restrictedWindow)
				return;

			var density = platformWindow.GetDisplayDensity();
			var maxWidth = window.MaximumWidth;
			var maxHeight = window.MaximumHeight;

			var actualMaxWidth = double.IsFinite(maxWidth)
				? (int)Math.Clamp(maxWidth * density, 0, int.MaxValue)
				: int.MaxValue;

			var actualMaxHeight = double.IsFinite(maxHeight)
				? (int)Math.Clamp(maxHeight * density, 0, int.MaxValue)
				: int.MaxValue;

			var MaxSize = CreateSize(actualMaxWidth, actualMaxHeight);

			restrictedWindow.MaximumSize = MaxSize;

			var appWindow = platformWindow.GetAppWindow();
			if (appWindow is null)
				return;

			var currentSize = appWindow.Size;
			var temp = currentSize;
			if (currentSize.Width > actualMaxWidth)
				temp.Width = actualMaxWidth;
			if (currentSize.Height > actualMaxHeight)
				temp.Height = actualMaxHeight;
			if (!AreEqual(currentSize, temp))
				appWindow.Resize(temp);
		}

		internal static void UpdateIsMinimizable(this UI.Xaml.Window platformWindow, IWindow window)
		{
			var appWindow = platformWindow.GetAppWindow();

			if (appWindow?.Presenter is UI.Windowing.OverlappedPresenter presenter)
			{
				presenter.IsMinimizable = window.IsMinimizable;
			}
		}

		internal static void UpdateIsMaximizable(this UI.Xaml.Window platformWindow, IWindow window)
		{
			var appWindow = platformWindow.GetAppWindow();

			if (appWindow?.Presenter is UI.Windowing.OverlappedPresenter presenter)
			{
				presenter.IsMaximizable = window.IsMaximizable;
			}
		}

		public static IWindow? GetWindow(this UI.Xaml.Window platformWindow)
		{
			foreach (var window in WindowExtensions.GetWindows())
			{
				if (window?.Handler?.PlatformView is UI.Xaml.Window win && win == platformWindow)
					return window;
			}

			if (platformWindow is MauiWinUIWindow mauiWindow)
				return mauiWindow?.Window;

			return null;
		}

		public static IntPtr GetWindowHandle(this UI.Xaml.Window platformWindow)
		{
#if UNO
			return platformWindow is MauiWinUIWindow mauiWindow ? mauiWindow.WindowHandle : IntPtr.Zero;
#else
			var hwnd = WindowNative.GetWindowHandle(platformWindow);

			if (hwnd == IntPtr.Zero)
				throw new NullReferenceException("The Window Handle is null.");

			return hwnd;
#endif
		}

		public static float GetDisplayDensity(this UI.Xaml.Window platformWindow)
		{
#if UNO
			var xamlRootScale = (platformWindow.Content as FrameworkElement)?.XamlRoot?.RasterizationScale;
			if (xamlRootScale is > 0)
				return (float)xamlRootScale.Value;

			// Uno's AppWindow coordinates are logical before XamlRoot is available.
			// Applying the host scale here double-scales the initial window and prevents rendering.
			return 1.0f;
#else
			var hwnd = platformWindow.GetWindowHandle();

			if (hwnd == IntPtr.Zero)
			{
				return 1.0f;
			}

			return PlatformMethods.GetDpiForWindow(hwnd) / DeviceDisplay.BaseLogicalDpi;
#endif
		}

		internal static void Minimize(this UI.Xaml.Window platformWindow)
		{
#if !UNO
			PlatformMethods
				.ShowWindow(platformWindow.GetWindowHandle(),
							PlatformMethods.ShowWindowFlags.SW_MINIMIZE);
#endif
		}

		internal static void Maximize(this UI.Xaml.Window platformWindow)
		{
#if !UNO
			PlatformMethods
				.ShowWindow(platformWindow.GetWindowHandle(),
							PlatformMethods.ShowWindowFlags.SW_MAXIMIZE);
#endif
		}

		internal static void Restore(this UI.Xaml.Window platformWindow)
		{
#if !UNO
			PlatformMethods
				.ShowWindow(platformWindow.GetWindowHandle(),
							PlatformMethods.ShowWindowFlags.SW_RESTORE);
#endif
		}

		public static UI.Windowing.AppWindow? GetAppWindow(this UI.Xaml.Window platformWindow)
		{
#if UNO
			return platformWindow.AppWindow;
#else
			var hwnd = platformWindow.GetWindowHandle();

			if (hwnd == IntPtr.Zero)
				return null;

			var windowId = UI.Win32Interop.GetWindowIdFromWindow(hwnd);
			return UI.Windowing.AppWindow.GetFromWindowId(windowId);
#endif
		}

		static PointInt32 CreatePoint(int x, int y)
		{
#if UNO
			return new PointInt32 { X = x, Y = y };
#else
			return new PointInt32(x, y);
#endif
		}

		static SizeInt32 CreateSize(int width, int height)
		{
#if UNO
			return new SizeInt32 { Width = width, Height = height };
#else
			return new SizeInt32(width, height);
#endif
		}

		static bool AreEqual(PointInt32 left, PointInt32 right) =>
			left.X == right.X && left.Y == right.Y;

		static bool AreEqual(SizeInt32 left, SizeInt32 right) =>
			left.Width == right.Width && left.Height == right.Height;

		internal static DisplayOrientation GetOrientation(this IWindow? window)
		{
			if (window == null)
				return DeviceDisplay.Current.MainDisplayInfo.Orientation;

			var appWindow = window.Handler?.MauiContext?.GetPlatformWindow()?.GetAppWindow();

			if (appWindow == null)
				return DisplayOrientation.Unknown;

			DisplayOrientations orientationEnum;
			int theScreenWidth = appWindow.Size.Width;
			int theScreenHeight = appWindow.Size.Height;
			if (theScreenWidth > theScreenHeight)
				orientationEnum = DisplayOrientations.Landscape;
			else
				orientationEnum = DisplayOrientations.Portrait;

			return orientationEnum == DisplayOrientations.Landscape
				? DisplayOrientation.Landscape
				: DisplayOrientation.Portrait;
		}
	}
}