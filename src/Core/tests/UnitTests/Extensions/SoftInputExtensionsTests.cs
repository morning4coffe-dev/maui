#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers;
using NSubstitute;
using Xunit;

namespace Microsoft.Maui.UnitTests.Extensions
{
	[System.ComponentModel.Category(TestCategory.Extensions)]
	public class SoftInputExtensionsTests
	{
		sealed class FakeDispatcher : IDispatcher
		{
			readonly Queue<Action> _pending = new Queue<Action>();
			int _dispatchCount;
			public Action<int>? BeforeDispatch { get; set; }
			public bool Defer { get; set; }
			public bool DispatchResult { get; set; } = true;
			public bool Dispatch(Action action)
			{
				BeforeDispatch?.Invoke(++_dispatchCount);
				if (!DispatchResult)
				{
					return false;
				}

				if (Defer)
					_pending.Enqueue(action);
				else
					action();
				return true;
			}

			public void Drain()
			{
				while (_pending.Count > 0)
					_pending.Dequeue()();
			}

			public bool DispatchDelayed(TimeSpan delay, Action action) => throw new NotImplementedException();

			public IDispatcherTimer CreateTimer() => throw new NotImplementedException();

			public bool IsDispatchRequired => true;
		}

		sealed class InputHandler : ViewHandler<IView, object>
		{
			public int FocusRequests { get; private set; }
			public Action? OnFocus { get; set; }

			public InputHandler() : base(new PropertyMapper<IView>())
			{
			}

			protected override object CreatePlatformView() => new object();

			public override void Invoke(string command, object? args)
			{
				if (command == nameof(IView.Focus))
				{
					FocusRequests++;
					OnFocus?.Invoke();
				}
				base.Invoke(command, args);
			}
		}

		static (ITextInput input, IView view, InputHandler handler) CreateInput(FakeDispatcher dispatcher)
		{
			var view = Substitute.For<IView, ITextInput>();
			view.Handler.Returns(_ => ((IElement)view).Handler as IViewHandler);
			var handler = new InputHandler();
			handler.SetMauiContext(new MauiContext(new ServiceCollection().AddSingleton<IDispatcher>(dispatcher).BuildServiceProvider()));
			handler.SetVirtualView(view);
			Assert.Same(handler, view.Handler);
			Assert.Same(view, ((IViewHandler)handler).VirtualView);
			return ((ITextInput)view, view, handler);
		}

		static void ChangeLifetime(IView view, InputHandler handler, string change)
		{
			switch (change)
			{
				case "detach":
					((IElementHandler)handler).DisconnectHandler();
					break;
				case "replace":
					((IElement)view).Handler = new InputHandler();
					break;
				case "rebind":
					handler.SetVirtualView(Substitute.For<IView>());
					break;
				case "rebind-back":
					handler.SetVirtualView(Substitute.For<IView>());
					handler.SetVirtualView(view);
					break;
				case "reconnect":
					((IElementHandler)handler).DisconnectHandler();
					handler.SetVirtualView(view);
					break;
				default:
					throw new ArgumentException("Unknown lifecycle transition.", nameof(change));
			}
		}

		[Theory]
		[InlineData("detach")]
		[InlineData("replace")]
		[InlineData("rebind")]
		[InlineData("rebind-back")]
		[InlineData("reconnect")]
		public async Task HideSoftInputAsync_QueuedWorkDoesNotOutliveHandler(string change)
		{
			var dispatcher = new FakeDispatcher { Defer = true };
			var (input, view, handler) = CreateInput(dispatcher);
			var request = input.HideSoftInputAsync(default);

			ChangeLifetime(view, handler, change);
			dispatcher.Drain();

			Assert.False(await request);
		}

		[Theory]
		[InlineData(1, "detach")]
		[InlineData(2, "detach")]
		[InlineData(3, "detach")]
		[InlineData(1, "replace")]
		[InlineData(2, "replace")]
		[InlineData(3, "replace")]
		[InlineData(1, "rebind")]
		[InlineData(2, "rebind")]
		[InlineData(3, "rebind")]
		[InlineData(1, "rebind-back")]
		[InlineData(2, "rebind-back")]
		[InlineData(3, "rebind-back")]
		[InlineData(1, "reconnect")]
		[InlineData(2, "reconnect")]
		[InlineData(3, "reconnect")]
		public async Task ShowSoftInputAsync_RechecksLifetimeAtEveryDispatch(int step, string change)
		{
			var dispatcher = new FakeDispatcher();
			var (input, view, handler) = CreateInput(dispatcher);
			dispatcher.BeforeDispatch = count =>
			{
				if (count == step)
					ChangeLifetime(view, handler, change);
			};

			Assert.False(await input.ShowSoftInputAsync(default));
			Assert.Equal(step == 3 ? 1 : 0, handler.FocusRequests);
		}

		[Fact]
		public async Task ShowSoftInputAsync_FocusCallbackCanDetach()
		{
			var dispatcher = new FakeDispatcher();
			var (input, _, handler) = CreateInput(dispatcher);
			handler.OnFocus = () => ((IElementHandler)handler).DisconnectHandler();

			Assert.False(await input.ShowSoftInputAsync(default));
			Assert.Equal(1, handler.FocusRequests);
		}

		[Theory]
		[InlineData(1)]
		[InlineData(2)]
		public async Task ShowSoftInputAsync_StopsWhenPrerequisiteDispatchFails(int step)
		{
			var dispatcher = new FakeDispatcher();
			var (input, _, handler) = CreateInput(dispatcher);
			dispatcher.BeforeDispatch = count => dispatcher.DispatchResult = count != step;

			Assert.False(await input.ShowSoftInputAsync(default));
			Assert.Equal(0, handler.FocusRequests);
		}

		[Fact]
		public async Task ShowSoftInputAsync_CancellationFromFocusPreventsKeyboardWork()
		{
			using var cancellation = new CancellationTokenSource();
			var (input, _, handler) = CreateInput(new FakeDispatcher());
			handler.OnFocus = cancellation.Cancel;

			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => input.ShowSoftInputAsync(cancellation.Token));
			Assert.Equal(1, handler.FocusRequests);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public async Task SoftInputAsync_CancelledQueuedWorkDoesNotReachPlatform(bool show)
		{
			var dispatcher = new FakeDispatcher { Defer = true };
			var (input, _, handler) = CreateInput(dispatcher);
			using var cancellation = new CancellationTokenSource();
			var request = show ? input.ShowSoftInputAsync(cancellation.Token) : input.HideSoftInputAsync(cancellation.Token);
			cancellation.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
			dispatcher.Drain();
			Assert.Equal(0, handler.FocusRequests);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public async Task SoftInputAsync_CustomHandlerRetainsIdentityChecks(bool detach)
		{
			var dispatcher = new FakeDispatcher { Defer = true };
			var view = Substitute.For<IView, ITextInput>();
			var handler = Substitute.For<IViewHandler>();
			var platform = new object();
			view.Handler.Returns(handler);
			handler.PlatformView.Returns(platform);
			((IElementHandler)handler).VirtualView.Returns(view);
			handler.MauiContext.Returns(new MauiContext(new ServiceCollection().AddSingleton<IDispatcher>(dispatcher).BuildServiceProvider()));
			var request = ((ITextInput)view).HideSoftInputAsync(default);
			if (detach)
				view.Handler = null;
			dispatcher.Drain();

			if (detach)
				Assert.False(await request);
			else
				await Assert.ThrowsAsync<NotSupportedException>(() => request);
		}

		[Fact]
		public async Task ShowSoftInputAsync_CurrentLifetimeStillReachesPlatform()
		{
			var (input, _, handler) = CreateInput(new FakeDispatcher());

			// The neutral target has no keyboard backend. Its exception proves the
			// public API still reaches the platform rather than reporting false.
			await Assert.ThrowsAsync<NotSupportedException>(() => input.ShowSoftInputAsync(default));
			Assert.Equal(1, handler.FocusRequests);
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

		[Fact]
		public async Task InvokeOnDispatcherAsync_CancelledQueuedActionDoesNotExecute()
		{
			var dispatcher = new FakeDispatcher { Defer = true };
			using var cancellation = new CancellationTokenSource();
			var called = false;
			var request = SoftInputExtensions.InvokeOnDispatcherAsync(dispatcher, () =>
			{
				called = true;
				return true;
			}, cancellation.Token);

			cancellation.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(cancellation.Token));
			dispatcher.Drain();

			Assert.False(called);
		}

		[Fact]
		public async Task InvokeOnDispatcherAsync_QueuedActionStillRunsWithoutCancellation()
		{
			var dispatcher = new FakeDispatcher { Defer = true };
			var request = SoftInputExtensions.InvokeOnDispatcherAsync(dispatcher, () => true);

			Assert.False(request.IsCompleted);
			dispatcher.Drain();

			Assert.True(await request);
		}
	}
}
