using System;

namespace Maui.Controls.Sample.Uno;

internal static class EmbeddingHostPlatformSupport
{
	internal static bool ShouldApplyVisibleBoundsPadding() =>
		ShouldApplyVisibleBoundsPadding(OperatingSystem.IsAndroid(), OperatingSystem.IsIOS());

	internal static bool ShouldApplyVisibleBoundsPadding(bool isAndroid, bool isIOS) =>
		isAndroid || isIOS;

	internal static bool SupportsSecondaryWindows() =>
		SupportsSecondaryWindows(OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS());

	internal static bool SupportsSecondaryWindows(bool isWindows, bool isLinux, bool isMacOS) =>
		isWindows || isLinux || isMacOS;
}
