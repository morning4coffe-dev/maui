using Microsoft.Maui.Graphics.Win2D;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Syncfusion.Maui.Toolkit.Semantics;

using WRect = global::Windows.Foundation.Rect;
using WSize = global::Windows.Foundation.Size;
using LayoutPanel = Microsoft.Maui.Platform.LayoutPanel;

namespace Syncfusion.Maui.Toolkit.Platform;

/// <summary>
/// Replaces the toolkit's Windows drawing panel with one that does not need Win2D.
/// </summary>
/// <remarks>
/// <para>
/// The upstream <c>LayoutPanelExt.Windows.cs</c> hosts a Win2D <c>CanvasControl</c> and paints through a
/// <c>W2DCanvas</c> drawing session. Neither exists here: on the Uno target <c>W2DGraphicsView</c> is a
/// Skia-backed view living in the <c>Microsoft.Maui.Graphics.Win2D</c> namespace for source compatibility,
/// and there is no <c>Microsoft.Graphics.Canvas</c> at all.
/// </para>
/// <para>
/// Fortunately that shim already accepts an <see cref="IDrawable"/> directly, so the adapter is smaller than
/// the original: the drawable is handed to the view and the view does the painting, instead of the panel
/// owning a canvas and a draw callback.
/// </para>
/// <para>
/// This is written against the surface the toolkit's own handlers use rather than copied from them, so the
/// third-party source stays untouched.
/// </para>
/// </remarks>
internal partial class LayoutPanelExt : LayoutPanel
{
	DrawingOrder _drawingOrder = DrawingOrder.NoDraw;
	NativeGraphicsView? _nativeGraphicsView;
	WeakReference<SfView>? _mauiView;
	WeakReference<IDrawable>? _drawable;

	public LayoutPanelExt(SfView layout)
	{
		Drawable = layout;
		MauiView = layout;
		AllowFocusOnInteraction = true;
		UseSystemFocusVisuals = true;
		SizeChanged += OnSizeChanged;
	}

	internal Func<double, double, Size>? CrossPlatformMeasure { get; set; }

	internal Func<Rect, Size>? CrossPlatformArrange { get; set; }

	public DrawingOrder DrawingOrder
	{
		get => _drawingOrder;
		set
		{
			_drawingOrder = value;

			if (_drawingOrder == DrawingOrder.NoDraw)
			{
				RemoveDrawableView();
			}
			else
			{
				InitializeNativeGraphicsView();
				ArrangeNativeGraphicsView();
			}
		}
	}

	public IDrawable? Drawable
	{
		get => _drawable is not null && _drawable.TryGetTarget(out var value) ? value : null;
		set => _drawable = value is null ? null : new WeakReference<IDrawable>(value);
	}

	SfView? MauiView
	{
		get => _mauiView is not null && _mauiView.TryGetTarget(out var value) ? value : null;
		set => _mauiView = value is null ? null : new WeakReference<SfView>(value);
	}

	internal void InitializeNativeGraphicsView()
	{
		if (_nativeGraphicsView is null && MauiView is { } mauiView)
		{
			_nativeGraphicsView = new NativeGraphicsView(mauiView) { Drawable = Drawable };
		}

		if (_nativeGraphicsView is not null)
		{
			_nativeGraphicsView.IsHitTestVisible =
				DrawingOrder is DrawingOrder.AboveContentWithTouch or DrawingOrder.BelowContent;
		}
	}

	internal void RemoveDrawableView()
	{
		if (_nativeGraphicsView is not null && Children.Contains(_nativeGraphicsView))
		{
			Children.Remove(_nativeGraphicsView);
		}
	}

	internal void ArrangeNativeGraphicsView()
	{
		if (_nativeGraphicsView is null)
		{
			return;
		}

		if (Children.Contains(_nativeGraphicsView))
		{
			Children.Remove(_nativeGraphicsView);
		}

		// Above content draws last; anything else has to sit underneath the children.
		if (DrawingOrder is DrawingOrder.AboveContentWithTouch or DrawingOrder.AboveContent)
		{
			Children.Add(_nativeGraphicsView);
		}
		else
		{
			Children.Insert(0, _nativeGraphicsView);
		}
	}

	internal void Invalidate() => _nativeGraphicsView?.Invalidate();

	internal void InvalidateSemantics() => _nativeGraphicsView?._semanticsAutomationPeer?.InvalidateSemantics();

	internal void Dispose()
	{
		SizeChanged -= OnSizeChanged;
		_nativeGraphicsView = null;
	}

	protected override WSize MeasureOverride(WSize availableSize)
	{
		if (CrossPlatformMeasure is null)
		{
			return base.MeasureOverride(availableSize);
		}

		var measured = CrossPlatformMeasure(availableSize.Width, availableSize.Height);

		_nativeGraphicsView?.Measure(availableSize);

		return new WSize(measured.Width, measured.Height);
	}

	protected override WSize ArrangeOverride(WSize finalSize)
	{
		if (CrossPlatformArrange is null)
		{
			return base.ArrangeOverride(finalSize);
		}

		CrossPlatformArrange(new Rect(0, 0, finalSize.Width, finalSize.Height));

		if (ClipsToBounds &&
			Clip is not null &&
			(Clip.Bounds.Width != finalSize.Width || Clip.Bounds.Height != finalSize.Height))
		{
			Clip = new RectangleGeometry { Rect = new WRect(0, 0, finalSize.Width, finalSize.Height) };
		}

		_nativeGraphicsView?.Arrange(new WRect(0, 0, finalSize.Width, finalSize.Height));

		return finalSize;
	}

	void OnSizeChanged(object sender, SizeChangedEventArgs args) => _nativeGraphicsView?.Invalidate();
}

/// <summary>
/// The drawing surface the toolkit's panel hosts, backed by the Skia <c>W2DGraphicsView</c> shim.
/// </summary>
/// <remarks>
/// Kept as a <see cref="UserControl"/> because the toolkit's Windows automation peer overrides
/// <c>OnCreateAutomationPeer</c> on this type and reads <c>_semanticsAutomationPeer</c> from it.
/// </remarks>
internal partial class NativeGraphicsView : UserControl
{
	readonly W2DGraphicsView _graphicsView = new();
	WeakReference<SfView>? _mauiView;

	internal CustomAutomationPeer? _semanticsAutomationPeer;

	internal NativeGraphicsView(SfView mauiView)
	{
		MauiView = mauiView;
		Content = _graphicsView;
	}

	public IDrawable? Drawable
	{
		get => _graphicsView.Drawable;
		set
		{
			_graphicsView.Drawable = value;
			Invalidate();
		}
	}

	SfView? MauiView
	{
		get => _mauiView is not null && _mauiView.TryGetTarget(out var value) ? value : null;
		set => _mauiView = value is null ? null : new WeakReference<SfView>(value);
	}

	internal void Invalidate() => _graphicsView.Invalidate();

	protected override AutomationPeer? OnCreateAutomationPeer()
	{
		if (MauiView is not { } mauiView)
		{
			return null;
		}

		_semanticsAutomationPeer = new CustomAutomationPeer(this, mauiView);

		return _semanticsAutomationPeer;
	}
}
