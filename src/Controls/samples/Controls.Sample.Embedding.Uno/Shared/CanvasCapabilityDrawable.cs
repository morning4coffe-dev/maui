using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Syncfusion.Maui.Toolkit.Charts;
using Syncfusion.Maui.Toolkit.Graphics.Internals;

namespace Maui.Controls.Sample.Uno;

sealed class CanvasCapabilityDrawable : IDrawable, ILineDrawing
{
	readonly ChartLabelStyle _text = new()
	{
		FontSize = 18,
		TextColor = Colors.Black,
	};

	public Color Stroke { get; set; } = Colors.Green;
	public double StrokeWidth { get; set; } = 4;
	public bool EnableAntiAliasing { get; set; } = true;
	public float Opacity { get; set; } = 1;
	public DoubleCollection? StrokeDashArray { get; set; }

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		canvas.FillColor = Colors.White;
		canvas.FillRectangle(dirtyRect);
		_text.TextColor = Colors.Black;
		Check("point-text", () => canvas.DrawText("Point text", 20, 10, _text));
		_text.TextColor = Colors.Blue;
		Check("rectangle-text", () => canvas.DrawText("Aligned text", new Rect(240, 10, 220, 55),
			HorizontalAlignment.Center, VerticalAlignment.Center, _text));
		Check("polyline", () => canvas.DrawLines(new float[] { 20, 85, 400, 85, 400, 110 }, this));
	}

	static void Check(string operation, Action draw)
	{
		try
		{
			draw();
			Console.WriteLine($"CANVAS-CAPABILITY EXECUTED {operation}; pixels still required");
		}
		catch (NotImplementedException error)
		{
			Console.WriteLine($"CANVAS-CAPABILITY FAIL {operation}: {error.GetType().Name}");
		}
	}
}
