using System;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui.Platform;
using PlatformView = Android.Views.View;

namespace Microsoft.Maui.Handlers
{
	public partial class ViewHandler
	{
		internal const string BeginNativePropertyUpdateBatchCommand = "BeginNativePropertyUpdateBatch";
		internal const string CommitNativePropertyUpdateBatchCommand = "CommitNativePropertyUpdateBatch";

		[Flags]
		internal enum NativePropertyUpdate
		{
			None = 0,
			IsEnabled = 1 << 0,
			Opacity = 1 << 1,
			TranslationX = 1 << 2,
			TranslationY = 1 << 3,
			ScaleX = 1 << 4,
			ScaleY = 1 << 5,
			Rotation = 1 << 6,
			RotationX = 1 << 7,
			RotationY = 1 << 8,
		}

		bool _isNativePropertyUpdateBatchActive;
		NativePropertyUpdate _pendingNativePropertyUpdates;

		internal int NativePropertyUpdateBatchFlushCount { get; private set; }

		partial void ConnectingHandler(PlatformView? platformView)
		{
			platformView?.FocusChange += OnPlatformViewFocusChange;
		}

		partial void DisconnectingHandler(PlatformView platformView)
		{
			_isNativePropertyUpdateBatchActive = false;
			_pendingNativePropertyUpdates = NativePropertyUpdate.None;

			if (platformView.IsAlive())
			{
				platformView.FocusChange -= OnPlatformViewFocusChange;

				if (ViewCompat.GetAccessibilityDelegate(platformView) is MauiAccessibilityDelegateCompat ad)
				{
					ad.Handler = null;
					ViewCompat.SetAccessibilityDelegate(platformView, null);
				}
			}

			if (VirtualView is IToolbarElement te)
			{
				te.Toolbar?.Handler?.DisconnectHandler();
			}
		}

		static void MapBeginNativePropertyUpdateBatch(IViewHandler handler, IView view, object? args)
		{
			if (!RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled ||
				handler is not ViewHandler viewHandler ||
				viewHandler._isNativePropertyUpdateBatchActive)
				return;

			viewHandler._isNativePropertyUpdateBatchActive = true;
			viewHandler._pendingNativePropertyUpdates = NativePropertyUpdate.None;
		}

		static void MapCommitNativePropertyUpdateBatch(IViewHandler handler, IView view, object? args)
		{
			if (handler is ViewHandler viewHandler)
				viewHandler.CommitNativePropertyUpdates(view);
		}

		internal bool TryQueueNativePropertyUpdate(NativePropertyUpdate property)
		{
			if (!_isNativePropertyUpdateBatchActive ||
				!RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled)
				return false;

			_pendingNativePropertyUpdates |= property;
			return true;
		}

		void CommitNativePropertyUpdates(IView view)
		{
			if (!_isNativePropertyUpdateBatchActive)
				return;

			var updates = _pendingNativePropertyUpdates;
			_isNativePropertyUpdateBatchActive = false;
			_pendingNativePropertyUpdates = NativePropertyUpdate.None;

			if (updates == NativePropertyUpdate.None || PlatformView is null)
				return;

			var enabled = false;
			var opacity = 0f;
			var translationX = 0f;
			var translationY = 0f;
			var scaleX = 0f;
			var scaleY = 0f;
			var rotation = 0f;
			var rotationX = 0f;
			var rotationY = 0f;

			if ((updates & NativePropertyUpdate.IsEnabled) != 0)
				enabled = view.IsEnabled;

			if ((updates & NativePropertyUpdate.Opacity) != 0)
				opacity = (float)view.Opacity;

			var targetView = this.ToPlatform();
			var context = targetView.Context;

			if ((updates & NativePropertyUpdate.TranslationX) != 0)
				translationX = (float)context.ToPixels(view.TranslationX);

			if ((updates & NativePropertyUpdate.TranslationY) != 0)
				translationY = (float)context.ToPixels(view.TranslationY);

			if ((updates & (NativePropertyUpdate.ScaleX | NativePropertyUpdate.ScaleY)) != 0)
			{
				var scale = view.Scale;
				if (double.IsNaN(scale))
				{
					// The immediate scale mappers also ignore ScaleX/ScaleY while the aggregate scale is NaN.
					updates &= ~(NativePropertyUpdate.ScaleX | NativePropertyUpdate.ScaleY);
				}
				else
				{
					if ((updates & NativePropertyUpdate.ScaleX) != 0)
						scaleX = (float)scale * (float)view.ScaleX;

					if ((updates & NativePropertyUpdate.ScaleY) != 0)
						scaleY = (float)scale * (float)view.ScaleY;
				}
			}

			if ((updates & NativePropertyUpdate.Rotation) != 0)
				rotation = (float)view.Rotation;

			if ((updates & NativePropertyUpdate.RotationX) != 0)
				rotationX = (float)view.RotationX;

			if ((updates & NativePropertyUpdate.RotationY) != 0)
				rotationY = (float)view.RotationY;

			if (updates == NativePropertyUpdate.None)
				return;

			PlatformInterop.UpdateViewProperties(
				PlatformView,
				targetView,
				(int)updates,
				enabled,
				opacity,
				translationX,
				translationY,
				scaleX,
				scaleY,
				rotation,
				rotationX,
				rotationY);

			NativePropertyUpdateBatchFlushCount++;

			if ((updates & NativePropertyUpdate.Opacity) != 0 &&
				targetView is WrapperView wrapperView &&
				wrapperView.Shadow != null &&
				wrapperView.IsLoaded())
			{
				wrapperView.ScheduleInvalidate();
			}
		}

		void OnRootViewSet(object? sender, EventArgs e)
		{
			UpdateValue(nameof(IToolbarElement.Toolbar));
		}

		static partial void MappingFrame(IViewHandler handler, IView view)
		{
			handler.ToPlatform().UpdateAnchorX(view);
			handler.ToPlatform().UpdateAnchorY(view);
		}

		public static void MapTranslationX(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				// Mapped through _InitializeBatchedProperties
				return;
			}

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.TranslationX))
				return;

			handler.ToPlatform().UpdateTranslationX(view);
		}

		public static void MapTranslationY(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				// Mapped through _InitializeBatchedProperties
				return;
			}

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.TranslationY))
				return;

			handler.ToPlatform().UpdateTranslationY(view);
		}

		public static void MapScale(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				// Mapped through _InitializeBatchedProperties
				return;
			}

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.ScaleX | NativePropertyUpdate.ScaleY))
				return;

			handler.ToPlatform().UpdateScale(view);
		}

		public static void MapScaleX(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				// Mapped through _InitializeBatchedProperties
				return;
			}

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.ScaleX))
				return;

			handler.ToPlatform().UpdateScaleX(view);
		}

		public static void MapScaleY(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				// Mapped through _InitializeBatchedProperties
				return;
			}

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.ScaleY))
				return;

			handler.ToPlatform().UpdateScaleY(view);
		}

		public static void MapRotation(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				// Mapped through _InitializeBatchedProperties
				return;
			}

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Rotation))
				return;

			handler.ToPlatform().UpdateRotation(view);
		}

		public static void MapRotationX(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				// Mapped through _InitializeBatchedProperties
				return;
			}

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.RotationX))
				return;

			handler.ToPlatform().UpdateRotationX(view);
		}

		public static void MapRotationY(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				// Mapped through _InitializeBatchedProperties
				return;
			}

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.RotationY))
				return;

			handler.ToPlatform().UpdateRotationY(view);
		}

		public static void MapAnchorX(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				// Mapped through _InitializeBatchedProperties
				return;
			}

			handler.ToPlatform().UpdateAnchorX(view);
		}

		public static void MapAnchorY(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				// Mapped through _InitializeBatchedProperties
				return;
			}

			handler.ToPlatform().UpdateAnchorY(view);
		}

		static partial void MappingSemantics(IViewHandler handler, IView view)
		{
			if (handler.PlatformView == null)
				return;

			AccessibilityDelegateCompat? accessibilityDelegate = null;
			if (handler.PlatformView is View viewPlatform)
				accessibilityDelegate = ViewCompat.GetAccessibilityDelegate(viewPlatform) as MauiAccessibilityDelegateCompat;

			if (handler.PlatformView is not PlatformView platformView)
				return;

			platformView = platformView.GetSemanticPlatformElement();

			var desc = view.Semantics?.Description;
			var hint = view.Semantics?.Hint;

			// We use MauiAccessibilityDelegateCompat to fix the issue of AutomationId breaking accessibility
			// Because AutomationId gets set on the contentDesc we have to clear that out on the accessibility node via
			// the use of our MauiAccessibilityDelegateCompat
			if (!string.IsNullOrWhiteSpace(hint) ||
				!string.IsNullOrWhiteSpace(desc) ||
				!string.IsNullOrWhiteSpace(view.AutomationId))
			{
				if (accessibilityDelegate == null)
				{
					var currentDelegate = ViewCompat.GetAccessibilityDelegate(platformView);
					if (currentDelegate is MauiAccessibilityDelegateCompat)
						currentDelegate = null;

					accessibilityDelegate = new MauiAccessibilityDelegateCompat(currentDelegate)
					{
						Handler = handler
					};

					ViewCompat.SetAccessibilityDelegate(platformView, accessibilityDelegate);
				}

				if (!string.IsNullOrWhiteSpace(hint) ||
					!string.IsNullOrWhiteSpace(desc))
				{
					platformView.ImportantForAccessibility = ImportantForAccessibility.Yes;
				}
			}
			else if (accessibilityDelegate != null)
			{
				ViewCompat.SetAccessibilityDelegate(platformView, null);
			}
		}

		public static void MapToolbar(IViewHandler handler, IView view)
		{
			if (handler.VirtualView is not IToolbarElement te || te.Toolbar == null)
				return;

			MapToolbar(handler, te);
		}

		internal static void MapToolbar(IElementHandler handler, IToolbarElement te)
		{
			if (te.Toolbar == null)
				return;

			var rootManager = handler.MauiContext?.GetNavigationRootManager();
			rootManager?.SetToolbarElement(te);

			var platformView = handler.PlatformView as View;

			_ = handler.MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			var appbarLayout =
				platformView?.FindViewById<ViewGroup>(Resource.Id.navigationlayout_appbar) ??
				rootManager?.RootView?.FindViewById<ViewGroup>(Resource.Id.navigationlayout_appbar);

			var nativeToolBar = te.Toolbar?.ToPlatform(handler.MauiContext);

			if (appbarLayout == null)
			{
				return;
			}

			if (appbarLayout.ChildCount > 0 &&
				appbarLayout.GetChildAt(0) == nativeToolBar)
			{
				return;
			}

			appbarLayout.AddView(nativeToolBar, 0);
		}

		public static void MapContextFlyout(IViewHandler handler, IView view)
		{
		}

		void OnPlatformViewFocusChange(object? sender, PlatformView.FocusChangeEventArgs e)
		{
			VirtualView?.IsFocused = e.HasFocus;
		}

		internal static void MapSafeAreaEdges(IViewHandler handler, IView view)
		{
			if (handler.IsConnectingHandler())
			{
				return;
			}

			if (handler.MauiContext?.Context is null || handler.PlatformView is not View platformView)
			{
				return;
			}

			// Use our static registry approach to find and reset the appropriate listener
			var listener = MauiWindowInsetListener.FindListenerForView(platformView);

			// Check for specific view group types that handle safe area
			if (handler.PlatformView is ContentViewGroup cvg)
			{
				listener?.ResetAppliedSafeAreas(cvg);
				cvg.MarkSafeAreaEdgeConfigurationChanged();
			}
			else if (handler.PlatformView is LayoutViewGroup lvg)
			{
				listener?.ResetAppliedSafeAreas(lvg);
				lvg.MarkSafeAreaEdgeConfigurationChanged();
			}
			else if (handler.PlatformView is MauiScrollView msv)
			{
				listener?.ResetAppliedSafeAreas(msv);
				msv.MarkSafeAreaEdgeConfigurationChanged();
			}

			view.InvalidateMeasure();
		}
	}
}