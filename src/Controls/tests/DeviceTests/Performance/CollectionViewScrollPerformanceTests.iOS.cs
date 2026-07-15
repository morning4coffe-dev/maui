using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Foundation;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Handlers.Items2;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using UIKit;
using Xunit;
using Xunit.Sdk;
using static Microsoft.Maui.DeviceTests.AssertHelpers;

namespace Microsoft.Maui.DeviceTests
{
	[Collection(RunInNewWindowCollection)]
	[Category(TestCategory.PerformanceCollectionViewScroll)]
	public class CollectionViewScrollPerformanceTests : ControlsHandlerTestBase
	{
		const int GroupCount = 5;
		const int ItemsPerGroup = 20;
		const int WarmupCount = 2;
		const int IterationCount = 10;

		[Fact(DisplayName = "Grouped CollectionView2 ScrollTo MakeVisible performance")]
		public async Task GroupedScrollToMakeVisible()
		{
			EnsureHandlerCreated(builder =>
			{
				builder.ConfigureMauiHandlers(handlers =>
				{
					handlers.AddHandler<CollectionView, CollectionViewHandler2>();
					handlers.AddHandler<Label, LabelHandler>();
				});
			});

			var groups = CreateGroups();
			var collectionView = new CollectionView
			{
				HeightRequest = 700,
				WidthRequest = 390,
				IsGrouped = true,
				ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,
				ItemsSource = groups,
				ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical),
				ItemTemplate = new DataTemplate(() =>
				{
					var label = new Label
					{
						HeightRequest = 80,
						Padding = new Thickness(12),
						VerticalTextAlignment = TextAlignment.Center
					};
					label.SetBinding(Label.TextProperty, nameof(PerformanceItem.Name));
					return label;
				}),
				GroupHeaderTemplate = new DataTemplate(() =>
				{
					var label = new Label
					{
						HeightRequest = 32,
						FontAttributes = FontAttributes.Bold
					};
					label.SetBinding(Label.TextProperty, nameof(PerformanceGroup.Name));
					return label;
				})
			};

			await CreateHandlerAndAddToWindow<CollectionViewHandler2>(collectionView, async handler =>
			{
				UICollectionView platformView = handler.Controller.CollectionView;

				await AssertEventually(
					() => platformView.NumberOfSections() == GroupCount
						&& platformView.NumberOfItemsInSection(GroupCount - 1) == ItemsPerGroup);

				DevicePerformanceResult result = await DevicePerformanceMeasurement.MeasureAsync(
					"collectionview-grouped-scrollto-makevisible",
					WarmupCount,
					IterationCount,
					async iteration =>
					{
						bool scrollToEnd = iteration % 2 == 0;
						int section = scrollToEnd ? GroupCount - 1 : 0;
						int itemIndex = scrollToEnd ? ItemsPerGroup - 1 : 0;
						PerformanceGroup group = groups[section];
						PerformanceItem item = group[itemIndex];
						NSIndexPath expectedIndexPath = NSIndexPath.FromItemSection(itemIndex, section);

						await InvokeOnMainThreadAsync(() =>
							collectionView.ScrollTo(item, group, ScrollToPosition.MakeVisible, animate: true));

						await WaitForScrollToSettle(platformView, expectedIndexPath);
					},
					new Dictionary<string, double>(StringComparer.Ordinal)
					{
						["groupCount"] = GroupCount,
						["itemsPerGroup"] = ItemsPerGroup,
						["itemHeight"] = 80
					});

				DevicePerformanceReporter.Write(result);
			});
		}

		async Task WaitForScrollToSettle(UICollectionView collectionView, NSIndexPath expectedIndexPath)
		{
			var timeout = System.Diagnostics.Stopwatch.StartNew();
			double previousX = double.NaN;
			double previousY = double.NaN;
			int stableSamples = 0;

			while (timeout.Elapsed < TimeSpan.FromSeconds(10))
			{
				await Task.Delay(50).ConfigureAwait(false);

				var state = await InvokeOnMainThreadAsync(() =>
				{
					var offset = collectionView.ContentOffset;
					bool expectedItemVisible = collectionView.IndexPathsForVisibleItems.Any(indexPath =>
						indexPath.Section == expectedIndexPath.Section
						&& indexPath.Item == expectedIndexPath.Item);

					return new
					{
						X = (double)offset.X,
						Y = (double)offset.Y,
						IsMoving = collectionView.Dragging || collectionView.Decelerating || collectionView.Tracking,
						ExpectedItemVisible = expectedItemVisible
					};
				});

				bool offsetStable = !double.IsNaN(previousX)
					&& Math.Abs(state.X - previousX) < 0.5
					&& Math.Abs(state.Y - previousY) < 0.5;

				stableSamples = offsetStable && !state.IsMoving ? stableSamples + 1 : 0;
				previousX = state.X;
				previousY = state.Y;

				if (stableSamples >= 3 && state.ExpectedItemVisible)
					return;
			}

			throw new XunitException(
				$"CollectionView did not settle with item {expectedIndexPath.Item} in section {expectedIndexPath.Section} visible.");
		}

		static List<PerformanceGroup> CreateGroups()
		{
			var groups = new List<PerformanceGroup>(GroupCount);
			for (int groupIndex = 0; groupIndex < GroupCount; groupIndex++)
			{
				var items = new List<PerformanceItem>(ItemsPerGroup);
				for (int itemIndex = 0; itemIndex < ItemsPerGroup; itemIndex++)
					items.Add(new PerformanceItem($"Group {groupIndex + 1}, Item {itemIndex + 1}"));

				groups.Add(new PerformanceGroup($"Group {groupIndex + 1}", items));
			}

			return groups;
		}

		sealed class PerformanceGroup : List<PerformanceItem>
		{
			public PerformanceGroup(string name, IEnumerable<PerformanceItem> items)
				: base(items)
			{
				Name = name;
			}

			public string Name { get; }
		}

		sealed class PerformanceItem
		{
			public PerformanceItem(string name)
			{
				Name = name;
			}

			public string Name { get; }
		}
	}
}
