using System;
using System.Collections;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;

using UnoItemsRepeater = Microsoft.UI.Xaml.Controls.ItemsRepeater;
using UnoOrientation = Microsoft.UI.Xaml.Controls.Orientation;
using UnoScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;
using UnoScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode;
using UnoScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
using UnoStackLayout = Microsoft.UI.Xaml.Controls.StackLayout;
using PlatformView = Microsoft.UI.Xaml.FrameworkElement;

namespace Microsoft.Maui.Controls.Embedding.Uno;

/// <summary>
/// A <see cref="CarouselView"/> handler built on <c>ItemsRepeater</c>, for the same reason as
/// <see cref="UnoCollectionViewHandler"/>: MAUI's Windows handler renders through <c>ListViewBase</c>, which
/// paints nothing on WebAssembly.
/// </summary>
/// <remarks>
/// <para>
/// A carousel is a horizontal repeater whose items are exactly one viewport wide, plus a two-way link
/// between the scroll offset and <see cref="CarouselView.Position"/>. Linking through <c>Position</c> is
/// what makes a bound <c>IndicatorView</c> follow, because <c>CarouselView.IndicatorView</c> binds the two
/// together on the virtual side.
/// </para>
/// <para>
/// Snapping is done by hand. <c>ItemsRepeater</c> does not implement <c>IScrollSnapPointsInfo</c>, so the
/// <c>ScrollViewer</c> has no snap points to work with; instead the nearest item is scrolled to once the
/// view stops moving.
/// </para>
/// <para>
/// <see cref="CarouselView.Loop"/> — which defaults to <see langword="true"/> — is not implemented, and
/// neither are <c>PeekAreaInsets</c>, <c>IsBounceEnabled</c> or <c>VisibleViews</c>. Loop in particular is
/// a silent difference from the default handler rather than an error.
/// </para>
/// </remarks>
public partial class UnoCarouselViewHandler : ViewHandler<CarouselView, PlatformView>
{
	public static readonly IPropertyMapper<CarouselView, UnoCarouselViewHandler> Mapper =
		new PropertyMapper<CarouselView, UnoCarouselViewHandler>(ViewMapper)
		{
			[nameof(ItemsView.ItemsSource)] = MapItemsSource,
			[nameof(ItemsView.ItemTemplate)] = MapItemTemplate,
			[nameof(CarouselView.Position)] = MapPosition,
			[nameof(CarouselView.CurrentItem)] = MapCurrentItem,
			[nameof(CarouselView.IsSwipeEnabled)] = MapIsSwipeEnabled,
		};

	UnoItemsRepeater? _repeater;
	UnoScrollViewer? _scrollViewer;
	MauiTemplateElementFactory? _factory;
	bool _isSyncingPosition;

	public UnoCarouselViewHandler()
		: base(Mapper)
	{
	}

	public UnoCarouselViewHandler(IPropertyMapper? mapper)
		: base(mapper ?? Mapper)
	{
	}

	int ItemCount
	{
		get
		{
			var count = 0;

			if (VirtualView?.ItemsSource is { } source)
			{
				foreach (var _ in source)
				{
					count++;
				}
			}

			return count;
		}
	}

	protected override PlatformView CreatePlatformView()
	{
		_repeater = new UnoItemsRepeater
		{
			Layout = new UnoStackLayout { Orientation = UnoOrientation.Horizontal },
		};

		_scrollViewer = new UnoScrollViewer
		{
			Content = _repeater,
			HorizontalScrollBarVisibility = UnoScrollBarVisibility.Hidden,
			VerticalScrollBarVisibility = UnoScrollBarVisibility.Disabled,
			HorizontalScrollMode = UnoScrollMode.Auto,
			VerticalScrollMode = UnoScrollMode.Disabled,
		};

		_scrollViewer.SizeChanged += OnScrollViewerSizeChanged;
		_scrollViewer.ViewChanged += OnViewChanged;

		return _scrollViewer;
	}

	protected override void DisconnectHandler(PlatformView platformView)
	{
		if (_scrollViewer is not null)
		{
			_scrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
			_scrollViewer.ViewChanged -= OnViewChanged;
		}

		_factory?.Clear();
		_factory = null;

		if (_repeater is not null)
		{
			_repeater.ItemsSource = null;
			_repeater.ItemTemplate = null;
		}

		base.DisconnectHandler(platformView);
	}

	public static void MapItemsSource(UnoCarouselViewHandler handler, CarouselView carousel) =>
		handler.UpdateItemsSource(carousel.ItemsSource);

	public static void MapItemTemplate(UnoCarouselViewHandler handler, CarouselView carousel) =>
		handler.UpdateItemTemplate(carousel);

	public static void MapPosition(UnoCarouselViewHandler handler, CarouselView carousel) =>
		handler.ScrollToPosition(carousel.Position, animate: true);

	public static void MapCurrentItem(UnoCarouselViewHandler handler, CarouselView carousel)
	{
		// Position is the authority; CurrentItem is kept in step from there.
	}

	public static void MapIsSwipeEnabled(UnoCarouselViewHandler handler, CarouselView carousel)
	{
		if (handler._scrollViewer is { } scrollViewer)
		{
			scrollViewer.HorizontalScrollMode = carousel.IsSwipeEnabled
				? UnoScrollMode.Auto
				: UnoScrollMode.Disabled;
		}
	}

	void UpdateItemsSource(IEnumerable? itemsSource)
	{
		if (_repeater is not null)
		{
			_repeater.ItemsSource = itemsSource;
		}
	}

	void UpdateItemTemplate(CarouselView carousel)
	{
		if (_repeater is null || MauiContext is null)
		{
			return;
		}

		_factory?.Clear();

		if (carousel.ItemTemplate is not { } template)
		{
			_factory = null;
			_repeater.ItemTemplate = null;
			return;
		}

		_factory = new MauiTemplateElementFactory(carousel, MauiContext, template, onItemInvoked: null)
		{
			ItemWidth = _scrollViewer?.ActualWidth,
		};

		_repeater.ItemTemplate = _factory;
	}

	void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs args)
	{
		if (_factory is null || args.NewSize.Width <= 0)
		{
			return;
		}

		_factory.ItemWidth = args.NewSize.Width;
		_factory.UpdateRealizedItemSizes();
		_repeater?.InvalidateMeasure();

		// The offset that represents the current position moves with the viewport.
		if (VirtualView is { } carousel)
		{
			ScrollToPosition(carousel.Position, animate: false);
		}
	}

	void OnViewChanged(object? sender, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs args)
	{
		if (args.IsIntermediate || _isSyncingPosition || _scrollViewer is null || VirtualView is null)
		{
			return;
		}

		var viewport = _scrollViewer.ActualWidth;

		if (viewport <= 0)
		{
			return;
		}

		var count = ItemCount;

		if (count == 0)
		{
			return;
		}

		var index = (int)Math.Round(_scrollViewer.HorizontalOffset / viewport, MidpointRounding.AwayFromZero);
		index = Math.Clamp(index, 0, count - 1);

		// Settle on the item, then report it. Doing it in this order keeps the reported position and what
		// is on screen from disagreeing while the animation runs.
		ScrollToPosition(index, animate: true);

		if (VirtualView.Position != index)
		{
			VirtualView.Position = index;
		}

		VirtualView.CurrentItem = ItemAt(index);
	}

	void ScrollToPosition(int position, bool animate)
	{
		if (_scrollViewer is null || _scrollViewer.ActualWidth <= 0)
		{
			return;
		}

		var target = position * _scrollViewer.ActualWidth;

		if (Math.Abs(_scrollViewer.HorizontalOffset - target) < 0.5)
		{
			return;
		}

		_isSyncingPosition = true;

		try
		{
			_scrollViewer.ChangeView(target, null, null, disableAnimation: !animate);
		}
		finally
		{
			_isSyncingPosition = false;
		}
	}

	object? ItemAt(int index)
	{
		if (VirtualView?.ItemsSource is not { } source)
		{
			return null;
		}

		var current = 0;

		foreach (var item in source)
		{
			if (current++ == index)
			{
				return item;
			}
		}

		return null;
	}
}
