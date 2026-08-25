using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using MauiContentPage = Microsoft.Maui.Controls.ContentPage;
using MauiLabel = Microsoft.Maui.Controls.Label;
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

		void Log(string line) => report.AppendLine(line);

		try
		{
			Log(string.Format(
				CultureInfo.InvariantCulture,
				"window.Page is the island page: {0}",
				ReferenceEquals(session.EmbeddedWindow?.Page, page)));

			await ProbeAlertAsync(page, xamlRoot, Log);
			await ProbeModalAsync(page, Log);
		}
		catch (Exception ex)
		{
			Log($"probe aborted: {ex.GetType().Name}: {ex.Message}");
		}

		var text = report.ToString();

		try
		{
			File.WriteAllText(LogPath, text);
		}
		catch (Exception)
		{
			// Diagnostics only, and the browser head has no writable temp directory.
		}

		return text;
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
			foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
			{
				popup.IsOpen = false;
			}
		}
		catch (Exception)
		{
			// Best effort only.
		}
	}
}
