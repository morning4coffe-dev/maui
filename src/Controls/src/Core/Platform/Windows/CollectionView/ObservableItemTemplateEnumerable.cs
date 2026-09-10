#nullable disable
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Microsoft.Maui.Controls.Platform
{
	internal sealed class ObservableItemTemplateEnumerable : ObservableCollection<ItemTemplateContext>, IDisposable
	{
		readonly IEnumerable _itemsSource;
		readonly DataTemplate _itemTemplate;
		readonly BindableObject _container;
		readonly IMauiContext _mauiContext;
		readonly double _itemHeight;
		readonly double _itemWidth;
		readonly Thickness _itemSpacing;
		readonly NotifyCollectionChangedEventHandler _collectionChanged;
		readonly WeakNotifyCollectionChangedProxy _proxy = new();
		bool _disposed;

		public ObservableItemTemplateEnumerable(
			IEnumerable itemsSource,
			INotifyCollectionChanged observable,
			DataTemplate itemTemplate,
			BindableObject container,
			double? itemHeight = null,
			double? itemWidth = null,
			Thickness? itemSpacing = null,
			IMauiContext mauiContext = null)
		{
			_itemsSource = itemsSource;
			_itemTemplate = itemTemplate;
			_container = container;
			_mauiContext = mauiContext;
			_itemHeight = itemHeight ?? 0;
			_itemWidth = itemWidth ?? 0;
			_itemSpacing = itemSpacing ?? new Thickness();
			_collectionChanged = OnSourceCollectionChanged;
			_proxy.Subscribe(observable, _collectionChanged);
			Reset();
		}

		~ObservableItemTemplateEnumerable() => _proxy.Unsubscribe();

		void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs args) =>
			_container.Dispatcher.DispatchIfRequired(Reset);

		void Reset()
		{
			Items.Clear();
			foreach (var item in _itemsSource)
			{
				Items.Add(new ItemTemplateContext(
					_itemTemplate,
					item,
					_container,
					_itemHeight,
					_itemWidth,
					_itemSpacing,
					_mauiContext));
			}
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			_proxy.Unsubscribe();
			GC.SuppressFinalize(this);
		}
	}
}
