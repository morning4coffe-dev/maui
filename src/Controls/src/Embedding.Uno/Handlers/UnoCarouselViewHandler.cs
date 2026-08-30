using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;

using UnoItemsRepeater = Microsoft.UI.Xaml.Controls.ItemsRepeater;
using UnoOrientation = Microsoft.UI.Xaml.Controls.Orientation;
using UnoScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;
using UnoScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode;
using UnoScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
using UnoStackLayout = Microsoft.UI.Xaml.Controls.StackLayout;
using UnoThickness = Microsoft.UI.Xaml.Thickness;
using PlatformView = Microsoft.UI.Xaml.FrameworkElement;

namespace Microsoft.Maui.Controls.Embedding.Uno;

/// <summary>
/// A <see cref="CarouselView"/> handler built on <c>ItemsRepeater</c>, for the same reason as
/// <see cref="UnoCollectionViewHandler"/>: MAUI's Windows handler renders through <c>ListViewBase</c>, which
/// paints nothing on WebAssembly.
/// </summary>
/// <remarks>
/// <para>
/// A carousel is a horizontal repeater whose items are one viewport wide, less the peek insets, plus a
/// two-way link between the scroll offset and <see cref="CarouselView.Position"/>. Linking through
/// <c>Position</c> is what makes a bound <c>IndicatorView</c> follow, because <c>CarouselView.IndicatorView</c>
/// binds the two together on the virtual side.
/// </para>
/// <para>
/// Snapping is done by hand. <c>ItemsRepeater</c> does not implement <c>IScrollSnapPointsInfo</c>, so the
/// <c>ScrollViewer</c> has no snap points to work with; instead the nearest item is scrolled to once the
/// view stops moving.
/// </para>
/// </remarks>
public partial class UnoCarouselViewHandler : ViewHandler<CarouselView, PlatformView>
{
	public static readonly IPropertyMapper<CarouselView, UnoCarouselViewHandler> Mapper =
		new PropertyMapper<CarouselView, UnoCarouselViewHandler>(ViewMapper)
		{
			[nameof(ItemsView.ItemsSource)] = MapItemsSource,
			[nameof(ItemsView.ItemTemplate)] = MapItemTemplate,
			[nameof(CarouselView.Loop)] = MapLoop,
			[nameof(CarouselView.PeekAreaInsets)] = MapPeekAreaInsets,
			[nameof(CarouselView.Position)] = MapPosition,
			[nameof(CarouselView.IsSwipeEnabled)] = MapIsSwipeEnabled,
			[nameof(CarouselView.IsBounceEnabled)] = MapIsBounceEnabled,
		};

	UnoItemsRepeater? _repeater;
	UnoScrollViewer? _scrollViewer;
	MauiTemplateElementFactory? _factory;
	LoopingItemsSource? _loopingSource;
	bool _isSyncingPosition;

	public UnoCarouselViewHandler()
		: base(Mapper)
	{
	}

	public UnoCarouselViewHandler(IPropertyMapper? mapper)
		: base(mapper ?? Mapper)
	{
	}

	/// <summary>Gets the width of one carousel step, which is the viewport less the peek insets.</summary>
	double ItemStep
	{
		get
		{
			if (_scrollViewer is null || VirtualView is null)
			{
				return 0;
			}

			var insets = VirtualView.PeekAreaInsets;
			var step = _scrollViewer.ActualWidth - insets.Left - insets.Right;

			return step > 1 ? step : 0;
		}
	}

	int InnerCount
	{
		get
		{
			if (_loopingSource is not null)
			{
				return _loopingSource.InnerCount;
			}

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

		_loopingSource?.Dispose();
		_loopingSource = null;

		if (_repeater is not null)
		{
			_repeater.ItemsSource = null;
			_repeater.ItemTemplate = null;
		}

		base.DisconnectHandler(platformView);
	}

	public static void MapItemsSource(UnoCarouselViewHandler handler, CarouselView carousel) =>
		handler.UpdateItemsSource(carousel);

	public static void MapItemTemplate(UnoCarouselViewHandler handler, CarouselView carousel) =>
		handler.UpdateItemTemplate(carousel);

	// Looping changes what the repeater is bound to, so it goes back through the source.
	public static void MapLoop(UnoCarouselViewHandler handler, CarouselView carousel) =>
		handler.UpdateItemsSource(carousel);

	public static void MapPeekAreaInsets(UnoCarouselViewHandler handler, CarouselView carousel) =>
		handler.UpdateItemMetrics();

	public static void MapPosition(UnoCarouselViewHandler handler, CarouselView carousel) =>
		handler.ScrollToRealIndex(carousel.Position, animate: carousel.IsScrollAnimated);

	public static void MapIsSwipeEnabled(UnoCarouselViewHandler handler, CarouselView carousel)
	{
		if (handler._scrollViewer is { } scrollViewer)
		{
			scrollViewer.HorizontalScrollMode = carousel.IsSwipeEnabled
				? UnoScrollMode.Auto
				: UnoScrollMode.Disabled;
		}
	}

	/// <summary>
	/// Maps <see cref="CarouselView.IsBounceEnabled"/> onto scroll inertia.
	/// </summary>
	/// <remarks>
	/// The Uno <c>ScrollViewer</c> has no bounce or over-scroll: it cannot travel past its extent, so the
	/// effect the property names does not exist to switch off. Inertia is the nearest real behaviour — with
	/// it on, a flick glides and overshoots before snapping back to an item, which is what a bouncing
	/// carousel feels like; with it off the carousel stops where it is released and snaps. This is a
	/// deliberate approximation, not a faithful mapping.
	/// </remarks>
	public static void MapIsBounceEnabled(UnoCarouselViewHandler handler, CarouselView carousel)
	{
		if (handler._scrollViewer is { } scrollViewer)
		{
			scrollViewer.IsScrollInertiaEnabled = carousel.IsBounceEnabled;
		}
	}

	void UpdateItemsSource(CarouselView carousel)
	{
		if (_repeater is null)
		{
			return;
		}

		_loopingSource?.Dispose();
		_loopingSource = carousel.Loop ? LoopingItemsSource.TryCreate(carousel.ItemsSource) : null;

		_repeater.ItemsSource = (IEnumerable?)_loopingSource ?? carousel.ItemsSource;

		// A new source resets the extent, so the current position has to be re-established against it.
		ScrollToRealIndex(carousel.Position, animate: false);
		UpdateVisibleViews();
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

		_factory = new MauiTemplateElementFactory(carousel, MauiContext, template, onItemInvoked: null);
		_repeater.ItemTemplate = _factory;

		UpdateItemMetrics();
	}

	/// <summary>Sizes items to the viewport less the peek insets, and insets the strip to match.</summary>
	void UpdateItemMetrics()
	{
		if (_factory is null || _repeater is null || VirtualView is null)
		{
			return;
		}

		var step = ItemStep;

		if (step <= 0)
		{
			return;
		}

		var insets = VirtualView.PeekAreaInsets;

		_factory.ItemWidth = step;
		_factory.UpdateRealizedItemSizes();

		// The margin is what makes the first and last items able to sit in the same place as the others:
		// without it there would be nothing to scroll into the peek area at either end.
		_repeater.Margin = new UnoThickness(insets.Left, insets.Top, insets.Right, insets.Bottom);
		_repeater.InvalidateMeasure();

		ScrollToRealIndex(VirtualView.Position, animate: false);
	}

	void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs args)
	{
		if (args.NewSize.Width > 0)
		{
			UpdateItemMetrics();
			UpdateVisibleViews();
		}
	}

	void OnViewChanged(object? sender, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs args)
	{
		if (args.IsIntermediate || _isSyncingPosition || _scrollViewer is null || VirtualView is null)
		{
			return;
		}

		var step = ItemStep;
		var count = InnerCount;

		if (step <= 0 || count == 0)
		{
			return;
		}

		var index = (int)Math.Round(_scrollViewer.HorizontalOffset / step, MidpointRounding.AwayFromZero);

		if (_loopingSource is not null)
		{
			index = Math.Clamp(index, 0, _loopingSource.Count - 1);

			var realIndex = _loopingSource.ToInnerIndex(index);

			// Settle on the item first, then jump back to the middle block if we have drifted out of it.
			// The jump is a whole block, so the item under the viewport does not change and it is invisible.
			if (_loopingSource.IsOutsideMiddleBlock(index))
			{
				SetOffset(_loopingSource.ToMiddleBlockIndex(realIndex) * step, animate: false);
			}
			else
			{
				SetOffset(index * step, animate: true);
			}

			ReportPosition(realIndex);
		}
		else
		{
			index = Math.Clamp(index, 0, count - 1);
			SetOffset(index * step, animate: true);
			ReportPosition(index);
		}

		UpdateVisibleViews();
	}

	void ReportPosition(int realIndex)
	{
		if (VirtualView is null)
		{
			return;
		}

		if (VirtualView.Position != realIndex)
		{
			VirtualView.Position = realIndex;
		}

		var item = ItemAt(realIndex);

		if (!Equals(VirtualView.CurrentItem, item))
		{
			VirtualView.CurrentItem = item;
		}
	}

	void ScrollToRealIndex(int realIndex, bool animate)
	{
		var step = ItemStep;

		if (step <= 0)
		{
			return;
		}

		var index = _loopingSource is null
			? realIndex
			: _loopingSource.ToMiddleBlockIndex(_loopingSource.ToInnerIndex(realIndex));

		SetOffset(index * step, animate);
	}

	void SetOffset(double offset, bool animate)
	{
		if (_scrollViewer is null || Math.Abs(_scrollViewer.HorizontalOffset - offset) < 0.5)
		{
			return;
		}

		_isSyncingPosition = true;

		try
		{
			_scrollViewer.ChangeView(offset, null, null, disableAnimation: !animate);
		}
		finally
		{
			_isSyncingPosition = false;
		}
	}

	/// <summary>
	/// Publishes the item views currently on screen into <see cref="CarouselView.VisibleViews"/>.
	/// </summary>
	/// <remarks>
	/// The property is read-only but hands back a live <c>ObservableCollection</c>, so it is populated by
	/// mutating that collection rather than by setting the property. With peek insets on, the neighbours are
	/// partly on screen and belong in here too, which is why the range is computed from the viewport rather
	/// than from the current index alone.
	/// </remarks>
	void UpdateVisibleViews()
	{
		if (_repeater is null || _scrollViewer is null || _factory is null || VirtualView is null)
		{
			return;
		}

		var step = ItemStep;

		if (step <= 0)
		{
			return;
		}

		var offset = _scrollViewer.HorizontalOffset;
		var first = (int)Math.Floor(offset / step);
		var last = (int)Math.Floor((offset + _scrollViewer.ActualWidth - 1) / step);

		var upperBound = _loopingSource?.Count ?? InnerCount;

		first = Math.Max(0, first);
		last = Math.Min(upperBound - 1, last);

		var visible = new List<View>();

		for (var i = first; i <= last; i++)
		{
			if (_repeater.TryGetElement(i) is { } element && _factory.TryGetView(element) is { } view)
			{
				visible.Add(view);
			}
		}

		var target = VirtualView.VisibleViews;

		if (target.Count == visible.Count)
		{
			var same = true;

			for (var i = 0; i < visible.Count; i++)
			{
				if (!ReferenceEquals(target[i], visible[i]))
				{
					same = false;
					break;
				}
			}

			if (same)
			{
				return;
			}
		}

		target.Clear();

		foreach (var view in visible)
		{
			target.Add(view);
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
