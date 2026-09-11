using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Controls.Embedding.Uno;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiPage = Microsoft.Maui.Controls.Page;
using NativeButton = Microsoft.UI.Xaml.Controls.Button;

namespace Maui.Controls.Sample.Uno;

internal static class LifecycleRegressionProbe
{
	static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	internal static async Task<Tier2ProbeResult> RunAsync(MauiEmbeddingSession session, MauiHost pageHost, MauiHost viewHost)
	{
		var report = new StringBuilder();
		var originalPage = pageHost.MauiContent;
		var originalView = viewHost.MauiContent;
		var originalWidth = viewHost.Width;
		var originalHeight = viewHost.Height;
		try
		{
			var ownership = await EmbeddingOwnershipRegressionProbe.RunAsync(session, pageHost, viewHost);
			report.Append(ownership.Report);
			Check(ownership.Passed, "public overload and transactional host ownership", report);
			await VerifyModalReplacementAsync(session, pageHost, report);
			viewHost.Width = 320;
			viewHost.Height = 240;
			await VerifyObservableListAsync(viewHost, report);
			await VerifyGridAsync(viewHost, report, 0);
			await VerifyGridAsync(viewHost, report, 32);
			await VerifyFailedEmbeddingAsync(session, pageHost, viewHost, report);
			await VerifyExclusiveOwnershipAsync(session, pageHost, viewHost, report);
			return new Tier2ProbeResult(true, report.ToString());
		}
		catch (Exception error)
		{
			report.AppendLine($"FAIL lifecycle regression: {error}");
			Console.WriteLine(report.ToString());
			return new Tier2ProbeResult(false, report.ToString());
		}
		finally
		{
			pageHost.MauiContent = originalPage;
			viewHost.MauiContent = originalView;
			viewHost.Width = originalWidth;
			viewHost.Height = originalHeight;
		}
	}

	static async Task VerifyObservableListAsync(MauiHost host, StringBuilder report)
	{
		var items = new ObservableCollection<string>();
		var collection = new CollectionView
		{
			WidthRequest = 320,
			HeightRequest = 240,
			ItemsSource = items,
			ItemTemplate = new Microsoft.Maui.Controls.DataTemplate(() =>
			{
				var label = new MauiLabel { HeightRequest = 40 };
				label.SetBinding(MauiLabel.TextProperty, static (string item) => item);
				return label;
			})
		};
		host.MauiContent = collection;
		Check(await Tier2Probe.WaitForAsync(() => collection.Handler?.PlatformView is ListViewBase native && native.IsLoaded),
			"observable list attached while empty", report);
		var list = (ListViewBase)collection.Handler!.PlatformView!;
		items.Add("added after attachment");
		Check(await Tier2Probe.WaitForAsync(() => list.Items.Count == 1 && Descendants<TextBlock>(list).Any(text => text.Text == items[0])),
			"observable insertion renders immediately", report);
		items.Clear();
		items.Add("replacement after reset");
		Check(await Tier2Probe.WaitForAsync(() => list.Items.Count == 1 && Descendants<TextBlock>(list).Any(text => text.Text == items[0])
			&& !Descendants<TextBlock>(list).Any(text => text.Text == "added after attachment")),
			"observable reset and repopulation render immediately", report);
	}

	static async Task VerifyModalReplacementAsync(MauiEmbeddingSession session, MauiHost host, StringBuilder report)
	{
		var page = host.MauiContent as MauiPage ?? throw new InvalidOperationException("A page island is required.");
		var modal = CreateModal();
		await page.Navigation.PushModalAsync(modal).WaitAsync(Timeout);
		var oldPlatform = modal.Handler?.PlatformView as FrameworkElement;
		Check(await Tier2Probe.WaitForAsync(() => oldPlatform?.XamlRoot is not null), "modal attached", report);

		var clicks = 0;
		var button = new MauiButton { Text = "Replacement regression button" };
		button.Clicked += (_, _) => clicks++;
		var replacement = new ContentPage { Content = button };
		host.MauiContent = replacement;
		Check(await Tier2Probe.WaitForAsync(() => button.Handler?.PlatformView is NativeButton native && native.IsLoaded && native.ActualHeight > 0),
			"replacement realized after open modal", report);
		Check(await Tier2Probe.WaitForAsync(() => modal.Parent is null && oldPlatform?.IsLoaded != true && session.EmbeddedWindow?.Navigation.ModalStack.Count == 0),
			$"old modal unparented and unloaded (parent={modal.Parent?.GetType().Name ?? "null"}, loaded={oldPlatform?.IsLoaded}, modals={session.EmbeddedWindow?.Navigation.ModalStack.Count})", report);
		var nativeButton = (NativeButton)button.Handler!.PlatformView!;
		Check(AncestorsAllowHitTesting(nativeButton), "replacement ancestors permit input", report);
		var invoke = new ButtonAutomationPeer(nativeButton).GetPattern(PatternInterface.Invoke) as IInvokeProvider
			?? throw new InvalidOperationException("Replacement button has no invoke provider.");
		invoke.Invoke();
		Check(await Tier2Probe.WaitForAsync(() => clicks == 1), "native replacement button reaches MAUI", report);

		var border = host.Parent as Microsoft.UI.Xaml.Controls.Border ?? throw new InvalidOperationException("Probe host has no border.");
		try
		{
			var pendingModal = CreateModal();
			var pending = replacement.Navigation.PushModalAsync(pendingModal);
			border.Child = null;
			await Task.Delay(100);
			Check(!pending.IsCompleted, "detached incoming modal waits for Loaded", report);
			host.MauiContent = new ContentPage { Content = new MauiLabel { Text = "replacement after pending modal" } };
			try
			{
				await pending.WaitAsync(Timeout);
				throw new InvalidOperationException("Discarded modal navigation completed instead of cancelling.");
			}
			catch (OperationCanceledException)
			{
				report.AppendLine("PASS discarded pending modal task cancelled");
			}
			Check(pendingModal.Parent is null, "pending modal logical parent removed", report);
		}
		finally
		{
			border.Child = host;
		}

		var current = (MauiPage)host.MauiContent!;
		Check(await Tier2Probe.WaitForAsync(() => (current.Handler?.PlatformView as FrameworkElement)?.IsLoaded == true),
			"replacement reattached", report);
		await current.Navigation.PushModalAsync(CreateModal()).WaitAsync(Timeout);
		await current.Navigation.PopModalAsync().WaitAsync(Timeout);
		report.AppendLine("PASS subsequent modal navigation completes");

		var outgoing = new ContentPage { Content = new MauiLabel { Text = "pending pop modal" } };
		await current.Navigation.PushModalAsync(outgoing).WaitAsync(Timeout);
		var outgoingPlatform = outgoing.Handler?.PlatformView as FrameworkElement;
		Check(await Tier2Probe.WaitForAsync(() => (current.Handler?.PlatformView as FrameworkElement)?.IsLoaded == false),
			"underlying page unloaded while modal is open", report);
		try
		{
			var pendingPop = current.Navigation.PopModalAsync();
			border.Child = null;
			Check(await Tier2Probe.WaitForAsync(() => outgoingPlatform?.IsLoaded == false),
				"outgoing modal unloaded before pending pop", report);
			Check(!pendingPop.IsCompleted, "pop waits for the underlying page to load", report);
			host.MauiContent = new ContentPage { Content = new MauiLabel { Text = "replacement after pending pop" } };
			try
			{
				await pendingPop.WaitAsync(Timeout);
				throw new InvalidOperationException("Discarded pop completed instead of cancelling.");
			}
			catch (OperationCanceledException)
			{
				report.AppendLine("PASS discarded pending pop task cancelled");
			}
			Check(outgoing.Parent is null && outgoing.Handler is null,
				"pending pop releases outgoing modal ownership and handler", report);
			Check(!((IVisualTreeElement)session.EmbeddedWindow!).GetVisualChildren().Contains(outgoing),
				"pending pop removes outgoing window visual child", report);
		}
		finally
		{
			border.Child = host;
		}

		current = (MauiPage)host.MauiContent!;
		Check(await Tier2Probe.WaitForAsync(() => (current.Handler?.PlatformView as FrameworkElement)?.IsLoaded == true),
			"replacement after pending pop reattached", report);
		await current.Navigation.PushModalAsync(CreateModal()).WaitAsync(Timeout);
		await current.Navigation.PopModalAsync().WaitAsync(Timeout);
		report.AppendLine("PASS subsequent navigation after pending pop completes");
	}

	static async Task VerifyFailedEmbeddingAsync(MauiEmbeddingSession session, MauiHost pageHost, MauiHost viewHost, StringBuilder report)
	{
		pageHost.MauiContent = null;
		viewHost.MauiContent = null;
		foreach (var (asPage, failDuringCreation) in new[] { (false, false), (true, false), (false, true), (true, true) })
		{
			var failing = new FailingEmbeddingView { FailDuringCreation = failDuringCreation };
			Microsoft.Maui.Controls.VisualElement content = asPage ? new ContentPage { Content = failing } : failing;
			try
			{
				session.Embed(content);
				throw new InvalidOperationException("The failing handler unexpectedly realized.");
			}
			catch (InvalidOperationException error) when (ReferenceEquals(error, failing.Failure))
			{
				report.AppendLine($"PASS failed {(asPage ? "page" : "view")} preserves the {(failDuringCreation ? "creation" : "connection")} exception");
			}
			Check(content.Parent is null && content.Handler is null && failing.Handler is null,
				$"failed {(asPage ? "page" : "view")} releases logical children and handlers", report);
			Check(session.EmbeddedWindow!.Page is null && !session.HasWindowPage &&
				session.WindowContext.Services.GetService(Type.GetType("Microsoft.Maui.Platform.WindowRootViewContainer, Microsoft.Maui", true)!) is null,
				"failed realization leaves no page or root registration", report);

			failing.ThrowOnConnect = false;
			var host = asPage ? pageHost : viewHost;
			host.MauiContent = content;
			Check(await Tier2Probe.WaitForAsync(() => (failing.Handler?.PlatformView as FrameworkElement)?.IsLoaded == true),
				$"failed {(asPage ? "page" : "view")} can be retried", report);
			if (content is MauiPage page)
			{
				await page.Navigation.PushModalAsync(CreateModal()).WaitAsync(Timeout);
				await page.Navigation.PopModalAsync().WaitAsync(Timeout);
				report.AppendLine("PASS retried page has a working modal root lifetime");
			}
			ExpectDuplicateRejected(() => session.Embed(content), "retried content still has exactly one owner", report);
			Check(content.Handler is not null && content.Parent is not null,
				"rejected duplicate does not roll back the successful retry", report);
			host.MauiContent = null;
		}
	}

	static async Task VerifyExclusiveOwnershipAsync(MauiEmbeddingSession session, MauiHost firstHost, MauiHost secondHost, StringBuilder report)
	{
		firstHost.MauiContent = null;
		secondHost.MauiContent = null;
		var content = new ContentView { Content = new MauiLabel { Text = "exclusive ownership" } };
		firstHost.MauiContent = content;
		Check(await Tier2Probe.WaitForAsync(() => (content.Handler?.PlatformView as FrameworkElement)?.IsLoaded == true),
			"ownership regression view loaded", report);
		var handler = content.Handler;
		var platform = firstHost.Content;
		var parent = content.Parent;
		var otherContent = new ContentView { Content = new MauiLabel { Text = "retained second host" } };
		secondHost.MauiContent = otherContent;
		var otherHandler = otherContent.Handler;
		var otherPlatform = secondHost.Content;

		ExpectDuplicateRejected(() => session.Embed(content), "duplicate Embed is rejected", report);
		ExpectDuplicateRejected(() => content.ToPlatformEmbedded(session.WindowContext), "direct embedding cannot steal a session view", report);
		ExpectDuplicateRejected(() => secondHost.MauiContent = content, "two hosts cannot realize the same view", report);
		Check(ReferenceEquals(otherContent.Handler, otherHandler) && ReferenceEquals(secondHost.Content, otherPlatform),
			"duplicate rejection preserves the second host's previous content", report);
		secondHost.MauiContent = null;
		Check(ReferenceEquals(content.Handler, handler) && ReferenceEquals(content.Parent, parent) &&
			ReferenceEquals(firstHost.Content, platform), "rejected second host leaves the owner untouched", report);

		var otherWindow = new Microsoft.UI.Xaml.Window();
		var otherSession = MauiEmbeddingSession.GetOrCreate(otherWindow);
		try
		{
			ExpectDuplicateRejected(() => otherSession.Embed(content), "cross-session duplicate is rejected", report);
			otherSession.Release(content);
			Check(otherSession.EmbeddedWindow is null && ReferenceEquals(content.Handler, handler),
				"rejected session creates no window scope and cannot release the owner", report);
		}
		finally
		{
			otherSession.Dispose();
			otherWindow.Close();
		}

		var border = firstHost.Parent as Microsoft.UI.Xaml.Controls.Border
			?? throw new InvalidOperationException("Probe host has no border.");
		try
		{
			border.Child = null;
			Check(await Tier2Probe.WaitForAsync(() => (platform as FrameworkElement)?.IsLoaded == false),
				"owner transiently unloaded", report);
			ExpectDuplicateRejected(() => secondHost.MauiContent = content, "transient unload retains exclusive ownership", report);
			secondHost.MauiContent = null;
		}
		finally
		{
			border.Child = firstHost;
		}
		Check(await Tier2Probe.WaitForAsync(() => (platform as FrameworkElement)?.IsLoaded == true),
			"original owner reattached", report);
		Check(ReferenceEquals(content.Handler, handler), "reattachment retains the handler", report);

		firstHost.MauiContent = null;
		Check(content.Parent is null && content.Handler is null, "explicit release clears ownership and handler", report);
		secondHost.MauiContent = content;
		Check(await Tier2Probe.WaitForAsync(() => (content.Handler?.PlatformView as FrameworkElement)?.IsLoaded == true),
			"released content transfers to another host", report);
		secondHost.MauiContent = null;
	}

	static void ExpectDuplicateRejected(Action attach, string name, StringBuilder report)
	{
		try
		{
			attach();
		}
		catch (InvalidOperationException)
		{
			report.AppendLine($"PASS {name}");
			return;
		}
		throw new InvalidOperationException(name);
	}

	internal sealed class FailingEmbeddingView : Microsoft.Maui.Controls.ContentView
	{
		internal bool ThrowOnConnect { get; set; } = true;
		internal bool FailDuringCreation { get; init; }
		internal InvalidOperationException Failure { get; } = new("Expected embedding handler failure.");
	}

	internal sealed class FailingEmbeddingViewHandler : ContentViewHandler
	{
		protected override ContentPanel CreatePlatformView()
		{
			if (VirtualView is FailingEmbeddingView { ThrowOnConnect: true, FailDuringCreation: true } view)
				throw view.Failure;
			return base.CreatePlatformView();
		}

		protected override void ConnectHandler(ContentPanel platformView)
		{
			base.ConnectHandler(platformView);
			if (VirtualView is FailingEmbeddingView { ThrowOnConnect: true } view)
				throw view.Failure;
		}
	}

	static async Task VerifyGridAsync(MauiHost host, StringBuilder report, int initialCount)
	{
		var items = new ObservableCollection<int>(Enumerable.Range(0, initialCount));
		var collection = new CollectionView
		{
			WidthRequest = 320,
			HeightRequest = 240,
			ItemsLayout = new GridItemsLayout(ItemsLayoutOrientation.Vertical) { Span = 2 },
			ItemTemplate = new Microsoft.Maui.Controls.DataTemplate(() => new MauiLabel { HeightRequest = 40, Text = "grid item" }),
			ItemsSource = items
		};
		host.MauiContent = collection;
		Check(await Tier2Probe.WaitForAsync(() => collection.Handler?.PlatformView is GridView native && native.IsLoaded),
			$"grid with {initialCount} initial items attached", report);
		var grid = (GridView)collection.Handler!.PlatformView!;
		Check(await Tier2Probe.WaitForAsync(() => grid.ActualWidth > 0 && grid.ActualHeight > 0 && grid.ActualHeight <= 240),
			$"grid viewport is bounded ({grid.ActualWidth}x{grid.ActualHeight})", report);
		if (initialCount == 0)
		{
			Check(grid.ItemsPanelRoot is ItemsWrapGrid, "empty grid starts with ItemsWrapGrid", report);
		}
		if (initialCount > 0)
		{
			for (var i = items.Count; i < 65; i++)
				items.Add(i);
			Check(await Tier2Probe.WaitForAsync(() => Descendants<ItemsWrapGrid>(grid).Any()),
				"growing grid promotes virtualization", report);
		}
		Check(grid.ItemsPanelRoot is ItemsWrapGrid && typeof(ItemsWrapGrid).GetInterfaces().Any(type => type.Name == "IVirtualizingPanel"),
			"Uno runtime implements ItemsWrapGrid virtualization (a stub cannot safely accept 100000 items)", report);
		collection.ItemsSource = Enumerable.Range(0, 100_000).ToList();
		Check(grid.Items.Count == 100_000, "large replacement source assigned", report);
		Check(await Tier2Probe.WaitForAsync(() => Descendants<ItemsWrapGrid>(grid).Any() && Descendants<GridViewItem>(grid).Any()),
			"large replacement grid realizes items", report);
		var realized = Descendants<GridViewItem>(grid).Count();
		Check(realized is > 0 and <= 100, $"100000-item grid has bounded realization ({realized})", report);
	}

	static ContentPage CreateModal() => new()
	{
		BackgroundColor = Microsoft.Maui.Graphics.Colors.Purple,
		Content = new MauiLabel { Text = "lifecycle modal" }
	};

	static bool AncestorsAllowHitTesting(DependencyObject element)
	{
		for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is UIElement ui && !ui.IsHitTestVisible)
				return false;
		}
		return true;
	}

	static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;
			foreach (var descendant in Descendants<T>(child))
				yield return descendant;
		}
	}

	static void Check(bool passed, string name, StringBuilder report)
	{
		if (!passed)
			throw new InvalidOperationException(name);
		report.AppendLine($"PASS {name}");
		Console.WriteLine($"LIFECYCLE: {name}");
	}
}
