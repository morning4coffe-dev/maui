using System.Text;
using System.Collections.ObjectModel;
using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Controls.Embedding.Uno;
using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml.Controls;
using DependencyObject = Microsoft.UI.Xaml.DependencyObject;
using FrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using MauiItemsView = Microsoft.Maui.Controls.ItemsView;
using MauiSelectionMode = Microsoft.Maui.Controls.SelectionMode;
using Visibility = Microsoft.UI.Xaml.Visibility;
using VisualTreeHelper = Microsoft.UI.Xaml.Media.VisualTreeHelper;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiTemplate = Microsoft.Maui.Controls.DataTemplate;
using NativeScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;

namespace Maui.Controls.Sample.Uno;

internal static class ProductionCompatibilityProbe
{
	internal static async Task<Tier2ProbeResult> RunAsync(MauiEmbeddingSession session, MauiHost host)
	{
		var report = new StringBuilder();
		var failures = 0;
		var original = host.MauiContent;
		var width = host.Width;
		var height = host.Height;
		host.Width = 320;
		host.Height = 240;
		try
		{
			await RunCase("Full mode preserves MAUI collection and carousel handlers", VerifyHandlerModeAsync);
			await RunCase("failed item creation rolls back ownership", VerifyFactoryFailureAsync);
			await RunCase("carousel CurrentItem moves the platform view", VerifyCurrentItemAsync);
			await RunCase("carousel loop follows asynchronous source growth and reset", () => VerifyLoopGrowthAsync(false));
			await RunCase("carousel loop observes notifying enumerable sources", () => VerifyLoopGrowthAsync(true));
			await RunCase("carousel VisibleViews tracks peek, scroll, resize, clear, and teardown", VerifyVisibleViewsAsync);
			await RunCase("carousel VisibleViews tolerates teardown from collection notification", VerifyVisibleViewsTeardownAsync);
			await RunCase("vertical CollectionView ScrollTo reaches the requested item",
				() => VerifyScrollToAsync(ItemsLayoutOrientation.Vertical));
			await RunCase("horizontal CollectionView ScrollTo reaches the requested item",
				() => VerifyScrollToAsync(ItemsLayoutOrientation.Horizontal));
			await RunCase("CollectionView maps header footer empty state and incremental loading", VerifyCollectionPresentationAsync);
			await RunCase("CollectionView keeps multiple selection consistent through source changes", VerifyMultipleSelectionAsync);
			await RunCase("CollectionView realizes grouped sources with group headers", VerifyGroupingAsync);
			return new Tier2ProbeResult(failures == 0, report.ToString());
		}
		finally
		{
			host.MauiContent = original;
			host.Width = width;
			host.Height = height;
		}

		async Task RunCase(string name, Func<Task> action)
		{
			try
			{
				await action();
				report.AppendLine($"PASS {name}");
			}
			catch (Exception error)
			{
				failures++;
				report.AppendLine($"FAIL {name}: {error}");
			}
			finally
			{
				host.MauiContent = null;
			}
		}

		async Task VerifyHandlerModeAsync()
		{
			var collection = new CollectionView
			{
				ItemsSource = new[] { "collection item" },
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "collection item" })
			};
			host.MauiContent = collection;
			await RequireAsync(() => collection.Handler?.PlatformView is FrameworkElement { IsLoaded: true }, "default collection handler");
			if (collection.Handler?.GetType() != typeof(CollectionViewHandler))
			{
				throw new InvalidOperationException(
					$"CollectionView resolved {collection.Handler?.GetType().FullName ?? "no handler"} instead of {typeof(CollectionViewHandler).FullName}.");
			}

			host.MauiContent = null;

			var carousel = new CarouselView
			{
				ItemsSource = new[] { "carousel item" },
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "carousel item" })
			};
			host.MauiContent = carousel;
			await RequireAsync(() => carousel.Handler?.PlatformView is FrameworkElement { IsLoaded: true }, "default carousel handler");
			if (carousel.Handler?.GetType() != typeof(CarouselViewHandler))
			{
				throw new InvalidOperationException(
					$"CarouselView resolved {carousel.Handler?.GetType().FullName ?? "no handler"} instead of {typeof(CarouselViewHandler).FullName}.");
			}
		}

		async Task VerifyFactoryFailureAsync()
		{
			var failed = new LifecycleRegressionProbe.FailingEmbeddingView { FailDuringCreation = true };
			try
			{
				failed.ToPlatformEmbedded(session.WindowContext);
				throw new InvalidOperationException("The failing view unexpectedly succeeded.");
			}
			catch (InvalidOperationException error) when (ReferenceEquals(error, failed.Failure))
			{
			}
			finally
			{
				if (failed.Parent is not null || failed.Handler is not null)
					throw new InvalidOperationException("The failed view retains its parent or handler.");
			}
		}

		async Task VerifyCurrentItemAsync()
		{
			var items = new[] { "first", "second", "third" };
			var carousel = new CarouselView
			{
				Loop = false,
				IsScrollAnimated = false,
				ItemsSource = items,
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "carousel item", HeightRequest = 80 })
			};
			host.MauiContent = carousel;
			await RequireAsync(() => carousel.Handler?.PlatformView is ListViewBase { IsLoaded: true }, "carousel attachment");
			var list = GetListView(carousel);
			var scroll = GetScrollViewer(list);
			await RequireAsync(() => scroll.IsLoaded && scroll.ActualWidth > 0 && list.ItemsSource is not null, "carousel layout");
			carousel.CurrentItem = items[2];
			await RequireAsync(() => carousel.Position == 2 && IsIndexVisible(list, 2, scroll, horizontal: true),
				"CurrentItem, Position, and visible item agreement");
		}

		async Task VerifyScrollToAsync(ItemsLayoutOrientation orientation)
		{
			var collection = new CollectionView
			{
				ItemsSource = Enumerable.Range(0, 100).ToArray(),
				ItemsLayout = new LinearItemsLayout(orientation),
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "item", HeightRequest = 40, WidthRequest = 40 })
			};
			host.MauiContent = collection;
			await RequireAsync(() => collection.Handler?.PlatformView is ListViewBase { IsLoaded: true }, "collection attachment");
			var list = GetListView(collection);
			var scroll = GetScrollViewer(list);
			var horizontal = orientation == ItemsLayoutOrientation.Horizontal;
			await RequireAsync(() => scroll.IsLoaded && (horizontal ? scroll.ExtentWidth : scroll.ExtentHeight) > 320, "collection layout");
			collection.ScrollTo(99, position: ScrollToPosition.End, animate: false);
			await RequireAsync(() => IsIndexVisible(list, 99, scroll, horizontal), "scroll request completion");
			collection.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
			await RequireAsync(() => IsIndexVisible(list, 0, scroll, horizontal), "scroll back to start");
			collection.ScrollTo((object)99, position: ScrollToPosition.End, animate: false);
			await RequireAsync(() => IsIndexVisible(list, 99, scroll, horizontal), "item-based scroll completion");
		}

		async Task VerifyLoopGrowthAsync(bool useEnumerable)
		{
			var items = new ObservableCollection<string>();
			var carousel = new CarouselView
			{
				Loop = true,
				IsScrollAnimated = false,
				ItemsSource = useEnumerable ? new NotifyingEnumerable(items) : items,
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "loop item", HeightRequest = 80 })
			};
			host.MauiContent = carousel;
			await RequireAsync(() => carousel.Handler?.PlatformView is ListViewBase { IsLoaded: true }, "carousel attachment");
			var list = GetListView(carousel);
			await RequireAsync(() => list.ActualWidth > 0, "empty carousel layout");
			items.Add("first");
			items.Add("second");
			await RequireAsync(() => list.Items.Count > 2 && carousel.CurrentItem is not null,
				"loop activation after empty source grows");
			items.Clear();
			await RequireAsync(() => carousel.CurrentItem is null && carousel.VisibleViews.Count == 0, "loop reset clears stale state");
			items.Add("single");
			await RequireAsync(() => Equals(carousel.CurrentItem, "single"), "singleton source becomes current");
			items.Add("another");
			await RequireAsync(() => list.Items.Count > 2 && carousel.Position >= 0,
				"loop reactivation after reset");
		}

		async Task VerifyCollectionPresentationAsync()
		{
			var items = new ObservableCollection<int>();
			var header = new MauiLabel { Text = "contract header", HeightRequest = 24 };
			var footer = new MauiLabel { Text = "contract footer", HeightRequest = 24 };
			var empty = new MauiLabel { Text = "contract empty", HeightRequest = 24 };
			var thresholdCount = 0;
			var collection = new CollectionView
			{
				ItemsSource = items,
				Header = header,
				Footer = footer,
				EmptyView = empty,
				RemainingItemsThreshold = 1,
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "item", HeightRequest = 40 })
			};
			collection.RemainingItemsThresholdReached += (_, _) => thresholdCount++;

			host.MauiContent = collection;
			await RequireAsync(
				() => collection.Handler?.PlatformView is ListViewBase { IsLoaded: true } &&
					header.Handler?.PlatformView is FrameworkElement { IsLoaded: true, ActualHeight: > 0 } &&
					footer.Handler?.PlatformView is FrameworkElement { IsLoaded: true, ActualHeight: > 0 } &&
					empty.Handler?.PlatformView is FrameworkElement { Visibility: Visibility.Visible },
				"header, footer, and empty state");

			for (var i = 0; i < 80; i++)
				items.Add(i);

			await RequireAsync(
				() => FindNamedElement(GetListView(collection), "EmptyViewContentControl") is FrameworkElement
				{
					Visibility: Visibility.Collapsed
				},
				"empty state removal");
			collection.ScrollTo(79, position: ScrollToPosition.End, animate: false);
			await RequireAsync(() => thresholdCount > 0, "remaining-items threshold");

			items.Clear();
			await RequireAsync(
				() => FindNamedElement(GetListView(collection), "EmptyViewContentControl") is FrameworkElement
				{
					Visibility: Visibility.Visible
				},
				"empty state restoration");
		}

		async Task VerifyVisibleViewsAsync()
		{
			var items = new ObservableCollection<string>(new[] { "zero", "one", "two", "three" });
			var carousel = new CarouselView
			{
				Loop = false,
				IsScrollAnimated = false,
				PeekAreaInsets = new Thickness(40, 0),
				ItemsSource = items,
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "visible item", HeightRequest = 80 })
			};
			host.MauiContent = carousel;
			await RequireAsync(
				() => carousel.VisibleViews.Count >= 2 &&
					carousel.VisibleViews.Any(view => Equals(view.BindingContext, items[0])) &&
					carousel.VisibleViews.Any(view => Equals(view.BindingContext, items[1])) &&
					VisibleItemsMatchViewport(),
				"initial and partially visible views");

			carousel.Position = 1;
			await RequireAsync(
				() => carousel.VisibleViews.Any(view => Equals(view.BindingContext, items[1])) &&
					carousel.VisibleViews.Any(view => Equals(view.BindingContext, items[2])) &&
					VisibleItemsMatchViewport(),
				"visible views after scrolling");

			var previousItemWidth = ((FrameworkElement)GetListView(carousel).ContainerFromIndex(1)).ActualWidth;
			host.Width = 420;
			await RequireAsync(() =>
				GetListView(carousel).ActualWidth >= 400 &&
				GetListView(carousel).ContainerFromIndex(1) is FrameworkElement { ActualWidth: var width } &&
				Math.Abs(width - previousItemWidth) > 1 &&
				VisibleItemsMatchViewport(), "resized item geometry and exact visible set");

			items.Clear();
			await RequireAsync(() => carousel.VisibleViews.Count == 0, "visible views after source clear");

			host.MauiContent = null;
			items.Add("after teardown");
			await Task.Delay(100);
			if (carousel.VisibleViews.Count != 0)
				throw new InvalidOperationException("VisibleViews changed after handler teardown.");

			bool VisibleItemsMatchViewport()
			{
				var list = GetListView(carousel);
				var scroll = GetScrollViewer(list);
				var expected = Enumerable.Range(0, items.Count)
					.Where(index => IsIndexVisible(list, index, scroll, horizontal: true))
					.Select(index => (object)items[index]);
				return carousel.VisibleViews.Count > 0 &&
					carousel.VisibleViews.Select(view => view.BindingContext).SequenceEqual(expected);
			}
		}

		async Task VerifyVisibleViewsTeardownAsync()
		{
			var carousel = new CarouselView
			{
				Loop = false,
				IsScrollAnimated = false,
				PeekAreaInsets = new Thickness(40, 0),
				ItemsSource = new[] { "zero", "one", "two", "three" },
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "visible item", HeightRequest = 80 })
			};
			host.MauiContent = carousel;
			await RequireAsync(() => carousel.VisibleViews.Count >= 2, "visible views before callback teardown");
			var detached = false;
			NotifyCollectionChangedEventHandler detach = (_, args) =>
			{
				if (!detached && args.Action == NotifyCollectionChangedAction.Add)
				{
					detached = true;
					carousel.Handler?.DisconnectHandler();
				}
			};
			carousel.VisibleViews.CollectionChanged += detach;
			try
			{
				carousel.Position = 2;
				await RequireAsync(() => detached, "teardown from VisibleViews notification");
				await Task.Delay(100);
				if (carousel.VisibleViews.Count != 0)
					throw new InvalidOperationException("A stale visible-view update repopulated a disconnected carousel.");
			}
			finally
			{
				carousel.VisibleViews.CollectionChanged -= detach;
			}
		}

		async Task VerifyMultipleSelectionAsync()
		{
			var items = new ObservableCollection<string>(new[] { "zero", "one", "two", "three" });
			var collection = new CollectionView
			{
				ItemsSource = items,
				SelectionMode = MauiSelectionMode.Multiple,
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "selectable", HeightRequest = 40 })
			};
			host.MauiContent = collection;
			await RequireAsync(() => collection.Handler?.PlatformView is ListViewBase { IsLoaded: true }, "multiple-selection collection");
			var list = GetListView(collection);

			collection.SelectedItems.Add(items[0]);
			collection.SelectedItems.Add(items[2]);
			await RequireAsync(() => list.SelectedItems.Count == 2, "programmatic multiple selection");

			list.SelectedItems.Add(list.Items[3]);
			await RequireAsync(() => collection.SelectedItems.Contains(items[3]), "platform multiple selection");

			items.Remove(items[2]);
			await RequireAsync(
				() => !collection.SelectedItems.Contains("two") && list.SelectedItems.Count == 2,
				"selection after selected item removal");

			host.MauiContent = null;
			items.Add("after teardown");
		}

		async Task VerifyGroupingAsync()
		{
			var groups = new ObservableCollection<ProbeGroup>
			{
				new("First", new[] { "one", "two" }),
				new("Second", new[] { "three" })
			};
			var collection = new CollectionView
			{
				IsGrouped = true,
				ItemsSource = groups,
				GroupHeaderTemplate = new MauiTemplate(() =>
				{
					return new MauiLabel { Text = "group header", HeightRequest = 24 };
				}),
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "group item", HeightRequest = 40 })
			};
			host.MauiContent = collection;
			await RequireAsync(
				() => collection.Handler?.PlatformView is ListViewBase { IsLoaded: true, IsGrouping: true } list &&
					list.ActualHeight > 0 &&
					list.ItemsSource is Microsoft.UI.Xaml.Data.ICollectionView { CollectionGroups.Count: 2 } &&
					list.GroupStyleSelector is not null,
				"grouped collection realization");

			groups.Add(new ProbeGroup("Third", new[] { "four" }));
			await RequireAsync(
				() => GetListView(collection).ItemsSource is Microsoft.UI.Xaml.Data.ICollectionView
				{
					CollectionGroups.Count: 3
				},
				"grouped source update");
		}
	}

	sealed class NotifyingEnumerable(ObservableCollection<string> items) : IEnumerable, INotifyCollectionChanged
	{
		public event NotifyCollectionChangedEventHandler? CollectionChanged
		{
			add => items.CollectionChanged += value;
			remove => items.CollectionChanged -= value;
		}
		public IEnumerator GetEnumerator() => items.GetEnumerator();
	}

	sealed class ProbeGroup(string name, IEnumerable<string> items) : ObservableCollection<string>(items)
	{
		public string Name { get; } = name;
	}

	static async Task RequireAsync(Func<bool> condition, string name)
	{
		if (!await Tier2Probe.WaitForAsync(condition))
			throw new InvalidOperationException($"Timed out: {name}.");
	}

	static ListViewBase GetListView(MauiItemsView itemsView) =>
		itemsView.Handler?.PlatformView as ListViewBase
		?? throw new InvalidOperationException("The items view has no ListViewBase platform view.");

	static NativeScrollViewer GetScrollViewer(ListViewBase listView) =>
		FindScrollViewer(listView)
		?? throw new InvalidOperationException("The items control has no ScrollViewer.");

	static NativeScrollViewer? FindScrollViewer(DependencyObject parent)
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			var child = VisualTreeHelper.GetChild(parent, i);
			if (child is NativeScrollViewer scrollViewer)
				return scrollViewer;
			if (FindScrollViewer(child) is { } descendant)
				return descendant;
		}
		return null;
	}

	static bool IsIndexVisible(ListViewBase listView, int index, FrameworkElement viewport, bool horizontal)
	{
		if (listView.ContainerFromIndex(index) is not FrameworkElement container)
			return false;

		var bounds = container.TransformToVisual(viewport).TransformBounds(
			new global::Windows.Foundation.Rect(0, 0, container.ActualWidth, container.ActualHeight));
		return horizontal
			? bounds.Right > 0 && bounds.Left < viewport.ActualWidth
			: bounds.Bottom > 0 && bounds.Top < viewport.ActualHeight;
	}

	static FrameworkElement? FindNamedElement(DependencyObject parent, string name)
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			var child = VisualTreeHelper.GetChild(parent, i);
			if (child is FrameworkElement { Name: var childName } element &&
				string.Equals(childName, name, StringComparison.Ordinal))
			{
				return element;
			}
			if (FindNamedElement(child, name) is { } descendant)
				return descendant;
		}
		return null;
	}
}
