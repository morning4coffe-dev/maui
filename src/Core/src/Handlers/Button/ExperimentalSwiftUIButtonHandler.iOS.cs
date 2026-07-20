#nullable enable
using Microsoft.Maui.Platform;
using UIKit;

namespace Microsoft.Maui.Handlers
{
	internal sealed class ExperimentalSwiftUIButtonHandler : ViewHandler<IButton, UIView>
	{
		readonly SwiftUIButtonCallback _callback;
		MauiSwiftUIButtonController? _controller;

		public static IPropertyMapper<IButton, ExperimentalSwiftUIButtonHandler> Mapper =
			new PropertyMapper<IButton, ExperimentalSwiftUIButtonHandler>(ViewHandler.ViewMapper)
			{
				[nameof(IText.Text)] = MapText,
				[nameof(IView.IsEnabled)] = MapIsEnabled,
				[nameof(IView.Semantics)] = MapSemantics,
				[nameof(IView.AutomationId)] = MapAutomationId,
			};

		public ExperimentalSwiftUIButtonHandler()
			: base(Mapper)
		{
			_callback = new SwiftUIButtonCallback(this);
		}

		internal MauiSwiftUIButtonController Controller =>
			_controller ?? throw new InvalidOperationException("The native controller is not connected.");

		protected override UIView CreatePlatformView()
		{
			_controller = new MauiSwiftUIButtonController();
			return _controller.PlatformView;
		}

		protected override void ConnectHandler(UIView platformView)
		{
			_callback.Handler = this;
			Controller.Connect(_callback);
			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(UIView platformView)
		{
			_callback.Handler = null;
			Controller.Disconnect();
			base.DisconnectHandler(platformView);
		}

		static void MapText(
			ExperimentalSwiftUIButtonHandler handler,
			IButton button) =>
			handler.Controller.ButtonText = (button as IText)?.Text ?? string.Empty;

		static void MapIsEnabled(
			ExperimentalSwiftUIButtonHandler handler,
			IButton button) =>
			handler.Controller.ButtonEnabled = button.IsEnabled;

		static void MapSemantics(
			ExperimentalSwiftUIButtonHandler handler,
			IButton button)
		{
			handler.Controller.SemanticsDescription =
				button.Semantics?.Description ?? string.Empty;
			handler.Controller.SemanticsHint =
				button.Semantics?.Hint ?? string.Empty;
		}

		static void MapAutomationId(
			ExperimentalSwiftUIButtonHandler handler,
			IButton button) =>
			handler.Controller.AutomationId = button.AutomationId ?? string.Empty;

		sealed class SwiftUIButtonCallback : MauiSwiftUIButtonCallback
		{
			public SwiftUIButtonCallback(ExperimentalSwiftUIButtonHandler handler)
			{
				Handler = handler;
			}

			public ExperimentalSwiftUIButtonHandler? Handler { get; set; }

			public override void OnClick() => Handler?.VirtualView?.Clicked();

			public override void OnPressed() => Handler?.VirtualView?.Pressed();

			public override void OnReleased() => Handler?.VirtualView?.Released();
		}
	}
}
