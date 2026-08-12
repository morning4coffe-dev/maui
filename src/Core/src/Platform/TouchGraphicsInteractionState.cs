using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platform
{
	internal sealed class TouchGraphicsInteractionState
	{
		bool _isInBounds;
		bool _isTouching;

		public void PointerEntered(IGraphicsView? graphicsView, PointF[] points)
		{
			_isInBounds = true;
			graphicsView?.StartHoverInteraction(points);
		}

		public static void PointerExited(IGraphicsView? graphicsView)
		{
			graphicsView?.EndHoverInteraction();
		}

		public void PointerMoved(IGraphicsView? graphicsView, PointF[] points, bool isInBounds)
		{
			_isInBounds = isInBounds;

			graphicsView?.MoveHoverInteraction(points);

			if (_isTouching)
			{
				graphicsView?.DragInteraction(points);
			}
		}

		public void PointerPressed(IGraphicsView? graphicsView, PointF[] points)
		{
			if (graphicsView is null)
			{
				return;
			}

			_isInBounds = true;
			_isTouching = true;
			graphicsView.StartInteraction(points);
		}

		public void PointerReleased(IGraphicsView? graphicsView, PointF[] points, bool isInBounds)
		{
			if (!_isTouching)
			{
				return;
			}

			_isTouching = false;
			_isInBounds = isInBounds;
			graphicsView?.EndInteraction(points, _isInBounds);
		}

		public void PointerCanceled(IGraphicsView? graphicsView)
		{
			if (!_isTouching)
			{
				return;
			}

			_isTouching = false;
			graphicsView?.CancelInteraction();
		}

		public void PointerCaptureLost(IGraphicsView? graphicsView) => PointerCanceled(graphicsView);

		public void Reset()
		{
			_isInBounds = false;
			_isTouching = false;
		}
	}
}
