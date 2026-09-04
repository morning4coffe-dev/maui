using System;
using System.Collections.Generic;
using System.Linq;
using Maui.Controls.Sample.Models;

namespace Maui.Controls.Sample.ViewModels.Base
{
	public abstract class BaseGalleryViewModel : BaseViewModel
	{
		string? _filterValue;

		public BaseGalleryViewModel()
		{
			var items = CreateItems();

			if (items != null)
				Items = items.ToList();

			Filter();
		}

		public IReadOnlyList<SectionModel>? Items { get; }

		public string? FilterValue
		{
			get { return _filterValue; }
			set
			{
				_filterValue = value;
				Filter();
			}
		}

		public IReadOnlyList<SectionModel> FilteredItems { get; private set; } = Array.Empty<SectionModel>();

		protected abstract IEnumerable<SectionModel> CreateItems();

		void Filter()
		{
			FilterValue ??= string.Empty;
			var filteredItems = string.IsNullOrEmpty(FilterValue)
				? Items!
				: Items!.Where(item => item.Title.IndexOf(FilterValue, StringComparison.InvariantCultureIgnoreCase) >= 0);

			FilteredItems = filteredItems.ToList();
			OnPropertyChanged(nameof(FilteredItems));
		}
	}
}