using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

/// <summary>The outcome of a Tier 2 verification run.</summary>
public sealed class Tier2ProbeResult
{
	public Tier2ProbeResult(bool passed, string report)
	{
		Passed = passed;
		Report = report;
	}

	/// <summary>Gets a value indicating whether every check passed.</summary>
	public bool Passed { get; }

	/// <summary>Gets the human readable report.</summary>
	public string Report { get; }
}

/// <summary>
/// Verifies the window-scoped MAUI features from code, so the result does not depend on UI automation.
/// </summary>
/// <remarks>
/// This is an assertion harness, not a report: every check has a verdict and a timeout, and a failed or
/// timed out check makes the whole run fail. Checks assert on the realized platform view rather than on
/// MAUI's virtual stacks, because MAUI records a modal or navigation push even when the platform never
/// realized the page.
/// </remarks>
public static class Tier2Probe
{
	/// <summary>Records the verdict of a single check. A custom delegate so the detail stays optional.</summary>
	delegate void CheckResult(string name, bool passed, string? detail = null);

	public const string EnableVariable = "MAUI_UNO_TIER2_PROBE";

	static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

	/// <summary>
	/// Gets a value indicating whether the probe should run at startup. The browser head cannot set
	/// environment variables, so the query string is honoured as well.
	/// </summary>
	public static bool IsEnabled =>
		string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal) ||
		QueryStringRequestsProbe();

	public static string LogPath => Path.Combine(Path.GetTempPath(), "tier2-probe.log");

	public static async Task<Tier2ProbeResult> RunAsync(MauiEmbeddingSession session, MauiPage page, XamlRoot? xamlRoot)
	{
		var report = new StringBuilder();
		var failures = 0;

		void Check(string name, bool passed, string? detail = null)
		{
			if (!passed)
			{
				failures++;
			}

			report.AppendLine(string.Format(
				CultureInfo.InvariantCulture,
				"{0} {1}{2}",
				passed ? "PASS" : "FAIL",
				name,
				string.IsNullOrEmpty(detail) ? string.Empty : $" — {detail}"));

			Flush(report);
		}

		try
		{
			Check("window page is the island page", ReferenceEquals(session.EmbeddedWindow?.Page, page));

			await CheckAlertAsync(page, xamlRoot, Check);
			await CheckTwoButtonAlertAsync(page, xamlRoot, Check);
			await CheckPromptAsync(page, xamlRoot, Check);
			await CheckActionSheetAsync(page, xamlRoot, Check);
			await CheckModalAsync(page, Check);
			await CheckNavigationAsync(page, Check);
			CheckSecondPageIsRejected(session, Check);

			// Last: this one cannot dismiss its own dialog.
			await CheckOffUiThreadAlertAsync(page, xamlRoot, Check);
		}
		catch (Exception ex)
		{
			Check("probe completed", false, $"{ex.GetType().Name}: {ex.Message}");
		}

		report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures} failed)");
		Flush(report);

		return new Tier2ProbeResult(failures == 0, report.ToString());
	}

	static async Task CheckAlertAsync(MauiPage page, XamlRoot? xamlRoot, CheckResult check)
	{
		var task = page.DisplayAlertAsync("probe", "probe body", "OK");
		var shown = await WaitForDialogAsync(xamlRoot);
		check("DisplayAlertAsync shows a dialog", shown);

		DismissDialogs(xamlRoot);
		check("DisplayAlertAsync completes after dismissal", await CompletesAsync(task));
	}

	static async Task CheckTwoButtonAlertAsync(MauiPage page, XamlRoot? xamlRoot, CheckResult check)
	{
		var task = page.DisplayAlertAsync("probe", "probe body", "Yes", "No");
		var shown = await WaitForDialogAsync(xamlRoot);
		check("two button DisplayAlertAsync shows a dialog", shown);

		DismissDialogs(xamlRoot);
		check("two button DisplayAlertAsync completes", await CompletesAsync(task));
	}

	static async Task CheckPromptAsync(MauiPage page, XamlRoot? xamlRoot, CheckResult check)
	{
		var task = page.DisplayPromptAsync("probe", "type something", initialValue: "hello");
		var shown = await WaitForDialogAsync(xamlRoot);
		check("DisplayPromptAsync shows a dialog", shown);

		DismissDialogs(xamlRoot);
		check("DisplayPromptAsync completes", await CompletesAsync(task));
	}

	static async Task CheckActionSheetAsync(MauiPage page, XamlRoot? xamlRoot, CheckResult check)
	{
		var task = page.DisplayActionSheetAsync("probe", "Cancel", null, "First", "Second");
		var shown = await WaitForDialogAsync(xamlRoot);
		check("DisplayActionSheetAsync shows a flyout or dialog", shown);

		DismissDialogs(xamlRoot);
		check("DisplayActionSheetAsync completes", await CompletesAsync(task));
	}

	static async Task CheckModalAsync(MauiPage page, CheckResult check)
	{
		var modal = new MauiContentPage
		{
			Title = "probe modal",
			Content = new MauiLabel { Text = "probe modal content" },
		};

		if (!await CompletesAsync(page.Navigation.PushModalAsync(modal)))
		{
			check("PushModalAsync completes", false, "timed out");
			return;
		}

		await Task.Delay(500);

		var platformView = modal.Handler?.PlatformView as FrameworkElement;
		check("modal is realized on the platform", platformView is not null, platformView?.GetType().Name ?? "no platform view");
		check("modal is attached to a XamlRoot", platformView?.XamlRoot is not null);
		check("modal is rendered", platformView is not null && platformView.ActualHeight > 0);
		check("PopModalAsync completes", await CompletesAsync(page.Navigation.PopModalAsync()));
	}

	static async Task CheckNavigationAsync(MauiPage page, CheckResult check)
	{
		if (page is not MauiNavigationPage navigationPage)
		{
			check("stack navigation", false, "window page is not a NavigationPage");
			return;
		}

		var pushed = new MauiContentPage
		{
			Title = "probe pushed page",
			Content = new MauiLabel { Text = "probe pushed content" },
		};

		if (!await CompletesAsync(navigationPage.Navigation.PushAsync(pushed)))
		{
			check("PushAsync completes", false, "timed out");
			return;
		}

		// NavigationPage runs a transition, so the pushed page is not measured on the frame the push
		// completes.
		await Task.Delay(750);

		var platformView = pushed.Handler?.PlatformView as FrameworkElement;
		check("pushed page is realized on the platform", platformView is not null, platformView?.GetType().Name ?? "no platform view");
		check("pushed page is attached to a XamlRoot", platformView?.XamlRoot is not null);
		check("pushed page is rendered", platformView is not null && platformView.ActualHeight > 0);
		check("PopAsync completes", await CompletesAsync(navigationPage.Navigation.PopAsync()));
	}

	static void CheckSecondPageIsRejected(MauiEmbeddingSession session, CheckResult check)
	{
		try
		{
			session.Embed(new MauiContentPage { Content = new MauiLabel { Text = "second page" } });
			check("a second page island is rejected", false, "it was accepted, so its dialogs would route through the first island");
		}
		catch (InvalidOperationException)
		{
			check("a second page island is rejected", true);
		}
	}

	// Regression test for a crash found during QA: requesting an alert whose state machine runs on a
	// thread-pool thread made Uno materialize the ContentDialog template off the UI thread. Because the
	// alert handlers are async void, that exception was unhandled and killed the process.
	static async Task CheckOffUiThreadAlertAsync(MauiPage page, XamlRoot? xamlRoot, CheckResult check)
	{
		Task? task = null;

		// Block body on purpose: an expression body returns the Task, which binds Task.Run's Func<Task>
		// overload and unwraps it, so the await would block until the dialog is dismissed.
		await Task.Run(() =>
		{
			task = page.DisplayAlertAsync("off-thread probe", "requested off the UI thread", "OK");
		});

		var shown = await WaitForDialogAsync(xamlRoot);
		check("off UI thread alert shows a dialog without crashing", shown);
		check("off UI thread alert request was created", task is not null);
	}

	static async Task<bool> WaitForDialogAsync(XamlRoot? xamlRoot)
	{
		if (xamlRoot is null)
		{
			return false;
		}

		var deadline = DateTime.UtcNow + OperationTimeout;

		while (DateTime.UtcNow < deadline)
		{
			if (CountOpenDialogs(xamlRoot) > 0)
			{
				return true;
			}

			await Task.Delay(100);
		}

		return false;
	}

	static async Task<bool> CompletesAsync(Task task) =>
		await Task.WhenAny(task, Task.Delay(OperationTimeout)) == task;

	static async Task<bool> CompletesAsync<T>(Task<T> task) =>
		await Task.WhenAny(task, Task.Delay(OperationTimeout)) == task;

	static int CountOpenDialogs(XamlRoot xamlRoot)
	{
		try
		{
			var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Count;
			return popups > 0 ? popups : FindDialogs(xamlRoot.Content, 0).Count;
		}
		catch (Exception)
		{
			return 0;
		}
	}

	static void DismissDialogs(XamlRoot? xamlRoot)
	{
		if (xamlRoot is null)
		{
			return;
		}

		try
		{
			// ContentDialog must be dismissed through Hide(); closing the hosting popup leaves the awaited
			// ShowAsync task pending, stalling everything queued behind it.
			foreach (var dialog in FindDialogs(xamlRoot.Content, 0))
			{
				dialog.Hide();
			}

			foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
			{
				if (popup.Child is ContentDialog hosted)
				{
					hosted.Hide();
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

	static List<ContentDialog> FindDialogs(DependencyObject? node, int depth)
	{
		var found = new List<ContentDialog>();

		if (node is null || depth > 40)
		{
			return found;
		}

		if (node is ContentDialog dialog)
		{
			found.Add(dialog);
			return found;
		}

		var count = VisualTreeHelper.GetChildrenCount(node);

		for (var i = 0; i < count; i++)
		{
			found.AddRange(FindDialogs(VisualTreeHelper.GetChild(node, i), depth + 1));
		}

		return found;
	}

	static bool QueryStringRequestsProbe()
	{
		try
		{
			var args = Environment.GetCommandLineArgs();
			return args.Any(a => a.Contains("tier2probe=1", StringComparison.OrdinalIgnoreCase));
		}
		catch (Exception)
		{
			return false;
		}
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
}
