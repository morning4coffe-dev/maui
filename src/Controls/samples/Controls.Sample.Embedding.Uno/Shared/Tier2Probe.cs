using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Embedding.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using MauiContentPage = Microsoft.Maui.Controls.ContentPage;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiNavigationPage = Microsoft.Maui.Controls.NavigationPage;
using MauiPage = Microsoft.Maui.Controls.Page;
using AppTheme = Microsoft.Maui.ApplicationModel.AppTheme;
using MauiApplication = Microsoft.Maui.Controls.Application;

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
/// The binding source used to verify that the host's data context reaches embedded MAUI content.
/// </summary>
/// <remarks>
/// Top level rather than nested in <see cref="Tier2Probe"/> because MAUI's typed bindings are produced by
/// an interceptor that has to be able to name this type.
/// </remarks>
public sealed class Tier2ProbeViewModel
{
	public string? Name { get; set; }
}

/// <summary>
/// Verifies the window-scoped MAUI features from code, so the result does not depend on UI automation.
/// </summary>
/// <remarks>
/// This is an assertion harness, not a report: every check has a verdict and a timeout, and a failed or
/// timed out check makes the whole run fail. A check whose precondition a supported host action removed —
/// replacing the island's page, for instance — is recorded as skipped instead, so it stays visible without
/// failing the run. Checks assert on the realized platform view rather than on
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

	public static async Task<Tier2ProbeResult> RunAsync(
		MauiEmbeddingSession session,
		MauiPage page,
		XamlRoot? xamlRoot,
		MauiHost? viewHost = null)
	{
		var report = new StringBuilder();
		var failures = 0;
		var skipped = 0;

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

		// A check whose precondition the host legitimately removed is not a defect. Recording it as
		// skipped keeps that visible without turning a supported host action into a failed run.
		void Skip(string name, string reason)
		{
			skipped++;

			report.AppendLine(string.Format(CultureInfo.InvariantCulture, "SKIP {0} — {1}", name, reason));
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
			await CheckNavigationAsync(page, Check, Skip);
			CheckSecondPageIsRejected(session, Check);
			CheckThemeIsBridged(session, Check);
			await CheckThemeChangeIsBridgedAsync(session, Check);
			await CheckBindingContextIsBridgedAsync(viewHost, Check);

			// Last: this one cannot dismiss its own dialog.
			await CheckOffUiThreadAlertAsync(page, xamlRoot, Check);
		}
		catch (Exception ex)
		{
			Check("probe completed", false, $"{ex.GetType().Name}: {ex.Message}");
		}

		report.AppendLine(failures == 0
			? skipped == 0 ? "RESULT: PASS" : $"RESULT: PASS ({skipped} skipped)"
			: $"RESULT: FAIL ({failures} failed)");
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

	static async Task CheckNavigationAsync(MauiPage page, CheckResult check, Action<string, string> skip)
	{
		if (page is not MauiNavigationPage navigationPage)
		{
			// Replacing the island's content with a plain page is a supported host action, so there is
			// simply no stack to push onto rather than anything being broken.
			skip("stack navigation", "the window page is not a NavigationPage");
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

	// The theme plumbing MAUI uses on Windows is a Win32 message hook that the Uno target compiles out, and
	// embedding has no MauiWinUIWindow either, so nothing ever calls IApplication.ThemeChanged. Without the
	// session's bridge, the embedded application's theme stays Unspecified for the life of the process and
	// AppThemeBinding never resolves. That is what makes this assertion meaningful rather than tautological.
	static void CheckThemeIsBridged(MauiEmbeddingSession session, CheckResult check)
	{
		var expected = ToAppTheme((session.PlatformWindow.Content as FrameworkElement)?.ActualTheme);
		var actual = MauiApplication.Current?.RequestedTheme;

		check(
			"host theme reaches the embedded application",
			actual is not null && actual == expected && actual != AppTheme.Unspecified,
			$"host={expected} maui={actual}");
	}

	// Uno rejects a runtime application theme change, so an Uno app switches theme through a root element's
	// RequestedTheme. That is what this drives, because it is what a real host does.
	static async Task CheckThemeChangeIsBridgedAsync(MauiEmbeddingSession session, CheckResult check)
	{
		const string Name = "host theme change reaches the embedded application";

		if (session.PlatformWindow.Content is not FrameworkElement root || MauiApplication.Current is null)
		{
			check(Name, false, "no window root");
			return;
		}

		var original = root.RequestedTheme;
		var flipped = root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;

		try
		{
			root.RequestedTheme = flipped;

			var expected = ToAppTheme(flipped);
			var followed = await WaitForAsync(() => MauiApplication.Current?.RequestedTheme == expected);

			check(Name, followed, $"host={expected} maui={MauiApplication.Current?.RequestedTheme}");
		}
		catch (Exception ex)
		{
			check(Name, false, $"{ex.GetType().Name}: {ex.Message}");
		}
		finally
		{
			root.RequestedTheme = original;
		}
	}

	// Asserts the bridge end to end rather than just comparing references: a real MAUI binding has to
	// resolve against the Uno DataContext for the bridge to be worth anything.
	static async Task CheckBindingContextIsBridgedAsync(MauiHost? viewHost, CheckResult check)
	{
		const string ContextName = "host DataContext flows to the embedded BindingContext";
		const string BindingName = "a MAUI binding resolves through the host DataContext";

		if (viewHost is null)
		{
			check(ContextName, false, "no view-level host was supplied");
			check(BindingName, false, "no view-level host was supplied");
			return;
		}

		var originalContent = viewHost.MauiContent;
		var originalDataContext = viewHost.DataContext;

		try
		{
			var label = new MauiLabel();

			// A typed binding, not a string path: string paths carry RequiresUnreferencedCode and fail the
			// trimmed WebAssembly publish this sample is verified against.
			label.SetBinding(MauiLabel.TextProperty, static (Tier2ProbeViewModel model) => model.Name);

			viewHost.MauiContent = label;
			viewHost.DataContext = new Tier2ProbeViewModel { Name = "bound through DataContext" };

			var bridged = await WaitForAsync(() => label.BindingContext is Tier2ProbeViewModel);
			check(ContextName, bridged, label.BindingContext?.GetType().Name ?? "null");

			var resolved = await WaitForAsync(() => label.Text == "bound through DataContext");
			check(BindingName, resolved, label.Text ?? "null");
		}
		catch (Exception ex)
		{
			check(ContextName, false, $"{ex.GetType().Name}: {ex.Message}");
		}
		finally
		{
			viewHost.DataContext = originalDataContext;
			viewHost.MauiContent = originalContent;
		}
	}

	static AppTheme ToAppTheme(ElementTheme? theme) => theme switch
	{
		ElementTheme.Dark => AppTheme.Dark,
		ElementTheme.Light => AppTheme.Light,
		_ => AppTheme.Unspecified,
	};

	static async Task<bool> WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + OperationTimeout;

		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return true;
			}

			await Task.Delay(50);
		}

		return condition();
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
