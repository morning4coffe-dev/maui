#nullable enable
using System;
using Microsoft.Maui.ApplicationModel;
using Windows.Graphics.Display;
using Windows.System.Display;

namespace Microsoft.Maui.Devices
{
	partial class DeviceDisplayImplementation
	{
		readonly object locker = new();
		DisplayInformation? subscribedDisplay;
		DisplayRequest? displayRequest;

		protected override bool GetKeepScreenOn()
		{
			lock (locker)
				return displayRequest is not null;
		}

		protected override void SetKeepScreenOn(bool keepScreenOn)
		{
			lock (locker)
			{
				if (!SupportsDisplayRequest())
				{
					if (keepScreenOn)
					{
						throw new FeatureNotSupportedException(
							"Keeping the screen on is not supported by this Uno host.");
					}

					return;
				}

				if (keepScreenOn && displayRequest is null)
				{
					var request = new DisplayRequest();
					request.RequestActive();
					displayRequest = request;
				}
				else if (!keepScreenOn && displayRequest is not null)
				{
					displayRequest.RequestRelease();
					displayRequest = null;
				}
			}
		}

		protected override DisplayInfo GetMainDisplayInfo()
		{
			var display = DisplayInformation.GetForCurrentView();
			return new DisplayInfo(
				display.ScreenWidthInRawPixels,
				display.ScreenHeightInRawPixels,
				display.LogicalDpi / 96d,
				GetOrientation(display.CurrentOrientation),
				GetRotation(display.NativeOrientation, display.CurrentOrientation));
		}

		protected override void StartScreenMetricsListeners()
		{
			if (subscribedDisplay is not null)
				return;

			subscribedDisplay = DisplayInformation.GetForCurrentView();
			subscribedDisplay.OrientationChanged += OnDisplayInformationChanged;
			subscribedDisplay.DpiChanged += OnDisplayInformationChanged;
		}

		protected override void StopScreenMetricsListeners()
		{
			if (subscribedDisplay is null)
				return;

			subscribedDisplay.OrientationChanged -= OnDisplayInformationChanged;
			subscribedDisplay.DpiChanged -= OnDisplayInformationChanged;
			subscribedDisplay = null;
		}

		void OnDisplayInformationChanged(DisplayInformation sender, object args) =>
			OnMainDisplayInfoChanged();

		static bool SupportsDisplayRequest() =>
			OperatingSystem.IsAndroid() ||
			OperatingSystem.IsBrowser() ||
			OperatingSystem.IsIOS() ||
			OperatingSystem.IsMacCatalyst();

		static DisplayOrientation GetOrientation(DisplayOrientations orientation) =>
			orientation switch
			{
				DisplayOrientations.Portrait or DisplayOrientations.PortraitFlipped => DisplayOrientation.Portrait,
				DisplayOrientations.Landscape or DisplayOrientations.LandscapeFlipped => DisplayOrientation.Landscape,
				_ => DisplayOrientation.Unknown,
			};

		static DisplayRotation GetRotation(
			DisplayOrientations nativeOrientation,
			DisplayOrientations currentOrientation) =>
			nativeOrientation switch
			{
				DisplayOrientations.Portrait => currentOrientation switch
				{
					DisplayOrientations.Portrait => DisplayRotation.Rotation0,
					DisplayOrientations.Landscape => DisplayRotation.Rotation90,
					DisplayOrientations.PortraitFlipped => DisplayRotation.Rotation180,
					DisplayOrientations.LandscapeFlipped => DisplayRotation.Rotation270,
					_ => DisplayRotation.Unknown,
				},
				DisplayOrientations.Landscape => currentOrientation switch
				{
					DisplayOrientations.Landscape => DisplayRotation.Rotation0,
					DisplayOrientations.Portrait => DisplayRotation.Rotation90,
					DisplayOrientations.LandscapeFlipped => DisplayRotation.Rotation180,
					DisplayOrientations.PortraitFlipped => DisplayRotation.Rotation270,
					_ => DisplayRotation.Unknown,
				},
				_ => currentOrientation switch
				{
					DisplayOrientations.Landscape => DisplayRotation.Rotation0,
					DisplayOrientations.PortraitFlipped => DisplayRotation.Rotation90,
					DisplayOrientations.LandscapeFlipped => DisplayRotation.Rotation180,
					DisplayOrientations.Portrait => DisplayRotation.Rotation270,
					_ => DisplayRotation.Unknown,
				},
			};
	}
}
