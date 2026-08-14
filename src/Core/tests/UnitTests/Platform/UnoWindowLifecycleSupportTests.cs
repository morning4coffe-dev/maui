using Microsoft.Maui;
using Xunit;

namespace Microsoft.Maui.UnitTests.Platform
{
	[Category(TestCategory.Core)]
	public class UnoWindowLifecycleSupportTests
	{
		[Theory]
		[InlineData(true, true)]
		[InlineData(false, false)]
		public void PreserveWindowOnCloseOnlyForAndroid(bool isAndroid, bool expected)
		{
			Assert.Equal(expected, UnoWindowLifecycleSupport.ShouldPreserveWindowOnClose(isAndroid));
		}
	}
}
