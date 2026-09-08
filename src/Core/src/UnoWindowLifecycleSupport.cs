using System;

namespace Microsoft.Maui
{
	internal static class UnoWindowLifecycleSupport
	{
		internal static bool ShouldPreserveWindowOnClose(bool isAndroid) => isAndroid;

		internal static SafeAreaRegions ResolveRootSafeAreaRegion(bool isAndroid, bool hasExplicitEdges, SafeAreaRegions requested) =>
			isAndroid && !hasExplicitEdges ? SafeAreaRegions.Container : requested;

		internal static bool DispatchBackRequest(bool alreadyHandled, Func<bool> backButtonClicked)
		{
			if (backButtonClicked is null)
				throw new ArgumentNullException(nameof(backButtonClicked));

			return alreadyHandled || backButtonClicked();
		}
	}
}
