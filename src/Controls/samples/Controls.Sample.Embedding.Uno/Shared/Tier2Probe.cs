using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using MauiContentPage = Microsoft.Maui.Controls.ContentPage;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiNavigationPage = Microsoft.Maui.Controls.NavigationPage;
using MauiPage = Microsoft.Maui.Controls.Page;

namespace Maui.Controls.Sample.Uno;

/// <summary>
/// Drives the window-scoped MAUI features from code and records what actually happened, so Tier 2 can be
/// verified without UI automation.
/// </summary>
/// <remarks>
/// Enabled by setting the <c>MAUI_UNO_TIER2_PROBE</c> environment variable to <c>1</c>. Results are written
/// to <c>tier2-probe.log</c> in the temp directory.
/// </remarks>
internal static class Tier2Probe
{
	public const string EnableVariable = "MAUI_UNO_TIER2_PROBE";

	public static bool IsEnabled =>
		string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal);

	public static string LogPath => Path.Combine(Path.GetTempPath(), "tier2-probe.log");

	public static async Task<string> RunAsync(MauiEmbeddingSession session, MauiPage page, XamlRoot? xamlRoot)
	{
		var report = new StringBuilder();

		// Flushed after every section: the off-UI-thread check intentionally leaves a dialog on screen,
		// so a single write at the end would lose everything before it.
		void Log(string line)
		{
			report.AppendLine(line);
			Flush(report);
		}

		try
		{
			Log(string.Format(
				CultureInfo.InvariantCulture,
				"window.Page is the island page: {0}",
				ReferenceEquals(session.EmbeddedWindow?.Page, page)));

			await ProbeAlertAsync(page, xamlRoot, Log);
			await ProbeModalAsync(page, Log);
			await ProbeNavigationAsync(page, Log);

			// Last: this one cannot dismiss its own dialog, so anything after it would never run.
			await ProbeAlertOffUiThreadAsync(page, xamlRoot, Log);
		}
		catch (Exception ex)
		{
			Log($"probe aborted: {ex.GetType().Name}: {ex.Message}");
		}

		return report.ToString();
	}

	static void Flush(StringBuilder report)
	{
		try
		{
			File.WriteAllText(LogPath, report.ToString());
		}
		catch (Exception)
		{
			// Diagnostics only, and the browser head has no writable temp directory.
		}
	}

	static async Task ProbeAlertAsync(MauiPage page, XamlRoot? xamlRoot, Action<string> log)
	{
		try
		{
			// Not awaited yet: a shown dialog only completes once it is dismissed.
			var alertTask = page.DisplayAlertAsync("probe", "probe body", "OK");

			await Task.Delay(1500);

			var openPopups = xamlRoot is null ? 0 : CountOpenPopups(xamlRoot, log);
			log($"alert opened a popup: {openPopups > 0} (count {openPopups})");

			if (xamlRoot is not null)
			{
				ClosePopups(xamlRoot);
			}

			var completed = await Task.WhenAny(alertTask, Task.Delay(3000)) == alertTask;
			log($"alert task completed after dismissal: {completed}");
		}
		catch (Exception ex)
		{
			log($"alert FAILED: {ex.GetType().Name}: {ex.Message}");
		}
	}

	// Regression test for a crash found during QA: requesting an alert whose state machine runs on a
	// thread-pool thread made Uno materialize the ContentDialog template off the UI thread. Because
	// AlertManager.OnAlertRequested is async void, the resulting exception was unhandled and killed the
	// process rather than surfacing on the awaited call.
	static async Task ProbeAlertOffUiThreadAsync(MauiPage page, XamlRoot? xamlRoot, Action<string> log)
	{
		try
		{
			Task? alertTask = null;

			// Block body on purpose: an expression body returns the Task, which binds Task.Run's
			// Func<Task> overload and unwraps it, so the await would block until the dialog is dismissed.
			await Task.Run(() =>
			{
				alertTask = page.DisplayAlertAsync("off-thread probe", "requested off the UI thread", "OK");
			});

			await Task.Delay(1500);

			var openPopups = xamlRoot is null ? 0 : CountOpenPopups(xamlRoot, log);
			log($"off-UI-thread alert opened a popup: {openPopups > 0}");

			if (xamlRoot is not null)
			{
				ClosePopups(xamlRoot);
			}

			var completed = alertTask is not null && await Task.WhenAny(alertTask, Task.Delay(3000)) == alertTask;
			log($"off-UI-thread alert completed without crashing: {completed}");
			log("(the off-UI-thread dialog is left on screen; dismiss it with OK)");
		}
		catch (Exception ex)
		{
			log($"off-UI-thread alert FAILED: {ex.GetType().Name}: {ex.Message}");
		}
	}

	static async Task ProbeModalAsync(MauiPage page, Action<string> log)
	{
		try
		{
			var modal = new MauiContentPage
			{
				Title = "probe modal",
				Content = new MauiLabel { Text = "probe modal content" },
			};

			await page.Navigation.PushModalAsync(modal);
			log($"PushModalAsync returned: OK (virtual stack depth {page.Navigation.ModalStack.Count})");

			// The virtual stack can be populated even when the platform never realized the page, so the
			// only trustworthy evidence is a live platform view attached to a XamlRoot.
			log($"modal handler created: {modal.Handler is not null}");

			var platformView = modal.Handler?.PlatformView as FrameworkElement;
			log($"modal platform view: {platformView?.GetType().Name ?? "none"}");
			log($"modal attached to XamlRoot: {platformView?.XamlRoot is not null}");
			log($"modal actually rendered: {platformView is not null && platformView.ActualHeight > 0}");

			await page.Navigation.PopModalAsync();
			log("PopModalAsync: OK");
		}
		catch (Exception ex)
		{
			log($"modal FAILED: {ex.GetType().Name}: {ex.Message}");
		}
	}

	static async Task ProbeNavigationAsync(MauiPage page, Action<string> log)
	{
		var navigationPage = page as MauiNavigationPage;

		if (navigationPage is null)
		{
			log("stack navigation: skipped (window page is not a NavigationPage)");
			return;
		}

		try
		{
			var pushed = new MauiContentPage
			{
				Title = "probe pushed page",
				Content = new MauiLabel { Text = "probe pushed content" },
			};

			await navigationPage.Navigation.PushAsync(pushed);
			log($"PushAsync returned: OK (stack depth {navigationPage.Navigation.NavigationStack.Count})");

			// NavigationPage runs a transition, so the pushed page is not measured on the frame the push
			// completes. Give the layout pass a chance before judging whether it rendered.
			await Task.Delay(750);

			var platformView = pushed.Handler?.PlatformView as FrameworkElement;
			log($"pushed page handler created: {pushed.Handler is not null}");
			log($"pushed page attached to XamlRoot: {platformView?.XamlRoot is not null}");
			log($"pushed page actually rendered: {platformView is not null && platformView.ActualHeight > 0}");

			await navigationPage.Navigation.PopAsync();
			log("PopAsync: OK");
		}
		catch (Exception ex)
		{
			log($"stack navigation FAILED: {ex.GetType().Name}: {ex.Message}");
		}
	}

	static int CountOpenPopups(XamlRoot xamlRoot, Action<string> log)
	{
		try
		{
			return VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Count;
		}
		catch (Exception ex)
		{
			log($"popup inspection unavailable: {ex.GetType().Name}");
			return 0;
		}
	}

	static void ClosePopups(XamlRoot xamlRoot)
	{
		try
		{
			// ContentDialog must be dismissed through Hide(); closing the hosting popup leaves the awaited
			// ShowAsync task pending, which would stall everything queued behind it.
			HideDialogs(xamlRoot.Content, 0);

			foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
			{
				if (popup.Child is ContentDialog dialog)
				{
					dialog.Hide();
				}
				else
				{
					popup.IsOpen = false;
				}
			}
		}
		catch (Exception)
		{
			// Best effort only.
		}
	}

	static void HideDialogs(DependencyObject? node, int depth)
	{
		if (node is null || depth > 40)
		{
			return;
		}

		if (node is ContentDialog dialog)
		{
			dialog.Hide();
			return;
		}

		var count = VisualTreeHelper.GetChildrenCount(node);

		for (var i = 0; i < count; i++)
		{
			HideDialogs(VisualTreeHelper.GetChild(node, i), depth + 1);
		}
	}
}
