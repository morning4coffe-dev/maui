using Microsoft.Maui.Controls.Platform;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Maui.Controls.Core.UnitTests;

public class WindowModalCleanupTests
{
	[Fact]
	public async Task DisposedRootCancelsAnUnfinishedModalTransition()
	{
		var lifetime = new ModalRootLifetime();
		var pendingLoaded = new TaskCompletionSource<bool>();
		var navigation = pendingLoaded.Task.WaitAsync(lifetime.Token);

		lifetime.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation);
		Assert.False(pendingLoaded.Task.IsCompleted);
	}

	[Fact]
	public async Task ReplacementRootHasAnIndependentNavigationLifetime()
	{
		var lifetime = new ModalRootLifetime();
		var previousRoot = lifetime.Token;
		lifetime.Cancel();
		lifetime.Begin();
		var replacementRoot = lifetime.Token;
		var loaded = new TaskCompletionSource<bool>();
		var navigation = loaded.Task.WaitAsync(replacementRoot);
		loaded.SetResult(true);

		Assert.True(await navigation);
		Assert.True(previousRoot.IsCancellationRequested);
		Assert.False(replacementRoot.IsCancellationRequested);
		Assert.NotEqual(previousRoot, replacementRoot);
	}

	[Fact]
	public void BeginningAnActiveRootDoesNotReplaceItsLifetime()
	{
		var lifetime = new ModalRootLifetime();
		var current = lifetime.Token;

		lifetime.Begin();

		Assert.Equal(current, lifetime.Token);
		lifetime.Cancel();
	}

	[Fact]
	public void RemovingDisposedModalClearsVisualBookkeepingWithoutPublishingNavigation()
	{
		var window = new Window(new ContentPage());
		var modal = new ContentPage();
		var popped = 0;
		window.ModalPopped += (_, _) => popped++;
		window.OnModalPushed(modal);

		Assert.True(window.RemoveModalFromVisualChildren(modal));
		Assert.False(window.RemoveModalFromVisualChildren(modal));

		Assert.Equal(0, popped);
	}

	[Fact]
	public void NormalModalPopStillPublishesNavigationAndClearsVisualBookkeeping()
	{
		var window = new Window(new ContentPage());
		var modal = new ContentPage();
		var popped = 0;
		window.ModalPopped += (_, _) => popped++;
		window.OnModalPushed(modal);

		window.OnModalPopped(modal);

		Assert.False(window.RemoveModalFromVisualChildren(modal));
		Assert.Equal(1, popped);
	}
}
