#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Maui.Controls.Platform
{
	// CollectionViewSource also uses the non-generic interfaces for counted, indexed access.
	internal class ItemTemplateContextList : IReadOnlyList<ItemTemplateContext>, IList
	{
		readonly IList _itemsSource;
		readonly DataTemplate _itemTemplate;
		readonly BindableObject _container;
		readonly IMauiContext _mauiContext;
		readonly double _itemHeight;
		readonly double _itemWidth;
		readonly Thickness _itemSpacing;

		readonly Dictionary<int, ItemTemplateContext> _itemTemplateContexts;

		public int Count => _itemsSource.Count;

		public ItemTemplateContext this[int index]
		{
			get
			{
				if (!_itemTemplateContexts.TryGetValue(index, out var context))
				{
					_itemTemplateContexts[index] = context = new ItemTemplateContext(_itemTemplate, _itemsSource[index],
						_container, _itemHeight, _itemWidth, _itemSpacing, _mauiContext);
				}

				return context;
			}
		}

		public ItemTemplateContextList(IList itemsSource, DataTemplate itemTemplate, BindableObject container,
			double? itemHeight = null, double? itemWidth = null, Thickness? itemSpacing = null, IMauiContext mauiContext = null)
		{
			_itemsSource = itemsSource;
			_itemTemplate = itemTemplate;
			_container = container;
			_mauiContext = mauiContext;
			if (itemHeight.HasValue)
				_itemHeight = itemHeight.Value;

			if (itemWidth.HasValue)
				_itemWidth = itemWidth.Value;

			if (itemSpacing.HasValue)
				_itemSpacing = itemSpacing.Value;

			_itemTemplateContexts = new(capacity: Math.Min(64, _itemsSource.Count));
		}

		public IEnumerator<ItemTemplateContext> GetEnumerator()
		{
			return new ItemTemplateContextListEnumerator(this);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		object IList.this[int index]
		{
			get => this[index];
			set => throw new NotSupportedException();
		}

		bool IList.IsReadOnly => true;
		bool IList.IsFixedSize => true;
		bool ICollection.IsSynchronized => false;
		object ICollection.SyncRoot => ((ICollection)_itemsSource).SyncRoot;

		int IList.IndexOf(object value)
		{
			foreach (var context in _itemTemplateContexts)
			{
				if (ReferenceEquals(context.Value, value))
					return context.Key;
			}

			return -1;
		}

		bool IList.Contains(object value) => ((IList)this).IndexOf(value) >= 0;

		void ICollection.CopyTo(Array array, int index)
		{
			ArgumentNullException.ThrowIfNull(array);
			if (array.Rank != 1 || array.GetLowerBound(0) != 0)
				throw new ArgumentException("The destination must be a zero-based, one-dimensional array.", nameof(array));
			if (index < 0)
				throw new ArgumentOutOfRangeException(nameof(index));
			if (index > array.Length || Count > array.Length - index)
				throw new ArgumentException("The destination array has insufficient space.", nameof(array));

			for (var i = 0; i < Count; i++)
				array.SetValue(this[i], index + i);
		}

		int IList.Add(object value) => throw new NotSupportedException();
		void IList.Clear() => throw new NotSupportedException();
		void IList.Insert(int index, object value) => throw new NotSupportedException();
		void IList.Remove(object value) => throw new NotSupportedException();
		void IList.RemoveAt(int index) => throw new NotSupportedException();

		internal class ItemTemplateContextListEnumerator : IEnumerator<ItemTemplateContext>
		{
			public ItemTemplateContext Current { get; private set; }
			object IEnumerator.Current => Current;
			int _currentIndex = -1;
			private ItemTemplateContextList _itemTemplateContextList;

			public ItemTemplateContextListEnumerator(ItemTemplateContextList observableItemTemplateCollection) =>
				_itemTemplateContextList = observableItemTemplateCollection;

			public void Dispose()
			{
			}

			public bool MoveNext()
			{
				if (_currentIndex >= _itemTemplateContextList.Count - 1)
				{
					return false;
				}

				_currentIndex += 1;
				Current = _itemTemplateContextList[_currentIndex];

				return true;
			}

			public void Reset()
			{
				Current = null;
				_currentIndex = -1;
			}
		}
	}
}