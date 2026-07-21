using System;
using System.Collections.Generic;
using System.ComponentModel;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using UIKit;
using PlatformView = UIKit.UIView;

namespace Microsoft.Maui.Handlers
{
	public partial class ViewHandler
	{
		internal const string NativeViewPropertyBatchingSwitch = "Microsoft.Maui.Experimental.NativeViewPropertyBatching";
		internal const string BeginNativePropertyUpdateBatchCommand = "BeginNativePropertyUpdateBatch";
		internal const string CommitNativePropertyUpdateBatchCommand = "CommitNativePropertyUpdateBatch";

		[Flags]
		internal enum NativePropertyUpdate
		{
			None = 0,
			Transformation = 1 << 0,
		}

		bool _nativeViewPropertiesInitialized;
		bool _isNativePropertyUpdateBatchActive;
		NativePropertyUpdate _pendingNativePropertyUpdates;

		internal int NativePropertyUpdateBatchFlushCount { get; private set; }

		internal static void TryInitializeNativeViewProperties(IViewHandler handler, IView view)
		{
			if (!AppContext.TryGetSwitch(NativeViewPropertyBatchingSwitch, out bool isEnabled) ||
				!isEnabled ||
				!handler.IsConnectingHandler() ||
				handler is not ViewHandler viewHandler ||
				handler.PlatformView is not PlatformView platformView)
			{
				return;
			}

			var hasContainer = handler.HasContainer && handler.ContainerView is PlatformView;
			var containerView = hasContainer
				? (PlatformView)handler.ContainerView!
				: platformView;

			platformView.InitializeNativeViewProperties(containerView, hasContainer, view);
			viewHandler._nativeViewPropertiesInitialized = true;
		}

		internal static bool DidInitializeNativeViewProperties(IViewHandler handler) =>
			handler.IsConnectingHandler() &&
			handler is ViewHandler viewHandler &&
			viewHandler._nativeViewPropertiesInitialized;

		partial void DisconnectingHandler(PlatformView platformView)
		{
			_nativeViewPropertiesInitialized = false;
			_isNativePropertyUpdateBatchActive = false;
			_pendingNativePropertyUpdates = NativePropertyUpdate.None;
		}

		static void MapBeginNativePropertyUpdateBatch(IViewHandler handler, IView view, object? args)
		{
			if (!RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled ||
				handler is not ViewHandler viewHandler ||
				viewHandler._isNativePropertyUpdateBatchActive)
			{
				return;
			}

			viewHandler._isNativePropertyUpdateBatchActive = true;
			viewHandler._pendingNativePropertyUpdates = NativePropertyUpdate.None;
		}

		static void MapCommitNativePropertyUpdateBatch(IViewHandler handler, IView view, object? args)
		{
			if (handler is ViewHandler viewHandler)
				viewHandler.CommitNativePropertyUpdates(view);
		}

		bool TryQueueNativePropertyUpdate(NativePropertyUpdate property)
		{
			if (!_isNativePropertyUpdateBatchActive ||
				!RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled)
			{
				return false;
			}

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

			if ((updates & NativePropertyUpdate.Transformation) == 0 || PlatformView is null)
				return;

			UpdateTransformation(this, view);
			NativePropertyUpdateBatchFlushCount++;
		}

		internal void ResetNativePropertyUpdateDiagnostics()
		{
			NativePropertyUpdateBatchFlushCount = 0;
		}

		[System.Runtime.Versioning.SupportedOSPlatform("ios13.0")]
		public static void MapContextFlyout(IViewHandler handler, IView view)
		{
#if MACCATALYST
			if (view is IContextFlyoutElement contextFlyoutContainer)
			{
				MapContextFlyout(handler, contextFlyoutContainer);
			}
#endif
		}

#if MACCATALYST
		[System.Runtime.Versioning.SupportedOSPlatform("ios13.0")]
		internal static void MapContextFlyout(IElementHandler handler, IContextFlyoutElement contextFlyoutContainer)
		{
			_ = handler.MauiContext ?? throw new InvalidOperationException($"The handler's {nameof(handler.MauiContext)} cannot be null.");

			if (handler.PlatformView is PlatformView uiView)
			{
				MauiUIContextMenuInteraction? currentInteraction = null;

				foreach (var interaction in uiView.Interactions)
				{
					if (interaction is MauiUIContextMenuInteraction menuInteraction)
						currentInteraction = menuInteraction;
				}

				if (contextFlyoutContainer.ContextFlyout != null)
				{
					if (currentInteraction == null)
						uiView.AddInteraction(new MauiUIContextMenuInteraction(handler));
				}
				else if (currentInteraction != null)
				{
					uiView.RemoveInteraction(currentInteraction);
				}
			}
		}
#endif

		static partial void MappingFrame(IViewHandler handler, IView view)
		{
			UpdateTransformation(handler, view);
		}

		public static void MapTranslationX(IViewHandler handler, IView view)
		{
			// During the initial setup, MappingFrame will take care of everything
			if (handler.IsConnectingHandler())
				return;

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Transformation))
			{
				return;
			}

			UpdateTransformation(handler, view);
		}

		public static void MapTranslationY(IViewHandler handler, IView view)
		{
			// During the initial setup, MappingFrame will take care of everything
			if (handler.IsConnectingHandler())
				return;

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Transformation))
			{
				return;
			}

			UpdateTransformation(handler, view);
		}

		public static void MapScale(IViewHandler handler, IView view)
		{
			// During the initial setup, MappingFrame will take care of everything
			if (handler.IsConnectingHandler())
				return;

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Transformation))
			{
				return;
			}

			UpdateTransformation(handler, view);
		}

		public static void MapScaleX(IViewHandler handler, IView view)
		{
			// During the initial setup, MappingFrame will take care of everything
			if (handler.IsConnectingHandler())
				return;

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Transformation))
			{
				return;
			}

			UpdateTransformation(handler, view);
		}

		public static void MapScaleY(IViewHandler handler, IView view)
		{
			// During the initial setup, MappingFrame will take care of everything
			if (handler.IsConnectingHandler())
				return;

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Transformation))
			{
				return;
			}

			UpdateTransformation(handler, view);
		}

		public static void MapRotation(IViewHandler handler, IView view)
		{
			// During the initial setup, MappingFrame will take care of everything
			if (handler.IsConnectingHandler())
				return;

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Transformation))
			{
				return;
			}

			UpdateTransformation(handler, view);
		}

		public static void MapRotationX(IViewHandler handler, IView view)
		{
			// During the initial setup, MappingFrame will take care of everything
			if (handler.IsConnectingHandler())
				return;

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Transformation))
			{
				return;
			}

			UpdateTransformation(handler, view);
		}

		public static void MapRotationY(IViewHandler handler, IView view)
		{
			// During the initial setup, MappingFrame will take care of everything
			if (handler.IsConnectingHandler())
				return;

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Transformation))
			{
				return;
			}

			UpdateTransformation(handler, view);
		}

		public static void MapAnchorX(IViewHandler handler, IView view)
		{
			// During the initial setup, MappingFrame will take care of everything
			if (handler.IsConnectingHandler())
				return;

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Transformation))
			{
				return;
			}

			UpdateTransformation(handler, view);
		}

		public static void MapAnchorY(IViewHandler handler, IView view)
		{
			// During the initial setup, MappingFrame will take care of everything
			if (handler.IsConnectingHandler())
				return;

			if (handler is ViewHandler viewHandler &&
				viewHandler.TryQueueNativePropertyUpdate(NativePropertyUpdate.Transformation))
			{
				return;
			}

			UpdateTransformation(handler, view);
		}

		internal static void UpdateTransformation(IViewHandler handler, IView view)
		{
			handler.ToPlatform().UpdateTransformation(view);
		}

		internal static void MapSafeAreaEdges(IViewHandler handler, IView view)
		{
			view.InvalidateMeasure();
		}
	}
}