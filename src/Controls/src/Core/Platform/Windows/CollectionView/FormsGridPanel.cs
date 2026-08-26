#if UNO
#nullable disable
using System;
using Microsoft.UI.Xaml.Controls;
using WSize = Windows.Foundation.Size;

namespace Microsoft.Maui.Controls.Platform
{
	internal sealed class FormsGridPanel : StackPanel
	{
		readonly FormsGridView _owner;

		public FormsGridPanel(FormsGridView owner)
		{
			_owner = owner;
			Orientation = owner.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Horizontal
				? Microsoft.UI.Xaml.Controls.Orientation.Vertical
				: Microsoft.UI.Xaml.Controls.Orientation.Horizontal;
		}

		protected override WSize MeasureOverride(WSize availableSize)
		{
			var span = Math.Max(1, _owner.Span);
			return _owner.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Horizontal
				? MeasureHorizontal(availableSize, span)
				: MeasureVertical(availableSize, span);
		}

		protected override WSize ArrangeOverride(WSize finalSize)
		{
			var span = Math.Max(1, _owner.Span);
			return _owner.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Horizontal
				? ArrangeHorizontal(finalSize, span)
				: ArrangeVertical(finalSize, span);
		}

		WSize MeasureVertical(WSize availableSize, int span)
		{
			var availableWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
			var itemWidth = availableWidth > 0 ? availableWidth / span : double.PositiveInfinity;
			var desiredWidth = 0d;
			var desiredHeight = 0d;
			var rowHeight = 0d;

#pragma warning disable RS0030
			for (var index = 0; index < Children.Count; index++)
			{
				var child = Children[index];
				child.Measure(new WSize(itemWidth, double.PositiveInfinity));
				desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width * span);
				rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);

				if ((index + 1) % span == 0)
				{
					desiredHeight += rowHeight;
					rowHeight = 0;
				}
			}

			if (Children.Count % span != 0)
			{
				desiredHeight += rowHeight;
			}
#pragma warning restore RS0030

			return new WSize(availableWidth > 0 ? availableWidth : desiredWidth, desiredHeight);
		}

		WSize MeasureHorizontal(WSize availableSize, int span)
		{
			var availableHeight = double.IsFinite(availableSize.Height) ? availableSize.Height : 0;
			var itemHeight = availableHeight > 0 ? availableHeight / span : double.PositiveInfinity;
			var desiredWidth = 0d;
			var desiredHeight = 0d;
			var columnWidth = 0d;

#pragma warning disable RS0030
			for (var index = 0; index < Children.Count; index++)
			{
				var child = Children[index];
				child.Measure(new WSize(double.PositiveInfinity, itemHeight));
				columnWidth = Math.Max(columnWidth, child.DesiredSize.Width);
				desiredHeight = Math.Max(desiredHeight, child.DesiredSize.Height * span);

				if ((index + 1) % span == 0)
				{
					desiredWidth += columnWidth;
					columnWidth = 0;
				}
			}

			if (Children.Count % span != 0)
			{
				desiredWidth += columnWidth;
			}
#pragma warning restore RS0030

			return new WSize(desiredWidth, availableHeight > 0 ? availableHeight : desiredHeight);
		}

		WSize ArrangeVertical(WSize finalSize, int span)
		{
			var itemWidth = finalSize.Width / span;
			var y = 0d;

#pragma warning disable RS0030
			for (var rowStart = 0; rowStart < Children.Count; rowStart += span)
			{
				var rowEnd = Math.Min(rowStart + span, Children.Count);
				var rowHeight = 0d;
				for (var index = rowStart; index < rowEnd; index++)
				{
					rowHeight = Math.Max(rowHeight, Children[index].DesiredSize.Height);
				}

				for (var index = rowStart; index < rowEnd; index++)
				{
					Children[index].Arrange(new global::Windows.Foundation.Rect(
						(index - rowStart) * itemWidth,
						y,
						itemWidth,
						rowHeight));
				}

				y += rowHeight;
			}
#pragma warning restore RS0030

			return new WSize(finalSize.Width, y);
		}

		WSize ArrangeHorizontal(WSize finalSize, int span)
		{
			var itemHeight = finalSize.Height / span;
			var x = 0d;

#pragma warning disable RS0030
			for (var columnStart = 0; columnStart < Children.Count; columnStart += span)
			{
				var columnEnd = Math.Min(columnStart + span, Children.Count);
				var columnWidth = 0d;
				for (var index = columnStart; index < columnEnd; index++)
				{
					columnWidth = Math.Max(columnWidth, Children[index].DesiredSize.Width);
				}

				for (var index = columnStart; index < columnEnd; index++)
				{
					Children[index].Arrange(new global::Windows.Foundation.Rect(
						x,
						(index - columnStart) * itemHeight,
						columnWidth,
						itemHeight));
				}

				x += columnWidth;
			}
#pragma warning restore RS0030

			return new WSize(x, finalSize.Height);
		}
	}
}
#endif
