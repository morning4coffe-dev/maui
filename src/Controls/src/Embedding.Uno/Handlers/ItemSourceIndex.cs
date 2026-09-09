using Microsoft.UI.Xaml.Controls;

namespace Microsoft.Maui.Controls.Embedding.Uno;

internal static class ItemSourceIndex
{
	internal static int Find(ItemsSourceView? source, object? item)
	{
		if (source is null)
		{
			return -1;
		}
		for (var index = 0; index < source.Count; index++)
		{
			if (Equals(source.GetAt(index), item))
			{
				return index;
			}
		}
		return -1;
	}
}
