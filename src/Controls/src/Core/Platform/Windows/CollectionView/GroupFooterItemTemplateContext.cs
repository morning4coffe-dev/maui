#nullable disable
using Microsoft.UI.Xaml;

namespace Microsoft.Maui.Controls.Platform
{
	internal class GroupFooterItemTemplateContext : ItemTemplateContext
	{
		public GroupFooterItemTemplateContext(DataTemplate formsDataTemplate, object item,
			BindableObject container, double? height = null, double? width = null, Thickness? itemSpacing = null, IMauiContext mauiContext = null)
			: base(formsDataTemplate, item, container, height, width, itemSpacing, mauiContext)
		{
		}

		public static void EnsureSelectionDisabled(DependencyObject element, object item)
		{
			if (element is FrameworkElement frameworkElement)
			{
#if UNO
				// Containers are recycled, so restore hit testing when a former footer is reused for an item.
				frameworkElement.IsHitTestVisible = item is not GroupFooterItemTemplateContext;
#else
				if (item is GroupFooterItemTemplateContext)
				{
					frameworkElement.IsHitTestVisible = false;
				}
#endif
			}
		}
	}
}