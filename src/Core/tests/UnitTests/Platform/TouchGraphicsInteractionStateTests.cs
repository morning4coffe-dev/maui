using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using NSubstitute;
using Xunit;

namespace Microsoft.Maui.UnitTests.Platform
{
	[Category(TestCategory.Core)]
	public class TouchGraphicsInteractionStateTests
	{
		[Fact]
		public void PointerMovedOutsideBoundsEndsAnActiveInteractionOutsideBounds()
		{
			var graphicsView = CreateGraphicsView();
			var interactionState = new TouchGraphicsInteractionState();
			var insidePoints = new[] { new PointF(10, 10) };
			var outsidePoints = new[] { new PointF(120, 120) };

			interactionState.PointerEntered(graphicsView, insidePoints);
			interactionState.PointerPressed(graphicsView, insidePoints);
			interactionState.PointerMoved(graphicsView, outsidePoints, false);

			graphicsView.DidNotReceive().EndHoverInteraction();
			graphicsView.DidNotReceive().EndInteraction(Arg.Any<PointF[]>(), Arg.Any<bool>());
			graphicsView.DidNotReceive().CancelInteraction();

			interactionState.PointerReleased(graphicsView, outsidePoints, false);

			graphicsView.Received(1).EndInteraction(Arg.Any<PointF[]>(), false);
			graphicsView.DidNotReceive().CancelInteraction();
		}

		[Fact]
		public void PointerCanceledOnlyCancelsTheActiveInteraction()
		{
			var graphicsView = CreateGraphicsView();
			var interactionState = new TouchGraphicsInteractionState();
			var points = new[] { new PointF(10, 10) };

			interactionState.PointerPressed(graphicsView, points);
			interactionState.PointerCanceled(graphicsView);
			interactionState.PointerReleased(graphicsView, points, true);

			graphicsView.Received(1).CancelInteraction();
			graphicsView.DidNotReceive().EndInteraction(Arg.Any<PointF[]>(), Arg.Any<bool>());
		}

		[Fact]
		public void PointerCaptureLostOnlyCancelsTheActiveInteraction()
		{
			var graphicsView = CreateGraphicsView();
			var interactionState = new TouchGraphicsInteractionState();
			var points = new[] { new PointF(10, 10) };

			interactionState.PointerPressed(graphicsView, points);
			interactionState.PointerCaptureLost(graphicsView);
			interactionState.PointerReleased(graphicsView, points, true);

			graphicsView.Received(1).CancelInteraction();
			graphicsView.DidNotReceive().EndInteraction(Arg.Any<PointF[]>(), Arg.Any<bool>());
		}

		[Fact]
		public void PointerReleasedEndsInsideBoundsWhenPressedInsideBounds()
		{
			var graphicsView = CreateGraphicsView();
			var interactionState = new TouchGraphicsInteractionState();
			var points = new[] { new PointF(10, 10) };

			interactionState.PointerPressed(graphicsView, points);
			interactionState.PointerReleased(graphicsView, points, true);

			graphicsView.Received(1).EndInteraction(Arg.Any<PointF[]>(), true);
		}

		[Fact]
		public void PointerMovedBackInsideBeforeReleaseEndsInsideBounds()
		{
			var graphicsView = CreateGraphicsView();
			var interactionState = new TouchGraphicsInteractionState();
			var points = new[] { new PointF(10, 10) };

			interactionState.PointerPressed(graphicsView, points);
			TouchGraphicsInteractionState.PointerExited(graphicsView);
			interactionState.PointerMoved(graphicsView, points, true);
			interactionState.PointerReleased(graphicsView, points, true);

			graphicsView.Received(1).EndInteraction(Arg.Any<PointF[]>(), true);
		}

		[Fact]
		public void PointerCaptureLostAfterReleaseDoesNotCancelACompletedInteraction()
		{
			var graphicsView = CreateGraphicsView();
			var interactionState = new TouchGraphicsInteractionState();
			var points = new[] { new PointF(10, 10) };

			interactionState.PointerPressed(graphicsView, points);
			interactionState.PointerReleased(graphicsView, points, true);
			interactionState.PointerCaptureLost(graphicsView);

			graphicsView.Received(1).EndInteraction(Arg.Any<PointF[]>(), true);
			graphicsView.DidNotReceive().CancelInteraction();
		}

		static IGraphicsView CreateGraphicsView()
		{
			var graphicsView = Substitute.For<IGraphicsView>();
			graphicsView.Drawable.Returns(Substitute.For<IDrawable>());
			return graphicsView;
		}
	}
}
