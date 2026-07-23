#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Controls.Diagnostics;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Graphics;
using UIKit;

namespace Microsoft.Maui.Controls.Platform
{
	internal partial class AlertManager
	{
		private partial IAlertManagerSubscription CreateSubscription(IMauiContext mauiContext)
		{
			var platformWindow = mauiContext.GetPlatformWindow();
			return new AlertRequestHelper(Window, platformWindow);
		}

		internal sealed partial class AlertRequestHelper
		{
			const float AlertPadding = 10.0f;

			int _busyCount;

			internal AlertRequestHelper(Window virtualView, UIWindow platformView)
			{
				VirtualView = virtualView;
				PlatformView = platformView;
			}

			public Window VirtualView { get; }

			public UIWindow PlatformView { get; }

			// TODO: This method is obsolete in .NET 10 and will be removed in .NET 11.
			public partial void OnPageBusy(Page sender, bool enabled)
			{
				_busyCount = Math.Max(0, enabled ? _busyCount + 1 : _busyCount - 1);
#pragma warning disable CA1416, CA1422 // TODO:  'UIApplication.NetworkActivityIndicatorVisible' is unsupported on: 'ios' 13.0 and later
				UIApplication.SharedApplication.NetworkActivityIndicatorVisible = _busyCount > 0;
#pragma warning restore CA1416, CA1422
			}

			public partial void OnAlertRequested(Page sender, AlertArguments arguments)
			{
				if (!PageIsInThisWindow(sender))
					return;

				PresentAlert(sender, arguments);
			}

			public partial void OnPromptRequested(Page sender, PromptArguments arguments)
			{
				if (!PageIsInThisWindow(sender))
					return;

				PresentPrompt(sender, arguments);
			}

			public partial void OnActionSheetRequested(Page sender, ActionSheetArguments arguments)
			{
				if (!PageIsInThisWindow(sender))
					return;

				PresentActionSheet(sender, arguments);
			}

			void PresentAlert(Page sender, AlertArguments arguments)
			{
				var alert = UIAlertController.Create(arguments.Title, arguments.Message, UIAlertControllerStyle.Alert);
				var oldFrame = alert.View.Frame;
				alert.View.Frame = new RectF((float)oldFrame.X, (float)oldFrame.Y, (float)oldFrame.Width, (float)oldFrame.Height - AlertPadding * 2);

				if (arguments.Cancel != null)
				{
					alert.AddAction(UIAlertAction.Create(arguments.Cancel, UIAlertActionStyle.Cancel,
						_ => arguments.SetResult(false)));
				}

				if (arguments.Accept != null)
				{
					alert.AddAction(UIAlertAction.Create(arguments.Accept, UIAlertActionStyle.Default,
						_ => arguments.SetResult(true)));
				}

				PresentPopUp(sender, VirtualView, PlatformView, alert, completion: arguments.Result.Task);
			}

			void PresentPrompt(Page sender, PromptArguments arguments)
			{
				var alert = UIAlertController.Create(arguments.Title, arguments.Message, UIAlertControllerStyle.Alert);
				alert.AddTextField(uiTextField =>
				{
					uiTextField.Placeholder = arguments.Placeholder;
					uiTextField.Text = arguments.InitialValue;
					if (arguments.MaxLength > -1 && (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26)))
					{
						uiTextField.ShouldChangeCharactersInRanges = (textField, ranges, replacementString) =>
						{
							var currentLength = textField.Text?.Length ?? 0;
							var totalRangeLength = 0;
							for (int i = 0; i < ranges.Length; i++)
							{
								var range = ranges[i].RangeValue;
								totalRangeLength += (int)range.Length;
							}

							var newLength = currentLength - totalRangeLength + replacementString.Length;
							return newLength <= arguments.MaxLength;
						};
					}
					else
					{
						uiTextField.ShouldChangeCharacters = (field, range, replacementString) => arguments.MaxLength <= -1 || field.Text.Length + replacementString.Length - range.Length <= arguments.MaxLength;
					}
					uiTextField.ApplyKeyboard(arguments.Keyboard);
				});

				var oldFrame = alert.View.Frame;
				alert.View.Frame = new RectF((float)oldFrame.X, (float)oldFrame.Y, (float)oldFrame.Width, (float)oldFrame.Height - AlertPadding * 2);

				alert.AddAction(UIAlertAction.Create(arguments.Cancel, UIAlertActionStyle.Cancel, _ => arguments.SetResult(null)));
				alert.AddAction(UIAlertAction.Create(arguments.Accept, UIAlertActionStyle.Default, _ => arguments.SetResult(alert.TextFields[0].Text)));

				PresentPopUp(sender, VirtualView, PlatformView, alert, completion: arguments.Result.Task);
			}


			void PresentActionSheet(Page sender, ActionSheetArguments arguments)
			{
				var alert = UIAlertController.Create(arguments.Title, null, UIAlertControllerStyle.ActionSheet);

				// Clicking outside of an ActionSheet is an implicit cancel on iPads. If we don't handle it, it freezes the app.
				if (arguments.Cancel != null || UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad)
				{
					alert.AddAction(UIAlertAction.Create(arguments.Cancel ?? "", UIAlertActionStyle.Cancel, _ => arguments.SetResult(arguments.Cancel)));
				}

				if (arguments.Destruction != null)
				{
					alert.AddAction(UIAlertAction.Create(arguments.Destruction, UIAlertActionStyle.Destructive, _ => arguments.SetResult(arguments.Destruction)));
				}

				foreach (var label in arguments.Buttons)
				{
					if (label == null)
						continue;

					var blabel = label;

					alert.AddAction(UIAlertAction.Create(blabel, UIAlertActionStyle.Default, _ => arguments.SetResult(blabel)));
				}

				PresentPopUp(sender, VirtualView, PlatformView, alert, arguments, arguments.Result.Task);
			}

			static void PresentPopUp(
				Page sender,
				Window virtualView,
				UIWindow platformView,
				UIAlertController alert,
				ActionSheetArguments arguments = null,
				Task completion = null)
			{
				UIWindow presentingWindow = platformView;
				var registration = new AlertRegistration();
				registration.Register(
					sender,
					alert.View,
					NativeElementRoles.Dialog,
					NativeElementDiscriminators.RealizedView);
				if (alert.TextFields is not null)
				{
					foreach (var textField in alert.TextFields)
					{
						registration.Register(
							sender,
							textField,
							NativeElementRoles.Dialog,
							NativeElementDiscriminators.RealizedView);
					}
				}
				foreach (var action in alert.Actions)
				{
					registration.Register(
							sender,
							action,
							NativeElementRoles.DialogAction,
							NativeElementDiscriminators.LogicalModel);
				}
				completion?.ContinueWith(
						_ => platformView.BeginInvokeOnMainThread(registration.Dispose),
						TaskScheduler.Default);

				if (sender.Handler is IPlatformViewHandler pvh &&
					pvh.PlatformView?.Window is UIWindow senderPageWindow &&
					senderPageWindow != platformView &&
					senderPageWindow.RootViewController is not null)
				{
					presentingWindow = senderPageWindow;
				}

				if (UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad &&
					arguments is not null &&
					alert.PopoverPresentationController is not null &&
					platformView.RootViewController?.View is not null)
				{
					var topViewController = GetTopUIViewController(presentingWindow);
					UIDevice.CurrentDevice.BeginGeneratingDeviceOrientationNotifications();
					var observer = NSNotificationCenter.DefaultCenter.AddObserver(UIDevice.OrientationDidChangeNotification,
						n => alert.PopoverPresentationController.SourceRect = new CGRect(0, 0, topViewController.View.Bounds.Height, topViewController.View.Bounds.Width));

					arguments.Result.Task.ContinueWith(t =>
					{
						NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
						UIDevice.CurrentDevice.EndGeneratingDeviceOrientationNotifications();
					}, TaskScheduler.FromCurrentSynchronizationContext());

					alert.PopoverPresentationController.SourceView = topViewController.View;
					alert.PopoverPresentationController.SourceRect = topViewController.View.Bounds;
					alert.PopoverPresentationController.PermittedArrowDirections = 0; // No arrow
				}

				presentingWindow.BeginInvokeOnMainThread(() =>
				{
					var presentation = GetTopUIViewController(presentingWindow)
						.PresentViewControllerAsync(alert, true);
					presentation.ContinueWith(
						task =>
						{
							platformView.BeginInvokeOnMainThread(() =>
							{
								if (task.IsFaulted || task.IsCanceled)
									registration.Dispose();
								else
								{
									registration.RegisterAlertActionViews(sender, alert);
									if (alert.PresentationController is not null)
										registration.Attach(alert.PresentationController);
								}
							});
						},
						TaskScheduler.Default);
					presentation.FireAndForget(virtualView?.Handler?.MauiContext?.CreateLogger<AlertManager>());
				});
			}

			sealed class AlertRegistration : IDisposable
			{
				readonly NativeElementRegistrationSet _registrations = new NativeElementRegistrationSet();
				readonly AlertDismissalObserver _dismissalObserver;
				UIPresentationController _presentationController;
				int _disposed;

				public AlertRegistration()
				{
					_dismissalObserver = new AlertDismissalObserver(Dispose);
				}

				public void Register(
					object owner,
					object nativeElement,
					string role,
					string discriminator)
				{
					if (Volatile.Read(ref _disposed) != 0)
						return;

					_registrations.Register(owner, nativeElement, role, discriminator);
				}

				public void RegisterAlertActionViews(object owner, UIAlertController alert)
				{
					if (Volatile.Read(ref _disposed) != 0)
						return;

					alert.View.LayoutIfNeeded();
					var actionsByTitle = alert.Actions
						.Select(action => action.Title)
						.Where(title => !string.IsNullOrEmpty(title))
						.GroupBy(title => title, StringComparer.Ordinal)
						.Where(group => group.Count() == 1)
						.ToDictionary(
							group => group.Key,
							group => alert.Actions.Single(action =>
								string.Equals(action.Title, group.Key, StringComparison.Ordinal)),
							StringComparer.Ordinal);
					var actionControls = FindAlertActionControls(alert.View, actionsByTitle.Keys)
						.GroupBy(GetControlTitle, StringComparer.Ordinal)
						.Where(group => group.Count() == 1)
						.Select(group => group.Single());
					foreach (var control in actionControls)
					{
						var title = GetControlTitle(control);
						_registrations.Unregister(actionsByTitle[title]);
						_registrations.Register(
							owner,
							control,
							NativeElementRoles.DialogAction,
							NativeElementDiscriminators.RealizedView);
					}
				}

				static IEnumerable<UIControl> FindAlertActionControls(
					UIView view,
					ICollection<string> actionTitles)
				{
					if (view is UIControl control)
					{
						var title = GetControlTitle(control);
						if (!string.IsNullOrEmpty(title) && actionTitles.Contains(title))
						{
							yield return control;
							yield break;
						}
					}

					foreach (var subview in view.Subviews)
					{
						foreach (var actionControl in FindAlertActionControls(subview, actionTitles))
							yield return actionControl;
					}
				}

				static string GetControlTitle(UIControl control)
				{
					if (!string.IsNullOrEmpty(control.AccessibilityLabel))
						return control.AccessibilityLabel;
					if (control is UIButton button
						&& !string.IsNullOrEmpty(button.Title(UIControlState.Normal)))
					{
						return button.Title(UIControlState.Normal);
					}

					return control.Subviews
						.OfType<UILabel>()
						.Select(label => label.Text)
						.FirstOrDefault(text => !string.IsNullOrEmpty(text));
				}

				public void Attach(UIPresentationController presentationController)
				{
					if (Volatile.Read(ref _disposed) != 0)
						return;

					_presentationController = presentationController;
					if (presentationController.Delegate is null)
						presentationController.Delegate = _dismissalObserver;
				}

				public void Dispose()
				{
					if (Interlocked.Exchange(ref _disposed, 1) != 0)
						return;

					if (_presentationController?.Delegate == _dismissalObserver)
						_presentationController.Delegate = null;
					_presentationController = null;
					_registrations.Dispose();
				}
			}

			sealed class AlertDismissalObserver : UIAdaptivePresentationControllerDelegate
			{
				Action _dismissed;

				public AlertDismissalObserver(Action dismissed)
				{
					_dismissed = dismissed;
				}

				public override void DidDismiss(UIPresentationController presentationController)
				{
					_dismissed?.Invoke();
					_dismissed = null;
				}
			}

			static UIViewController GetTopUIViewController(UIWindow platformWindow)
			{
				var topUIViewController = platformWindow.RootViewController;
				while (topUIViewController?.PresentedViewController is not null &&
					   !topUIViewController.PresentedViewController.IsBeingDismissed)
				{
					topUIViewController = topUIViewController.PresentedViewController;
				}

				return topUIViewController;
			}

			bool PageIsInThisWindow(Page page) =>
				page?.Window == VirtualView;
		}
	}
}