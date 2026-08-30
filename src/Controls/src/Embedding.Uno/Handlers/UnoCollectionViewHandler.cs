using System;
using System.Collections;
using Microsoft.Maui.Handlers;

using UnoItemsRepeater = Microsoft.UI.Xaml.Controls.ItemsRepeater;
using UnoOrientation = Microsoft.UI.Xaml.Controls.Orientation;
using UnoScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;
using UnoScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
using UnoStackLayout = Microsoft.UI.Xaml.Controls.StackLayout;
using UnoUniformGridLayout = Microsoft.UI.Xaml.Controls.UniformGridLayout;
using PlatformView = Microsoft.UI.Xaml.FrameworkElement;

namespace Microsoft.Maui.Controls.Embedding.Uno;

/// <summary>
/// A <see cref="CollectionView"/> handler built on <c>ItemsRepeater</c> rather than
/// <c>ListViewBase</c>, so that it renders on every Uno target.
/// </summary>
/// <remarks>
/// <para>
/// MAUI's Windows handler renders through <c>ListViewBase</c> with a custom control template and
/// <c>ItemsStackPanel</c> virtualization. That path realizes and arranges its item containers correctly on
/// WebAssembly and then paints nothing, which is not something the embedding layer can work around from the
/// outside. <c>ItemsRepeater</c> is the portable primitive: a layout plus an element factory, with no
/// control template and no platform-specific panel.
/// </para>
/// <para>
/// This maps the items surface, not all of <see cref="CollectionView"/>. Grouping, reordering, incremental
/// loading, headers and footers, multiple selection and the empty view are not implemented; the properties
/// simply have no effect rather than throwing. See the sample README for the current list.
/// </para>
/// </remarks>
public partial class UnoCollectionViewHandler : ViewHandler<ReorderableItemsView, PlatformView>
{
	public static readonly IPropertyMapper<ReorderableItemsView, UnoCollectionViewHandler> Mapper =
		new PropertyMapper<ReorderableItemsView, UnoCollectionViewHandler>(ViewMapper)
		{
			[nameof(ItemsView.ItemsSource)] = MapItemsSource,
			[nameof(ItemsView.ItemTemplate)] = MapItemTemplate,
			[nameof(StructuredItemsView.ItemsLayout)] = MapItemsLayout,
		};

	UnoItemsRepeater? _repeater;
	UnoScrollViewer? _scrollViewer;
	MauiTemplateElementFactory? _factory;

	public UnoCollectionViewHandler()
		: base(Mapper)
	{
	}

	public UnoCollectionViewHandler(IPropertyMapper? mapper)
		: base(mapper ?? Mapper)
	{
	}

	protected override PlatformView CreatePlatformView()
	{
		_repeater = new UnoItemsRepeater();

		_scrollViewer = new UnoScrollViewer
		{
			Content = _repeater,
			HorizontalScrollBarVisibility = UnoScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = UnoScrollBarVisibility.Auto,
			HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled,
			VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Auto,
		};

		return _scrollViewer;
	}

	protected override void DisconnectHandler(PlatformView platformView)
	{
		_factory?.Clear();
		_factory = null;

		if (_repeater is not null)
		{
			_repeater.ItemsSource = null;
			_repeater.ItemTemplate = null;
		}

		base.DisconnectHandler(platformView);
	}

	public static void MapItemsSource(UnoCollectionViewHandler handler, ReorderableItemsView itemsView) =>
		handler.UpdateItemsSource(itemsView.ItemsSource);

	public static void MapItemTemplate(UnoCollectionViewHandler handler, ReorderableItemsView itemsView) =>
		handler.UpdateItemTemplate(itemsView);

	public static void MapItemsLayout(UnoCollectionViewHandler handler, ReorderableItemsView itemsView) =>
		handler.UpdateItemsLayout(itemsView);

	void UpdateItemsSource(IEnumerable? itemsSource)
	{
		if (_repeater is not null)
		{
			_repeater.ItemsSource = itemsSource;
		}
	}

	void UpdateItemTemplate(ReorderableItemsView itemsView)
	{
		if (_repeater is null || MauiContext is null)
		{
			return;
		}

		_factory?.Clear();

		if (itemsView.ItemTemplate is not { } template)
		{
			_factory = null;
			_repeater.ItemTemplate = null;
			return;
		}

		_factory = new MauiTemplateElementFactory(
			itemsView,
			MauiContext,
			template,
			item => OnItemInvoked(itemsView, item));

		_repeater.ItemTemplate = _factory;
	}

	void UpdateItemsLayout(ReorderableItemsView itemsView)
	{
		if (_repeater is null || _scrollViewer is null)
		{
			return;
		}

		// A grid layout scrolls along its own orientation; a linear layout scrolls the other way.
		switch (itemsView.ItemsLayout)
		{
			case GridItemsLayout grid:
				_repeater.Layout = new UnoUniformGridLayout
				{
					MaximumRowsOrColumns = Math.Max(1, grid.Span),
					Orientation = grid.Orientation == ItemsLayoutOrientation.Horizontal
						? UnoOrientation.Vertical
						: UnoOrientation.Horizontal,
					MinItemWidth = grid.HorizontalItemSpacing >= 0 ? double.NaN : double.NaN,
					MinColumnSpacing = grid.HorizontalItemSpacing,
					MinRowSpacing = grid.VerticalItemSpacing,
				};
				SetScrollDirection(grid.Orientation);
				break;

			case LinearItemsLayout linear:
				_repeater.Layout = new UnoStackLayout
				{
					Orientation = linear.Orientation == ItemsLayoutOrientation.Horizontal
						? UnoOrientation.Horizontal
						: UnoOrientation.Vertical,
					Spacing = linear.ItemSpacing,
				};
				SetScrollDirection(linear.Orientation);
				break;

			default:
				_repeater.Layout = new UnoStackLayout { Orientation = UnoOrientation.Vertical };
				SetScrollDirection(ItemsLayoutOrientation.Vertical);
				break;
		}
	}

	void SetScrollDirection(ItemsLayoutOrientation orientation)
	{
		if (_scrollViewer is null)
		{
			return;
		}

		var horizontal = orientation == ItemsLayoutOrientation.Horizontal;

		_scrollViewer.HorizontalScrollBarVisibility = horizontal
			? UnoScrollBarVisibility.Auto
			: UnoScrollBarVisibility.Disabled;
		_scrollViewer.HorizontalScrollMode = horizontal
			? Microsoft.UI.Xaml.Controls.ScrollMode.Auto
			: Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
		_scrollViewer.VerticalScrollBarVisibility = horizontal
			? UnoScrollBarVisibility.Disabled
			: UnoScrollBarVisibility.Auto;
		_scrollViewer.VerticalScrollMode = horizontal
			? Microsoft.UI.Xaml.Controls.ScrollMode.Disabled
			: Microsoft.UI.Xaml.Controls.ScrollMode.Auto;
	}

	static void OnItemInvoked(ReorderableItemsView itemsView, object? item)
	{
		if (itemsView.SelectionMode is SelectionMode.None)
		{
			return;
		}

		itemsView.SelectedItem = item;
	}
}
