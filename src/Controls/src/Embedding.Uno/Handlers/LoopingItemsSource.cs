using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Microsoft.Maui.Controls.Embedding.Uno;

/// <summary>
/// Repeats sources with multiple items so a carousel can scroll past either end.
/// Empty and singleton sources retain change notifications without adding repeated items.
/// </summary>
/// <remarks>
/// <para>
/// A repeater has a finite scroll extent, so wrap-around cannot be produced by scrolling alone. The source
/// is instead repeated <see cref="Blocks"/> times and the carousel is kept in the middle block; whenever it
/// settles outside that block the handler jumps it back by a whole block without animation, which is
/// invisible because the item at that offset is the same item.
/// </para>
/// <para>
/// Repeating rather than cloning matters: index <c>i</c> maps to <c>inner[i % Count]</c>, so every repeat of
/// an item is the same object, and selection and binding identity are preserved.
/// </para>
/// </remarks>
sealed class LoopingItemsSource : IList, INotifyCollectionChanged, IDisposable
{
	/// <summary>How many times the source is repeated. Odd, so that there is a single middle block.</summary>
	public const int Blocks = 3;

	readonly IList _inner;
	readonly IEnumerable _source;
	readonly INotifyCollectionChanged? _innerNotifier;

	LoopingItemsSource(IEnumerable source)
	{
		_source = source;
		_inner = source as IList ?? Materialize(source);

		if (source is INotifyCollectionChanged notifier)
		{
			_innerNotifier = notifier;
			_innerNotifier.CollectionChanged += OnInnerCollectionChanged;
		}
	}

	public event NotifyCollectionChangedEventHandler? CollectionChanged;

	/// <summary>Gets the number of items in the underlying source.</summary>
	public int InnerCount => _inner.Count;

	public int Count => _inner.Count > 1 ? checked(_inner.Count * Blocks) : _inner.Count;

	public bool IsFixedSize => false;

	public bool IsReadOnly => true;

	public bool IsSynchronized => false;

	public object SyncRoot => this;

	public object? this[int index]
	{
		get
		{
			if (index < 0 || index >= Count)
				throw new ArgumentOutOfRangeException(nameof(index));
			return _inner[index % _inner.Count];
		}
		set => throw new NotSupportedException();
	}

	/// <summary>
	/// Wraps a source, retaining change notifications while it is empty or has a single item.
	/// </summary>
	public static LoopingItemsSource? TryCreate(IEnumerable? source)
	{
		if (source is null)
		{
			return null;
		}

		return new LoopingItemsSource(source);
	}

	static IList Materialize(IEnumerable source)
	{
		var list = new List<object?>();

		foreach (var item in source)
		{
			list.Add(item);
		}

		return list;
	}

	/// <summary>Maps a repeated index onto the underlying source.</summary>
	public int ToInnerIndex(int index) =>
		_inner.Count == 0 ? 0 : ((index % _inner.Count) + _inner.Count) % _inner.Count;

	/// <summary>Maps a source index into the middle block.</summary>
	public int ToMiddleBlockIndex(int innerIndex) => _inner.Count > 1 ? _inner.Count + innerIndex : innerIndex;

	/// <summary>Gets whether <paramref name="index"/> has drifted out of the middle block.</summary>
	public bool IsOutsideMiddleBlock(int index) => _inner.Count > 1 && (index < _inner.Count || index >= _inner.Count * 2);

	public int IndexOf(object? value)
	{
		var innerIndex = _inner.IndexOf(value);

		return innerIndex < 0 ? -1 : ToMiddleBlockIndex(innerIndex);
	}

	public bool Contains(object? value) => _inner.Contains(value);

	public IEnumerator GetEnumerator()
	{
		for (var i = 0; i < Count; i++)
		{
			yield return this[i]!;
		}
	}

	public void CopyTo(Array array, int index)
	{
		for (var i = 0; i < Count; i++)
		{
			array.SetValue(this[i], index + i);
		}
	}

	public int Add(object? value) => throw new NotSupportedException();

	public void Clear() => throw new NotSupportedException();

	public void Insert(int index, object? value) => throw new NotSupportedException();

	public void Remove(object? value) => throw new NotSupportedException();

	public void RemoveAt(int index) => throw new NotSupportedException();

	public void Dispose()
	{
		if (_innerNotifier is not null)
		{
			_innerNotifier.CollectionChanged -= OnInnerCollectionChanged;
		}
	}

	// Index arithmetic across three blocks would have to be recomputed per action, and the repeater handles
	// a reset correctly, so any underlying change is reported as one.
	void OnInnerCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
	{
		if (_source is not IList)
		{
			_inner.Clear();
			foreach (var item in _source)
				_inner.Add(item);
		}
		CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}
}
