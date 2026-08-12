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
	CurrentDrawing? currentDrawing;

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
			CancelCurrentDrawing(connectedView);
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
		var interactionView = new GraphicsView
		{
			Drawable = drawable,
		};
		this.interactionView = interactionView;
		interactionView.StartInteraction += OnStartInteraction;
		interactionView.DragInteraction += OnDragInteraction;
		interactionView.EndInteraction += OnEndInteraction;
		interactionView.CancelInteraction += OnCancelInteraction;

		platformView.UpdateDrawable(interactionView);
		platformView.Connect(interactionView);
		SubscribeToLines(connectedView.Lines);
	}

	protected override void DisconnectHandler(PlatformTouchGraphicsView platformView)
	{
		CancelCurrentDrawing(connectedView);
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

	bool CancelCurrentDrawing(IDrawingView? view)
	{
		if (currentDrawing is null)
		{
			return false;
		}

		var drawing = currentDrawing;
		currentDrawing = null;
		drawing.Dispose();
		view?.OnDrawingLineCancelled();
		return true;
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

		CancelCurrentDrawing(virtualView);

		if (!virtualView.IsMultiLineModeEnabled)
		{
			virtualView.Lines.Clear();
		}

		var point = e.Touches[0];
		currentDrawing = CurrentDrawing.Create(virtualView.LineColor, virtualView.LineWidth, point);

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
		var drawing = currentDrawing;
		if (virtualView is null ||
			platformView is null ||
			drawing is null ||
			e.Touches is not { Length: > 0 })
		{
			return;
		}

		foreach (var point in e.Touches)
		{
			drawing.AppendPoint(point);
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
		var drawing = currentDrawing;
		if (virtualView is null || platformView is null || drawing is null)
		{
			return;
		}

		currentDrawing = null;

		try
		{
			CompleteLine(virtualView, platformView, drawing.Line);
		}
		finally
		{
			drawing.Dispose();
		}
	}

	void OnCancelInteraction(object? sender, EventArgs e)
	{
		var virtualView = connectedView;
		var platformView = connectedPlatformView;
		if (virtualView is null || platformView is null || !CancelCurrentDrawing(virtualView))
		{
			return;
		}

		if (ReferenceEquals(connectedPlatformView, platformView))
		{
			platformView.Invalidate();
		}
	}

	void CompleteLine(IDrawingView virtualView, PlatformTouchGraphicsView platformView, DrawingLine drawingLine)
	{
		var shouldClearOnFinish = virtualView.ShouldClearOnFinish;
		virtualView.Lines.Add(drawingLine);
		virtualView.OnDrawingLineCompleted(drawingLine);

		if (shouldClearOnFinish)
		{
			virtualView.Lines.Clear();
		}

		if (ReferenceEquals(connectedPlatformView, platformView))
		{
			platformView.Invalidate();
		}
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

			if (owner.currentDrawing is not null)
			{
				DrawPath(
					canvas,
					owner.currentDrawing.Path,
					owner.currentDrawing.Line.LineColor,
					owner.currentDrawing.Line.LineWidth);
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

		static void DrawPath(ICanvas canvas, PathF path, Color lineColor, float lineWidth)
		{
			canvas.StrokeColor = lineColor;
			canvas.StrokeSize = lineWidth;
			canvas.StrokeLineCap = LineCap.Round;
			canvas.StrokeLineJoin = LineJoin.Round;
			canvas.DrawPath(path);
		}
	}

	sealed class CurrentDrawing(DrawingLine line, PathF path) : IDisposable
	{
		public DrawingLine Line { get; } = line;

		public PathF Path { get; } = path;

		public static CurrentDrawing Create(Color lineColor, float lineWidth, PointF startPoint)
		{
			var path = new PathF();
			path.MoveTo(startPoint);

			return new CurrentDrawing(
				new DrawingLine
				{
					LineColor = lineColor,
					LineWidth = lineWidth,
					Points = [startPoint],
				},
				path);
		}

		public void AppendPoint(PointF point)
		{
			Path.LineTo(point);
			Line.Points.Add(point);
		}

		public void Dispose() => Path.Dispose();
	}
}
