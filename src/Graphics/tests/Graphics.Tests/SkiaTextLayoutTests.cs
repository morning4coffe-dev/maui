using System.Collections.Generic;
using Microsoft.Maui.Graphics.Skia;
using SkiaSharp;
using Xunit;

namespace Microsoft.Maui.Graphics.Tests;

public class SkiaTextLayoutTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(float.NaN)]
	public void EmptyWidthDoesNotProduceLines(float width)
	{
		var lines = Layout("text", width);
		Assert.Empty(lines);
	}

	[Fact]
	public void NarrowWidthMakesProgressWithoutSplittingTextElements()
	{
		var lines = Layout("W\U0001F600e\u0301", 0.01f);
		Assert.Equal(new[] { "W", "\U0001F600", "e\u0301" }, lines);
	}

	[Fact]
	public void WordWrapDoesNotSplitForcedWhitespaceTextElement()
	{
		var lines = Layout("W\U0001F600e\u0301 \u0301X", 0.01f);
		Assert.Equal(new[] { "W", "\U0001F600", "e\u0301", " \u0301", "X" }, lines);
	}

	[Fact]
	public void MarginsCanExhaustAvailableWidth()
	{
		var lines = Layout("text", 10, margin: 5);
		Assert.Empty(lines);
	}

	[Theory]
	[InlineData(VerticalAlignment.Top)]
	[InlineData(VerticalAlignment.Center)]
	[InlineData(VerticalAlignment.Bottom)]
	public void OrdinaryTextRemainsOnOneLine(VerticalAlignment alignment)
	{
		var lines = Layout("ordinary text", 1000, alignment);
		Assert.Equal(new[] { "ordinary text" }, lines);
	}

	static List<string> Layout(string text, float width, VerticalAlignment alignment = VerticalAlignment.Top, float margin = 0)
	{
		var lines = new List<string>();
		using var font = new SKFont(SKTypeface.Default, 14);
		// Top-aligned ClipBounds also terminates when regressed zero-fit wrapping does not advance.
		using var layout = new SkiaTextLayout(
			text,
			new RectF(0, 0, width, 1000),
			new StandardTextAttributes { FontSize = 14, VerticalAlignment = alignment, Margin = margin },
			(_, _, line, _, _, _) => lines.Add(line),
			TextFlow.ClipBounds,
			font);
		layout.LayoutText();
		return lines;
	}
}
