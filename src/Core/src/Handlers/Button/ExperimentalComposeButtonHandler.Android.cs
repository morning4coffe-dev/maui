#nullable enable

namespace Microsoft.Maui.Handlers
{
	internal sealed class ExperimentalComposeButtonHandler : ViewHandler<IButton, MauiComposeButtonView>
	{
		readonly ComposeButtonCallback _callback;

		public static IPropertyMapper<IButton, ExperimentalComposeButtonHandler> Mapper =
			new PropertyMapper<IButton, ExperimentalComposeButtonHandler>(ViewHandler.ViewMapper)
			{
				[nameof(IText.Text)] = MapText,
				[nameof(IView.IsEnabled)] = MapIsEnabled,
				[nameof(IView.Semantics)] = MapSemantics,
				[nameof(IView.AutomationId)] = MapAutomationId,
			};

		public ExperimentalComposeButtonHandler()
			: base(Mapper)
		{
			_callback = new ComposeButtonCallback(this);
		}

		protected override MauiComposeButtonView CreatePlatformView() =>
			new(Context);

		protected override void ConnectHandler(MauiComposeButtonView platformView)
		{
			_callback.Handler = this;
			platformView.Connect(_callback);
			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(MauiComposeButtonView platformView)
		{
			_callback.Handler = null;
			platformView.Disconnect();
			base.DisconnectHandler(platformView);
		}

		static void MapText(
			ExperimentalComposeButtonHandler handler,
			IButton button) =>
			handler.PlatformView.SetButtonText((button as IText)?.Text);

		static void MapIsEnabled(
			ExperimentalComposeButtonHandler handler,
			IButton button) =>
			handler.PlatformView.SetButtonEnabled(button.IsEnabled);

		static void MapSemantics(
			ExperimentalComposeButtonHandler handler,
			IButton button) =>
			handler.PlatformView.SetSemanticsDescription(button.Semantics?.Description);

		static void MapAutomationId(
			ExperimentalComposeButtonHandler handler,
			IButton button) =>
			handler.PlatformView.SetAutomationId(button.AutomationId);

		sealed class ComposeButtonCallback : Java.Lang.Object, IMauiComposeButtonCallback
		{
			public ComposeButtonCallback(ExperimentalComposeButtonHandler handler)
			{
				Handler = handler;
			}

			public ExperimentalComposeButtonHandler? Handler { get; set; }

			public void OnClick() => Handler?.VirtualView?.Clicked();

			public void OnPressed() => Handler?.VirtualView?.Pressed();

			public void OnReleased() => Handler?.VirtualView?.Released();
		}
	}
}
