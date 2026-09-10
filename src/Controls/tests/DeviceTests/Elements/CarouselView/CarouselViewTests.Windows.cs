using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Platform;
using FrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using Xunit;
using static Microsoft.Maui.DeviceTests.AssertHelpers;

namespace Microsoft.Maui.DeviceTests
{
	public partial class CarouselViewTests
	{
		[Fact]
		public async Task VisibleViewsTracksLayoutScrollSourceAndTeardown()
		{
			SetupBuilder();

			var items = new ObservableCollection<string> { "zero", "one", "two", "three" };
			var carousel = new CarouselView
			{
				WidthRequest = 320,
				HeightRequest = 180,
				Loop = false,
				PeekAreaInsets = new Thickness(40, 0),
				ItemsSource = items,
				ItemTemplate = new DataTemplate(() =>
				{
					var label = new Label();
					label.SetBinding(Label.TextProperty, ".");
					return label;
				})
			};

			await CreateHandlerAndAddToWindow<CarouselViewHandler>(carousel, async handler =>
			{
				await AssertEventually(() => carousel.VisibleViews.Count >= 2 && VisibleItemsMatchViewport());
				Assert.Contains(items[0], carousel.VisibleViews.Select(view => view.BindingContext));
				Assert.Contains(items[1], carousel.VisibleViews.Select(view => view.BindingContext));

				carousel.Position = 1;
				await AssertEventually(() =>
					carousel.VisibleViews.Any(view => Equals(view.BindingContext, items[1])) &&
					carousel.VisibleViews.Any(view => Equals(view.BindingContext, items[2])) &&
					VisibleItemsMatchViewport());

				var previousWidth = ((FrameworkElement)handler.PlatformView.ContainerFromIndex(1)).ActualWidth;
				carousel.WidthRequest = 420;
				await AssertEventually(() => handler.PlatformView.ActualWidth >= 400 &&
					handler.PlatformView.ContainerFromIndex(1) is FrameworkElement { ActualWidth: var width } &&
					System.Math.Abs(width - previousWidth) > 1 &&
					VisibleItemsMatchViewport());

				items.Clear();
				await AssertEventually(() => carousel.VisibleViews.Count == 0);

				carousel.Handler.DisconnectHandler();
				items.Add("after teardown");
				await Task.Delay(100);
				Assert.Empty(carousel.VisibleViews);

				bool VisibleItemsMatchViewport()
				{
					var list = handler.PlatformView;
					var scroll = list.GetFirstDescendant<UI.Xaml.Controls.ScrollViewer>();
					if (scroll is null)
						return false;
					var expected = Enumerable.Range(0, items.Count).Where(index =>
					{
						if (list.ContainerFromIndex(index) is not FrameworkElement container)
							return false;
						var bounds = container.TransformToVisual(scroll).TransformBounds(
							new global::Windows.Foundation.Rect(0, 0, container.ActualWidth, container.ActualHeight));
						return bounds.Right > 0 && bounds.Left < scroll.ActualWidth;
					}).Select(index => (object)items[index]);
					return carousel.VisibleViews.Count > 0 &&
						carousel.VisibleViews.Select(view => view.BindingContext).SequenceEqual(expected);
				}
			});
		}

	}
}