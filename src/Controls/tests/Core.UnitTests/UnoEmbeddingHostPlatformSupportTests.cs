using Maui.Controls.Sample.Uno;
using Xunit;

namespace Microsoft.Maui.Controls.Core.UnitTests;

public class UnoEmbeddingHostPlatformSupportTests
{
	[Theory]
	[InlineData(true, false, true)]
	[InlineData(false, true, true)]
	[InlineData(false, false, false)]
	public void VisibleBoundsPaddingIsEnabledForMobileHosts(bool isAndroid, bool isIOS, bool expected)
	{
		Assert.Equal(expected, EmbeddingHostPlatformSupport.ShouldApplyVisibleBoundsPadding(isAndroid, isIOS));
	}

	[Theory]
	[InlineData(true, false, false, true)]
	[InlineData(false, true, false, true)]
	[InlineData(false, false, true, true)]
	[InlineData(false, false, false, false)]
	public void SecondaryWindowsAreLimitedToDesktopHosts(bool isWindows, bool isLinux, bool isMacOS, bool expected)
	{
		Assert.Equal(expected, EmbeddingHostPlatformSupport.SupportsSecondaryWindows(isWindows, isLinux, isMacOS));
	}
}
