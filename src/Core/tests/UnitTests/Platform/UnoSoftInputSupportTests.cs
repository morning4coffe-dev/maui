using Microsoft.Maui;
using Xunit;

namespace Microsoft.Maui.UnitTests.Platform
{
	[Category(TestCategory.Core)]
	public class UnoSoftInputSupportTests
	{
		[Theory]
		[InlineData(true, false, 0, true)]
		[InlineData(false, true, 400, true)]
		[InlineData(false, true, 0, false)]
		[InlineData(false, false, 400, false)]
		public void DetectsAndroidOcclusionWhenVisibilityIsNotProjected(
			bool isVisible,
			bool isAndroid,
			double occludedHeight,
			bool expected)
		{
			Assert.Equal(
				expected,
				UnoSoftInputSupport.IsShowing(isVisible, isAndroid, occludedHeight));
		}
	}
}
