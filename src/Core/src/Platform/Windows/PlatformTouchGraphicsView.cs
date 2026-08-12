using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Graphics.Win2D;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Microsoft.Maui.Platform
{
	public partial class PlatformTouchGraphicsView : UserControl
	{
		IGraphicsView? _graphicsView;
		readonly W2DGraphicsView _platformGraphicsView;
		readonly TouchGraphicsInteractionState _interactionState = new();

		public PlatformTouchGraphicsView()
		{
			ManipulationMode = ManipulationModes.All;

			Content = _platformGraphicsView = new W2DGraphicsView();
		}

		public void UpdateDrawable(IGraphicsView graphicsView)
		{
			_platformGraphicsView.UpdateDrawable(graphicsView);
			if (!ReferenceEquals(_graphicsView, graphicsView))
			{
				_interactionState.Reset();
			}

			_graphicsView = graphicsView;
		}

		public void Invalidate() => _platformGraphicsView.Invalidate();

		PointF[] GetViewPoints(PointerRoutedEventArgs e)
		{
			var point = e.GetCurrentPoint(this).Position;
			return new[] { new PointF((float)point.X, (float)point.Y) };
		}

		bool IsInBounds(PointF[] points) => new RectF(0, 0, (float)ActualWidth, (float)ActualHeight).ContainsAny(points);

		protected override void OnPointerEntered(PointerRoutedEventArgs e)
		{
			_interactionState.PointerEntered(_graphicsView, GetViewPoints(e));
		}

		protected override void OnPointerCanceled(PointerRoutedEventArgs e)
		{
			_interactionState.PointerCanceled(_graphicsView);
			ReleasePointerCaptures();
		}

		void OnPointerCaptureLost(object? sender, PointerRoutedEventArgs e)
		{
			_interactionState.PointerCaptureLost(_graphicsView);
		}

		protected override void OnPointerExited(PointerRoutedEventArgs e)
		{
			_interactionState.PointerExited(_graphicsView);
		}

		protected override void OnPointerMoved(PointerRoutedEventArgs e)
		{
			var points = GetViewPoints(e);
			var isInBounds = IsInBounds(points);
			_interactionState.PointerMoved(_graphicsView, points, isInBounds);
		}

		protected override void OnPointerPressed(PointerRoutedEventArgs e)
		{
			if (_graphicsView is null)
			{
				return;
			}

			CapturePointer(e.Pointer);
			_interactionState.PointerPressed(_graphicsView, GetViewPoints(e));
		}

		protected override void OnPointerReleased(PointerRoutedEventArgs e)
		{
			var points = GetViewPoints(e);
			var isInBounds = IsInBounds(points);
			_interactionState.PointerReleased(_graphicsView, points, isInBounds);
			ReleasePointerCaptures();
		}

		public void Connect(IGraphicsView graphicsView)
		{
			PointerCaptureLost -= OnPointerCaptureLost;
			PointerCaptureLost += OnPointerCaptureLost;

			if (!ReferenceEquals(_graphicsView, graphicsView))
			{
				_interactionState.Reset();
			}

			_graphicsView = graphicsView;
		}

		public void Disconnect()
		{
			PointerCaptureLost -= OnPointerCaptureLost;
			_graphicsView = null;
			_interactionState.Reset();
		}
	}
}