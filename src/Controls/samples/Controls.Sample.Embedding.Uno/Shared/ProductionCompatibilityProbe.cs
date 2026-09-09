using System.Text;
using System.Collections.ObjectModel;
using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Embedding.Uno;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml.Controls;
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
			await RunCase("failed item creation rolls back ownership", VerifyFactoryFailureAsync);
			await RunCase("carousel CurrentItem moves the platform view", VerifyCurrentItemAsync);
			await RunCase("carousel loop follows asynchronous source growth and reset", () => VerifyLoopGrowthAsync(false));
			await RunCase("carousel loop observes notifying enumerable sources", () => VerifyLoopGrowthAsync(true));
			await RunCase("vertical CollectionView ScrollTo reaches the requested item",
				() => VerifyScrollToAsync(ItemsLayoutOrientation.Vertical));
			await RunCase("horizontal CollectionView ScrollTo reaches the requested item",
				() => VerifyScrollToAsync(ItemsLayoutOrientation.Horizontal));
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

		async Task VerifyFactoryFailureAsync()
		{
			var failed = new LifecycleRegressionProbe.FailingEmbeddingView { FailDuringCreation = true };
			var collection = new CollectionView
			{
				ItemsSource = Array.Empty<object>(),
				ItemTemplate = new MauiTemplate(() => failed)
			};
			var handler = new UnoCollectionViewHandler();
			handler.SetMauiContext(session.WindowContext);
			collection.Handler = handler;
			host.MauiContent = collection;
			try
			{
				await RequireAsync(() => handler.PlatformView is NativeScrollViewer { IsLoaded: true }, "collection attachment");
				var repeater = (ItemsRepeater)((NativeScrollViewer)handler.PlatformView).Content;
				var factory = (ElementFactory)repeater.ItemTemplate;
				try
				{
					factory.GetElement(new ElementFactoryGetArgs { Data = new object(), Parent = repeater });
					throw new InvalidOperationException("The failing template unexpectedly succeeded.");
				}
				catch (InvalidOperationException error) when (ReferenceEquals(error, failed.Failure))
				{
				}
				if (failed.Parent is not null || failed.Handler is not null)
					throw new InvalidOperationException("The failed template retains its parent or handler.");
			}
			finally
			{
				((Microsoft.Maui.IView)failed).DisconnectHandlers();
				failed.Parent = null;
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
			var handler = new UnoCarouselViewHandler();
			handler.SetMauiContext(session.WindowContext);
			carousel.Handler = handler;
			host.MauiContent = carousel;
			var scroll = (NativeScrollViewer)handler.PlatformView;
			await RequireAsync(() => scroll.IsLoaded && scroll.ActualWidth > 0 && scroll.ExtentWidth >= scroll.ActualWidth * 3 - 1,
				"carousel layout");
			carousel.CurrentItem = items[2];
			await RequireAsync(() => carousel.Position == 2 && scroll.HorizontalOffset >= scroll.ActualWidth * 2 - 1,
				"CurrentItem/Position/offset agreement");
		}

		async Task VerifyScrollToAsync(ItemsLayoutOrientation orientation)
		{
			var collection = new CollectionView
			{
				ItemsSource = Enumerable.Range(0, 100).ToArray(),
				ItemsLayout = new LinearItemsLayout(orientation),
				ItemTemplate = new MauiTemplate(() => new MauiLabel { Text = "item", HeightRequest = 40, WidthRequest = 40 })
			};
			var handler = new UnoCollectionViewHandler();
			handler.SetMauiContext(session.WindowContext);
			collection.Handler = handler;
			host.MauiContent = collection;
			var scroll = (NativeScrollViewer)handler.PlatformView;
			var horizontal = orientation == ItemsLayoutOrientation.Horizontal;
			await RequireAsync(() => scroll.IsLoaded && (horizontal ? scroll.ExtentWidth : scroll.ExtentHeight) > 320, "collection layout");
			collection.ScrollTo(99, position: ScrollToPosition.End, animate: false);
			await RequireAsync(AtEnd, "scroll request completion");
			collection.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
			await RequireAsync(() => (horizontal ? scroll.HorizontalOffset : scroll.VerticalOffset) < 1, "scroll back to start");
			collection.ScrollTo((object)99, position: ScrollToPosition.End, animate: false);
			await RequireAsync(AtEnd, "item-based scroll completion");

			bool AtEnd() => horizontal
				? scroll.HorizontalOffset > 3000 && Math.Abs(scroll.HorizontalOffset - scroll.ScrollableWidth) < 1
				: scroll.VerticalOffset > 3000 && Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1;
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
			var handler = new UnoCarouselViewHandler();
			handler.SetMauiContext(session.WindowContext);
			carousel.Handler = handler;
			host.MauiContent = carousel;
			var scroll = (NativeScrollViewer)handler.PlatformView;
			await RequireAsync(() => scroll.IsLoaded && scroll.ActualWidth > 0, "empty carousel layout");
			items.Add("first");
			items.Add("second");
			await RequireAsync(InMiddleBlock, "loop activation after empty source grows");
			items.Clear();
			await RequireAsync(() => carousel.CurrentItem is null && carousel.VisibleViews.Count == 0, "loop reset clears stale state");
			items.Add("single");
			await RequireAsync(() => Equals(carousel.CurrentItem, "single") &&
				Math.Abs(scroll.ExtentWidth - scroll.ActualWidth) < 1, "singleton source has no repeated blocks");
			items.Add("another");
			await RequireAsync(InMiddleBlock, "loop reactivation after reset");

			bool InMiddleBlock() => scroll.ExtentWidth >= scroll.ActualWidth * 6 - 1 &&
				Math.Abs(scroll.HorizontalOffset - scroll.ActualWidth * 2) < 1;
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

	static async Task RequireAsync(Func<bool> condition, string name)
	{
		if (!await Tier2Probe.WaitForAsync(condition))
			throw new InvalidOperationException($"Timed out: {name}.");
	}
}
