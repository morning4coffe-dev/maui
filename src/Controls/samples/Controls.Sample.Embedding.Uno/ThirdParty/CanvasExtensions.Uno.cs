using Windows.UI.ViewManagement;
using GraphicsFont = Microsoft.Maui.Graphics.Font;

namespace Syncfusion.Maui.Toolkit.Graphics.Internals;

public static partial class CanvasExtensions
{
	public static void DrawText(this ICanvas canvas, string value, float x, float y, ITextElement textElement)
	{
		canvas.SaveState();
		try
		{
			var textStyle = SetTextStyle(canvas, textElement);
			var size = canvas.GetStringSize(value, textStyle.Font, textStyle.Size);
			canvas.DrawString(value, x, y, size.Width, size.Height,
				HorizontalAlignment.Left, VerticalAlignment.Top, TextFlow.OverflowBounds);
		}
		finally
		{
			canvas.RestoreState();
		}
	}

	public static void DrawText(this ICanvas canvas, string value, Rect rect,
		HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment, ITextElement textElement)
	{
		canvas.SaveState();
		try
		{
			SetTextStyle(canvas, textElement);
			canvas.DrawString(value, (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height,
				horizontalAlignment, verticalAlignment, TextFlow.ClipBounds);
		}
		finally
		{
			canvas.RestoreState();
		}
	}

	public static void DrawLines(this ICanvas canvas, float[] points, ILineDrawing lineDrawing)
	{
		ArgumentNullException.ThrowIfNull(points);
		if (points.Length % 2 != 0)
		{
			throw new ArgumentException("Polyline coordinates must contain complete X/Y pairs.", nameof(points));
		}

		canvas.SaveState();
		try
		{
			canvas.StrokeSize = (float)lineDrawing.StrokeWidth;
			canvas.StrokeColor = lineDrawing.Stroke;
			canvas.Antialias = lineDrawing.EnableAntiAliasing;
			canvas.Alpha = lineDrawing.Opacity;
			canvas.StrokeDashPattern = lineDrawing.StrokeDashArray?.ToFloatArray();

			if (points.Length >= 4)
			{
				var path = new PathF();
				path.MoveTo(points[0], points[1]);
				for (var index = 2; index < points.Length; index += 2)
				{
					path.LineTo(points[index], points[index + 1]);
				}
				canvas.DrawPath(path);
			}
		}
		finally
		{
			canvas.RestoreState();
		}
	}

	static (GraphicsFont Font, float Size) SetTextStyle(ICanvas canvas, ITextElement textElement)
	{
		var font = textElement.Font;
		var style = font.Slant switch
		{
			FontSlant.Italic => FontStyleType.Italic,
			FontSlant.Oblique => FontStyleType.Oblique,
			_ => FontStyleType.Normal,
		};
		var graphicsFont = new GraphicsFont(font.Family, (int)font.Weight, style);
		canvas.Font = graphicsFont;
		var fontManager = textElement.FontManager
			?? throw new InvalidOperationException("Text drawing requires the registered MAUI font manager.");
		var size = fontManager.GetFontSize(font.WithSize(textElement.FontSize), textElement.FontSizeDefaultValueCreator());
		if (textElement.FontAutoScalingEnabled)
		{
			size *= new UISettings().TextScaleFactor;
		}
		canvas.FontSize = (float)size;
		canvas.FontColor = textElement.TextColor;
		return (graphicsFont, (float)size);
	}
}
