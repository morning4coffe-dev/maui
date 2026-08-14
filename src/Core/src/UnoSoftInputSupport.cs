namespace Microsoft.Maui
{
	internal static class UnoSoftInputSupport
	{
		internal static bool IsShowing(bool isVisible, bool isAndroid, double occludedHeight) =>
			isVisible || (isAndroid && occludedHeight > 0);
	}
}
