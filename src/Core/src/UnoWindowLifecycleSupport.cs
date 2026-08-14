namespace Microsoft.Maui
{
	internal static class UnoWindowLifecycleSupport
	{
		internal static bool ShouldPreserveWindowOnClose(bool isAndroid) => isAndroid;
	}
}
