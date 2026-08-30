using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Microsoft.Maui.Controls.Embedding.Uno;

/// <summary>
/// Presents a source repeated a fixed number of times, so that a carousel can be scrolled past either end
/// and keep going.
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
	readonly INotifyCollectionChanged? _innerNotifier;

	LoopingItemsSource(IList inner)
	{
		_inner = inner;

		if (inner is INotifyCollectionChanged notifier)
		{
			_innerNotifier = notifier;
			_innerNotifier.CollectionChanged += OnInnerCollectionChanged;
		}
	}

	public event NotifyCollectionChangedEventHandler? CollectionChanged;

	/// <summary>Gets the number of items in the underlying source.</summary>
	public int InnerCount => _inner.Count;

	public int Count => _inner.Count * Blocks;

	public bool IsFixedSize => false;

	public bool IsReadOnly => true;

	public bool IsSynchronized => false;

	public object SyncRoot => this;

	public object? this[int index]
	{
		get => _inner.Count == 0 ? null : _inner[((index % _inner.Count) + _inner.Count) % _inner.Count];
		set => throw new NotSupportedException();
	}

	/// <summary>
	/// Wraps <paramref name="source"/>, or returns <see langword="null"/> when repeating would be pointless.
	/// </summary>
	public static LoopingItemsSource? TryCreate(IEnumerable? source)
	{
		if (source is null)
		{
			return null;
		}

		var list = source as IList ?? Materialize(source);

		// A single item has nowhere to wrap to, and an empty source has nothing to show.
		return list.Count > 1 ? new LoopingItemsSource(list) : null;
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
	public int ToMiddleBlockIndex(int innerIndex) => _inner.Count + innerIndex;

	/// <summary>Gets whether <paramref name="index"/> has drifted out of the middle block.</summary>
	public bool IsOutsideMiddleBlock(int index) => index < _inner.Count || index >= _inner.Count * 2;

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
	void OnInnerCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
		CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
}
