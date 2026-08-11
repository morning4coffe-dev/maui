using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Extensions;
using CommunityToolkit.Maui.Core.Views;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui.Controls.Sample.Uno;

sealed class UnoDrawingViewHandler : ViewHandler<IDrawingView, PlatformTouchGraphicsView>
{
	public static readonly IPropertyMapper<IDrawingView, UnoDrawingViewHandler> Mapper =
		new PropertyMapper<IDrawingView, UnoDrawingViewHandler>(ViewMapper)
		{
			[nameof(IDrawingView.DrawAction)] = MapInvalidate,
			[nameof(IDrawingView.ShouldClearOnFinish)] = MapInvalidate,
			[nameof(IDrawingView.IsMultiLineModeEnabled)] = MapInvalidate,
			[nameof(IDrawingView.LineColor)] = MapInvalidate,
			[nameof(IDrawingView.LineWidth)] = MapInvalidate,
			[nameof(IDrawingView.Background)] = MapInvalidate,
			[nameof(IDrawingView.Lines)] = MapLines,
		};

	readonly DrawingSurfaceDrawable drawable;
	IDrawingView? connectedView;
	PlatformTouchGraphicsView? connectedPlatformView;
	GraphicsView? interactionView;
	ObservableCollection<IDrawingLine>? subscribedLines;
	DrawingLine? currentLine;
	readonly List<PendingDrawing> pendingDrawings = [];

	public UnoDrawingViewHandler()
		: base(Mapper)
	{
		drawable = new DrawingSurfaceDrawable(this);
	}

	protected override PlatformTouchGraphicsView CreatePlatformView() => new();

	public override void SetVirtualView(IView view)
	{
		if (!ReferenceEquals(connectedView, view))
		{
			CancelPendingDrawing(connectedView);
			connectedView = null;
			SubscribeToLines(null);
		}

		base.SetVirtualView(view);

		connectedView = VirtualView;
		SubscribeToLines(connectedView.Lines);
		connectedPlatformView?.Invalidate();
	}

	protected override void ConnectHandler(PlatformTouchGraphicsView platformView)
	{
		base.ConnectHandler(platformView);

		connectedView = VirtualView;
		connectedPlatformView = platformView;
		interactionView = new GraphicsView
		{
			Drawable = drawable,
		};
		interactionView.StartInteraction += OnStartInteraction;
		interactionView.DragInteraction += OnDragInteraction;
		interactionView.EndInteraction += OnEndInteraction;
		interactionView.CancelInteraction += OnCancelInteraction;

		platformView.UpdateDrawable(interactionView);
		SubscribeToLines(connectedView.Lines);
	}

	protected override void DisconnectHandler(PlatformTouchGraphicsView platformView)
	{
		CancelPendingDrawing(connectedView);
		connectedView = null;
		connectedPlatformView = null;
		SubscribeToLines(null);

		if (interactionView is not null)
		{
			interactionView.StartInteraction -= OnStartInteraction;
			interactionView.DragInteraction -= OnDragInteraction;
			interactionView.EndInteraction -= OnEndInteraction;
			interactionView.CancelInteraction -= OnCancelInteraction;
			interactionView = null;
		}

		platformView.Disconnect();
		base.DisconnectHandler(platformView);
	}

	static void MapInvalidate(UnoDrawingViewHandler handler, IDrawingView view) =>
		handler.PlatformView?.Invalidate();

	static void MapLines(UnoDrawingViewHandler handler, IDrawingView view)
	{
		handler.SubscribeToLines(view.Lines);
		handler.PlatformView?.Invalidate();
	}

	void SubscribeToLines(ObservableCollection<IDrawingLine>? lines)
	{
		if (ReferenceEquals(subscribedLines, lines))
		{
			return;
		}

		if (subscribedLines is not null)
		{
			subscribedLines.CollectionChanged -= OnLinesCollectionChanged;
		}

		subscribedLines = lines;

		if (subscribedLines is not null)
		{
			subscribedLines.CollectionChanged += OnLinesCollectionChanged;
		}
	}

	void CancelPendingDrawing(IDrawingView? view)
	{
		if (currentLine is null && pendingDrawings.Count == 0)
		{
			return;
		}

		currentLine = null;
		pendingDrawings.Clear();
		view?.OnDrawingLineCancelled();
	}

	void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
		PlatformView?.Invalidate();

	void OnStartInteraction(object? sender, TouchEventArgs e)
	{
		var virtualView = connectedView;
		var platformView = connectedPlatformView;
		if (virtualView is null || platformView is null || e.Touches is not { Length: > 0 })
		{
			return;
		}

		if (!virtualView.IsMultiLineModeEnabled)
		{
			virtualView.Lines.Clear();
		}

		var point = e.Touches[0];
		currentLine = new DrawingLine
		{
			LineColor = virtualView.LineColor,
			LineWidth = virtualView.LineWidth,
			Points = [point],
		};

		virtualView.OnDrawingLineStarted(point);
		if (ReferenceEquals(connectedPlatformView, platformView))
		{
			platformView.Invalidate();
		}
	}

	void OnDragInteraction(object? sender, TouchEventArgs e)
	{
		var virtualView = connectedView;
		var platformView = connectedPlatformView;
		var drawingLine = currentLine;
		if (virtualView is null ||
			platformView is null ||
			drawingLine is null ||
			e.Touches is not { Length: > 0 })
		{
			return;
		}

		foreach (var point in e.Touches)
		{
			drawingLine.Points.Add(point);
			virtualView.OnPointDrawn(point);
		}

		if (ReferenceEquals(connectedPlatformView, platformView))
		{
			platformView.Invalidate();
		}
	}

	void OnEndInteraction(object? sender, TouchEventArgs e)
	{
		var virtualView = connectedView;
		var platformView = connectedPlatformView;
		if (virtualView is null || platformView is null || currentLine is null)
		{
			return;
		}

		var completedLine = currentLine;
		currentLine = null;
		var pendingDrawing = new PendingDrawing(completedLine);
		pendingDrawings.Add(pendingDrawing);
		if (!platformView.DispatcherQueue.TryEnqueue(() => CompleteLine(pendingDrawing)))
		{
			pendingDrawings.Remove(pendingDrawing);
			virtualView.OnDrawingLineCancelled();
		}

		if (ReferenceEquals(connectedPlatformView, platformView))
		{
			platformView.Invalidate();
		}
	}

	void OnCancelInteraction(object? sender, EventArgs e)
	{
		var virtualView = connectedView;
		var platformView = connectedPlatformView;
		if (virtualView is null || platformView is null)
		{
			return;
		}

		if (currentLine is not null)
		{
			currentLine = null;
		}
		else if (pendingDrawings.Count > 0)
		{
			pendingDrawings.RemoveAt(pendingDrawings.Count - 1);
		}
		else
		{
			return;
		}

		virtualView.OnDrawingLineCancelled();
		if (ReferenceEquals(connectedPlatformView, platformView))
		{
			platformView.Invalidate();
		}
	}

	void CompleteLine(PendingDrawing pendingDrawing)
	{
		var virtualView = connectedView;
		var platformView = connectedPlatformView;
		if (!pendingDrawings.Remove(pendingDrawing) ||
			virtualView is null ||
			platformView is null)
		{
			return;
		}

		var shouldClearOnFinish = virtualView.ShouldClearOnFinish;
		virtualView.Lines.Add(pendingDrawing.Line);
		virtualView.OnDrawingLineCompleted(pendingDrawing.Line);

		if (shouldClearOnFinish)
		{
			virtualView.Lines.Clear();
		}

		if (ReferenceEquals(connectedPlatformView, platformView))
		{
			platformView.Invalidate();
		}
	}

	sealed class PendingDrawing(DrawingLine line)
	{
		public DrawingLine Line { get; } = line;
	}

	sealed class DrawingSurfaceDrawable(UnoDrawingViewHandler owner) : IDrawable
	{
		public void Draw(ICanvas canvas, RectF dirtyRect)
		{
			var view = owner.connectedView;
			if (view is null)
			{
				return;
			}

			canvas.SetFillPaint(
				view.Background ?? new SolidPaint(Colors.Transparent),
				dirtyRect);
			canvas.FillRectangle(dirtyRect);

			view.DrawAction?.Invoke(canvas, dirtyRect);

			foreach (var line in view.Lines)
			{
				DrawLine(canvas, line, smooth: line.ShouldSmoothPathWhenDrawn);
			}

			if (owner.currentLine is not null)
			{
				DrawLine(canvas, owner.currentLine, smooth: false);
			}
		}

		static void DrawLine(ICanvas canvas, IDrawingLine line, bool smooth)
		{
			var points = smooth
				? line.Points.CreateSmoothedPathWithGranularity(line.Granularity)
				: line.Points;
			if (points.Count == 0)
			{
				return;
			}

			var path = new PathF();
			path.MoveTo(points[0]);
			foreach (var point in points)
			{
				path.LineTo(point);
			}

			canvas.StrokeColor = line.LineColor;
			canvas.StrokeSize = line.LineWidth;
			canvas.StrokeLineCap = LineCap.Round;
			canvas.StrokeLineJoin = LineJoin.Round;
			canvas.DrawPath(path);
		}
	}
}
