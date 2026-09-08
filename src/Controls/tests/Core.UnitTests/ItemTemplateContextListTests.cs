using System;
using System.Collections;
using Microsoft.Maui.Controls.Platform;
using Xunit;

namespace Microsoft.Maui.Controls.Core.UnitTests;

public class ItemTemplateContextListTests
{
	[Fact]
	public void NonGenericAccessDoesNotEnumerateOrMaterializeOtherContexts()
	{
		var source = new CountingList();
		for (var i = 0; i < 100_000; i++)
			source.Add(i);
		var contexts = new ItemTemplateContextList(source, new DataTemplate(() => new Label()), new CollectionView());
		var list = Assert.IsAssignableFrom<IList>(contexts);

		Assert.Equal(100_000, list.Count);
		Assert.Equal(0, source.Reads);
		var last = Assert.IsType<ItemTemplateContext>(list[99_999]);
		Assert.Equal(99_999, last.Item);
		Assert.Equal(1, source.Reads);
		Assert.Same(last, list[99_999]);
		Assert.Equal(99_999, list.IndexOf(last));
		Assert.True(list.Contains(last));
		Assert.Equal(-1, list.IndexOf(new object()));
		Assert.Equal(1, source.Reads);
	}

	[Fact]
	public void NonGenericListIsReadOnlyAndCopiesStableContexts()
	{
		var contexts = new ItemTemplateContextList(new[] { 4, 4 }, new DataTemplate(() => new Label()), new CollectionView());
		var list = Assert.IsAssignableFrom<IList>(contexts);
		var copy = new object[3];
		list.CopyTo(copy, 1);

		Assert.Null(copy[0]);
		Assert.Same(list[0], copy[1]);
		Assert.Same(list[1], copy[2]);
		Assert.Equal(1, list.IndexOf(copy[2]));
		Assert.True(list.IsReadOnly);
		Assert.True(list.IsFixedSize);
		Assert.Throws<NotSupportedException>(() => list[0] = copy[2]);
		Assert.Throws<NotSupportedException>(() => list.Add(copy[1]));
		Assert.Throws<NotSupportedException>(() => list.Insert(0, copy[1]));
		Assert.Throws<NotSupportedException>(() => list.Remove(copy[1]));
		Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
		Assert.Throws<NotSupportedException>(() => list.Clear());
	}

	sealed class CountingList : ArrayList
	{
		public int Reads { get; private set; }

		public override object this[int index]
		{
			get
			{
				Reads++;
				return base[index];
			}
			set => base[index] = value;
		}
	}
}
