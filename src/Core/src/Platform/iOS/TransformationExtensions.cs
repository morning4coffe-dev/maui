using System;
using CoreAnimation;
using CoreGraphics;
using Microsoft.Maui.Graphics;
using ObjCRuntime;
using UIKit;

namespace Microsoft.Maui.Platform
{
	public static class TransformationExtensions
	{
		public static void UpdateTransformation(this UIView platformView, IView? view)
		{
			CALayer? layer = platformView.Layer;
			CGPoint? originalAnchor = layer?.AnchorPoint;

			platformView.UpdateTransformation(view, layer, originalAnchor);
		}

		public static void UpdateTransformation(this UIView platformView, IView? view, CALayer? layer, CGPoint? originalAnchor)
		{
			if (view == null)
				return;

			var anchorX = (float)view.AnchorX;
			var anchorY = (float)view.AnchorY;
			var translationX = (float)view.TranslationX;
			var translationY = (float)view.TranslationY;
			var rotationX = (float)view.RotationX;
			var rotationY = (float)view.RotationY;
			var rotation = (float)view.Rotation;
			var scale = (float)view.Scale;
			var scaleX = (float)view.ScaleX * scale;
			var scaleY = (float)view.ScaleY * scale;
			var width = (float)view.Frame.Width;
			var height = (float)view.Frame.Height;
			var x = (float)view.Frame.X;
			var y = (float)view.Frame.Y;

			if (!TryCreateTransformation(
				view,
				anchorX,
				anchorY,
				translationX,
				translationY,
				rotationX,
				rotationY,
				rotation,
				scale,
				scaleX,
				scaleY,
				width,
				height,
				out var transform))
			{
				return;
			}

			var anchorPoint = new PointF(anchorX, anchorY);

			if (Foundation.NSThread.IsMain)
			{
				ApplyTransformation(layer, anchorPoint, transform);
				return;
			}

			DispatchTransformation(layer, anchorPoint, transform);
		}

		static void DispatchTransformation(
			CALayer? layer,
			PointF anchorPoint,
			CATransform3D transform)
		{
			CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
				ApplyTransformation(layer, anchorPoint, transform));
		}

		static bool TryCreateTransformation(
			IView view,
			float anchorX,
			float anchorY,
			float translationX,
			float translationY,
			float rotationX,
			float rotationY,
			float rotation,
			float scale,
			float scaleX,
			float scaleY,
			float width,
			float height,
			out CATransform3D transform)
		{
			var shouldUpdate =
				width > 0 &&
				height > 0 &&
				view.Parent != null;

			if (!shouldUpdate)
			{
				transform = CATransform3D.Identity;
				return false;
			}

			const double epsilon = 0.001;

			transform = CATransform3D.Identity;

			// Position is relative to anchor point
			if (Math.Abs(anchorX - .5) > epsilon)
				transform = transform.Translate((anchorX - .5f) * width, 0, 0);

			if (Math.Abs(anchorY - .5) > epsilon)
				transform = transform.Translate(0, (anchorY - .5f) * height, 0);

			if (Math.Abs(translationX) > epsilon || Math.Abs(translationY) > epsilon)
				transform = transform.Translate(translationX, translationY, 0);

			// Not just an optimization, iOS will not "pixel align" a view which has M34 set
			if (Math.Abs(rotationY % 180) > epsilon || Math.Abs(rotationX % 180) > epsilon)
				transform.M34 = 1.0f / -400f;

			if (Math.Abs(rotationX % 360) > epsilon)
				transform = transform.Rotate(rotationX * MathF.PI / 180.0f, 1.0f, 0.0f, 0.0f);

			if (Math.Abs(rotationY % 360) > epsilon)
				transform = transform.Rotate(rotationY * MathF.PI / 180.0f, 0.0f, 1.0f, 0.0f);

			transform = transform.Rotate(rotation * MathF.PI / 180.0f, 0.0f, 0.0f, 1.0f);

			if (Math.Abs(scaleX - 1) > epsilon || Math.Abs(scaleY - 1) > epsilon)
				transform = transform.Scale(scaleX, scaleY, scale);

			return true;
		}

		static void ApplyTransformation(
			CALayer? layer,
			PointF anchorPoint,
			CATransform3D transform)
		{
			if (layer is null)
				return;

			layer.AnchorPoint = anchorPoint;
			layer.Transform = transform;
		}
	}

}