#nullable enable
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.Maui.Handlers
{
	public partial class SwitchHandler : ViewHandler<ISwitch, ToggleSwitch>
	{
		protected override ToggleSwitch CreatePlatformView() => new ToggleSwitch() { OffContent = null, OnContent = null };

		public static void MapIsOn(ISwitchHandler handler, ISwitch view)
		{
			handler.PlatformView?.UpdateIsToggled(view);
		}

		public static void MapTrackColor(ISwitchHandler handler, ISwitch view)
		{
			if (handler is SwitchHandler)
			{
				handler.PlatformView?.UpdateTrackColor(view);
			}
		}

		public static void MapThumbColor(ISwitchHandler handler, ISwitch view)
		{
			if (handler is SwitchHandler)
			{
				handler.PlatformView?.UpdateThumbColor(view);
			}
		}

		protected override void DisconnectHandler(ToggleSwitch platformView)
		{
			base.DisconnectHandler(platformView);
			platformView.Toggled -= OnToggled;
			platformView.Loaded -= OnLoaded;
		}

		protected override void ConnectHandler(ToggleSwitch platformView)
		{
			base.ConnectHandler(platformView);
			platformView.Toggled += OnToggled;
			platformView.Loaded += OnLoaded;
		}

		void OnLoaded(object sender, UI.Xaml.RoutedEventArgs e)
		{
			var toggleSwitch = (ToggleSwitch)sender;

			if (toggleSwitch is null)
			{
				return;
			}

			var rootGrid = toggleSwitch.GetDescendantByName<Grid>("SwitchAreaGrid")?.Parent as Grid;
			if (rootGrid is not null && rootGrid.ColumnDefinitions.Count > 0)
			{
				// In the default ToggleSwitch template, the second column (index 1) is only used for spacing 
				// between the toggle knob and the On/Off content area (which is defined in the third column).
				// Since MAUI does not support OnContent/OffContent, this spacing is unnecessary
				// so we set its width to 0 to reduce unwanted layout padding.
				rootGrid.ColumnDefinitions[1].Width = new UI.Xaml.GridLength(0);
			}

#if UNO
			if (System.OperatingSystem.IsBrowser())
			{
				AutomationProperties.SetAccessibilityView(toggleSwitch, AccessibilityView.Content);
				if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(toggleSwitch)))
				{
					AutomationProperties.SetName(toggleSwitch, "Switch");
				}

				HideAccessibilityDescendants(toggleSwitch);
			}
#endif
		}

#if UNO
		static void HideAccessibilityDescendants(UI.Xaml.DependencyObject parent)
		{
			for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
			{
				var child = VisualTreeHelper.GetChild(parent, index);
				if (child is UI.Xaml.FrameworkElement element)
				{
					AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
				}

				HideAccessibilityDescendants(child);
			}
		}
#endif

		// TODO: Make it public in .NET 10.0
		internal static void MapSwitchMinimumWidth(IViewHandler handler, IView view)
		{
			// Update the native ToggleSwitch MinWidth to reflect the MAUI view's MinimumWidth,
			// overriding the default WinUI MinWidth (154) since we're not supporting OnContent and OffContent.
			// This ensures the control does not reserve unnecessary space for labels.
			if (view is ISwitch switchView && handler is SwitchHandler switchHandler)
			{
				switchHandler.PlatformView?.UpdateMinWidth(switchView);
			}
		}

		void OnToggled(object sender, UI.Xaml.RoutedEventArgs e)
		{
			if (VirtualView is null || PlatformView is null || VirtualView.IsOn == PlatformView.IsOn)
			{
				return;
			}

			VirtualView.IsOn = PlatformView.IsOn;
		}
	}
}