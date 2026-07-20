using System;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Microsoft.Maui.Platform
{
	/// <summary>
	/// Behavior that automatically resizes a CALayer to match its superlayer's bounds
	/// </summary>
	[BaseType(typeof(NSObject), Name = "MauiCALayerAutosizeToSuperLayerBehavior")]
	[Internal]
	interface MauiCALayerAutosizeToSuperLayerBehavior
	{
		/// <summary>
		/// Attaches this behavior to the given layer.
		/// The layer must have a superlayer when this method is called.
		/// The layer's frame will be kept in sync with the superlayer's bounds.
		/// </summary>
		/// <param name="layer">The layer that needs to be resized to match the superlayer's bounds.</param>
		[Export("attachWithLayer:")]
		MauiCALayerAutosizeToSuperLayerResult Attach(CALayer layer);

		/// <summary>
		/// Detaches this behavior from the current layer and stops observing
		/// </summary>
		[Export("detach")]
		void Detach();
	}

	[BaseType(typeof(NSObject), Name = "MauiViewPropertyBatcher")]
	[Internal]
	interface MauiViewPropertyBatcher
	{
		[Static]
		[Export("applyWithPlatformView:containerView:hasContainer:hidden:semanticContentAttribute:enabled:applyOpacity:opacity:")]
		bool Apply(
			UIView platformView,
			UIView containerView,
			bool hasContainer,
			bool hidden,
			UISemanticContentAttribute semanticContentAttribute,
			bool enabled,
			bool applyOpacity,
			double opacity);
	}

	[Protocol, Model]
	[BaseType(typeof(NSObject))]
	[Internal]
	interface MauiSwiftUIButtonCallback
	{
		[Abstract]
		[Export("onClick")]
		void OnClick();

		[Abstract]
		[Export("onPressed")]
		void OnPressed();

		[Abstract]
		[Export("onReleased")]
		void OnReleased();
	}

	[BaseType(typeof(UIViewController), Name = "MauiSwiftUIButtonController")]
	[Internal]
	interface MauiSwiftUIButtonController
	{
		[Export("init")]
		NativeHandle Constructor();

		[Export("buttonText")]
		string ButtonText { get; set; }

		[Export("buttonEnabled")]
		bool ButtonEnabled { get; set; }

		[Export("semanticsDescription")]
		string SemanticsDescription { get; set; }

		[Export("semanticsHint")]
		string SemanticsHint { get; set; }

		[Export("automationId")]
		string AutomationId { get; set; }

		[Export("platformView")]
		UIView PlatformView { get; }

		[Export("disconnectedForDiagnostics")]
		bool DisconnectedForDiagnostics { get; }

		[Export("connectWithCallback:")]
		void Connect(IMauiSwiftUIButtonCallback callback);

		[Export("disconnect")]
		void Disconnect();

		[Export("performClickForDiagnostics")]
		void PerformClickForDiagnostics();

		[Export("performPressedForDiagnostics")]
		void PerformPressedForDiagnostics();

		[Export("performReleasedForDiagnostics")]
		void PerformReleasedForDiagnostics();

		[Export("sizeThatFits:")]
		CGSize SizeThatFits(CGSize size);
	}
}