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

		[Theory]
		[InlineData(true, false, SafeAreaRegions.None, SafeAreaRegions.Container)]
		[InlineData(true, false, SafeAreaRegions.All, SafeAreaRegions.Container)]
		[InlineData(true, true, SafeAreaRegions.None, SafeAreaRegions.None)]
		[InlineData(true, true, SafeAreaRegions.Container, SafeAreaRegions.Container)]
		[InlineData(true, true, SafeAreaRegions.SoftInput, SafeAreaRegions.SoftInput)]
		[InlineData(false, false, SafeAreaRegions.None, SafeAreaRegions.None)]
		[InlineData(false, false, SafeAreaRegions.Container, SafeAreaRegions.Container)]
		public void RootSafeAreaPreservesExplicitEdgesAndOtherPlatforms(bool isAndroid, bool hasExplicitEdges, SafeAreaRegions requested, SafeAreaRegions expected)
		{
			Assert.Equal(expected, UnoWindowLifecycleSupport.ResolveRootSafeAreaRegion(isAndroid, hasExplicitEdges, requested));
		}

		[Theory]
		[InlineData(true, false, true, 0)]
		[InlineData(false, true, true, 1)]
		[InlineData(false, false, false, 1)]
		public void BackRequestsPreserveHandledStateAndRootFallback(bool alreadyHandled, bool windowHandlesBack, bool expected, int expectedCalls)
		{
			var calls = 0;
			Assert.Equal(expected, UnoWindowLifecycleSupport.DispatchBackRequest(alreadyHandled, () =>
			{
				calls++;
				return windowHandlesBack;
			}));
			Assert.Equal(expectedCalls, calls);
		}
	}
}
