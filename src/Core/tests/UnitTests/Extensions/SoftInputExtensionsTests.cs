#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.Maui.Dispatching;
using Xunit;

namespace Microsoft.Maui.UnitTests.Extensions
{
	[System.ComponentModel.Category(TestCategory.Extensions)]
	public class SoftInputExtensionsTests
	{
		sealed class FakeDispatcher : IDispatcher
		{
			public bool DispatchResult { get; set; } = true;
			public bool Dispatch(Action action)
			{
				if (!DispatchResult)
				{
					return false;
				}

				action();
				return true;
			}

			public bool DispatchDelayed(TimeSpan delay, Action action) => throw new NotImplementedException();

			public IDispatcherTimer CreateTimer() => throw new NotImplementedException();

			public bool IsDispatchRequired => true;
		}

		[Fact]
		public async Task InvokeOnDispatcherAsync_ReturnsActionResult()
		{
			var result = await SoftInputExtensions.InvokeOnDispatcherAsync(new FakeDispatcher(), () => true);

			Assert.True(result);
		}

		[Fact]
		public async Task InvokeOnDispatcherAsync_ReturnsFalseWhenDispatcherMissing()
		{
			var result = await SoftInputExtensions.InvokeOnDispatcherAsync(null, () => true);

			Assert.False(result);
		}

		[Fact]
		public async Task InvokeOnDispatcherAsync_PreservesFalseResult()
		{
			var called = false;

			var result = await SoftInputExtensions.InvokeOnDispatcherAsync(new FakeDispatcher(), () =>
			{
				called = true;
				return false;
			});

			Assert.True(called);
			Assert.False(result);
		}

		[Fact]
		public async Task InvokeOnDispatcherAsync_ReturnsFalseWhenDispatchFails()
		{
			var called = false;

			var result = await SoftInputExtensions.InvokeOnDispatcherAsync(new FakeDispatcher { DispatchResult = false }, () =>
			{
				called = true;
				return true;
			});

			Assert.False(called);
			Assert.False(result);
		}

		[Fact]
		public async Task InvokeOnDispatcherAsync_PropagatesActionException()
		{
			await Assert.ThrowsAsync<InvalidOperationException>(() =>
				SoftInputExtensions.InvokeOnDispatcherAsync(new FakeDispatcher(), () => throw new InvalidOperationException("boom")));
		}
	}
}
