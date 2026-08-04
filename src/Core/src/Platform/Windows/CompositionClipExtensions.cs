#if UNO
using System;
using System.Numerics;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Composition;

namespace Microsoft.Maui.Platform
{
	static class CompositionClipExtensions
	{
		internal static RectangleClip CreateMauiRectangleClip(
			this Compositor compositor,
			IShape shape,
			float width,
			float height,
			float cornerInset = 0)
		{
			width = Math.Max(0, width);
			height = Math.Max(0, height);

			if (shape is not IRoundRectangle roundRectangle)
			{
				return compositor.CreateRectangleClip(0, 0, width, height);
			}

			var maximumRadius = Math.Min(width, height) / 2;
			var cornerRadius = roundRectangle.CornerRadius;
			return compositor.CreateRectangleClip(
				0,
				0,
				width,
				height,
				CreateRadius(cornerRadius.TopLeft, cornerInset, maximumRadius),
				CreateRadius(cornerRadius.TopRight, cornerInset, maximumRadius),
				CreateRadius(cornerRadius.BottomRight, cornerInset, maximumRadius),
				CreateRadius(cornerRadius.BottomLeft, cornerInset, maximumRadius));
		}

		static Vector2 CreateRadius(double radius, float inset, float maximumRadius)
		{
			var adjustedRadius = (float)Math.Clamp(radius - inset, 0, maximumRadius);
			return new Vector2(adjustedRadius, adjustedRadius);
		}
	}
}
#endif
