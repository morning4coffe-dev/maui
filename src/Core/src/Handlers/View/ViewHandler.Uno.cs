#if UNO
using System;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui.Handlers
{
	public abstract partial class ViewHandler
	{
		internal static void MapSafeAreaEdges(IViewHandler handler, IView view)
		{
			if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst())
			{
				return;
			}

			if (handler.MauiContext?.Services.GetService(typeof(NavigationRootManager)) is NavigationRootManager manager)
			{
				manager.UpdateSafeArea(view);
			}
		}
	}
}
#endif
