using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Internals;

namespace Microsoft.Maui.Controls.Platform
{
	internal partial class ModalNavigationManager
	{
		Window _window;
		public IReadOnlyList<Page> ModalStack => _modalPages.Pages;
		IMauiContext WindowMauiContext => _window.MauiContext;

		List<Page> _platformModalPages = new List<Page>();
		NavigatingStepRequestList _modalPages = new NavigatingStepRequestList();

		Page? _currentPage;

		Page CurrentPlatformPage =>
			_platformModalPages.Count > 0 ? _platformModalPages[_platformModalPages.Count - 1] : (_window.Page ?? throw new InvalidOperationException("Current Window isn't loaded"));

		Page CurrentPlatformModalPage =>
			_platformModalPages.Count > 0 ? _platformModalPages[_platformModalPages.Count - 1] : throw new InvalidOperationException("Modal Stack is Empty");

		Page? CurrentPage
		{
			get
			{
				var currentPage = _modalPages.Count > 0 ? _modalPages[_modalPages.Count - 1].Page : _window.Page;

				if (currentPage is Shell shell)
					currentPage = shell.CurrentPage;

				return currentPage;
			}
		}

		// Shell takes care of firing its own Modal life cycle events
		// With shell you cam remove / add multiple modals at once
		bool FireLifeCycleEvents => _window?.Page is not Shell;

		partial void InitializePlatform();

		public ModalNavigationManager(Window window)
		{
			_window = window;
			_window.PropertyChanged += (_, args) =>
			{
				if (args.Is(Window.PageProperty))
					SettingNewPage();
			};

			InitializePlatform();

			_window.HandlerChanging += OnWindowHandlerChanging;
			_window.Destroying += (_, _) =>
			{
				ClearModalPages(platform: true);
			};
		}

		void OnWindowHandlerChanging(object? sender, HandlerChangingEventArgs e)
		{
			// If the window handler is changing the activity is being recreated
			// the window activated/resumed event will take care of syncing the platform modals
			if (e.OldHandler is not null)
			{
				ClearModalPages(platform: true);
			}
		}

		public Task<Page?> PopModalAsync()
		{
			return PopModalAsync(true);
		}

		public Task PushModalAsync(Page modal)
		{
			return PushModalAsync(modal, true);
		}

		bool syncing = false;
#if UNO
		readonly ModalRootLifetime _embeddedRootLifetime = new ModalRootLifetime();
		readonly HashSet<Page> _outgoingModalPages = new HashSet<Page>();

		internal void BeginEmbeddedRootLifetime()
		{
			_embeddedRootLifetime.Begin();
		}
#endif

		bool IsModalReady
		{
			get
			{
				return
					_window?.Page?.Handler is not null &&
					_window.Handler is not null
					&& IsModalPlatformReady;
			}
		}

		void SyncPlatformModalStack([CallerMemberName] string? callerName = null)
		{
			var logger = _window.FindMauiContext(true)?.Services?.CreateLogger<ModalNavigationManager>();
			SyncPlatformModalStackAsync().FireAndForget(logger, callerName);
		}

		void SyncModalStackWhenPlatformIsReady([CallerMemberName] string? callerName = null)
		{
			var logger = _window.FindMauiContext(true)?.Services?.CreateLogger<ModalNavigationManager>();
			SyncModalStackWhenPlatformIsReadyAsync().FireAndForget(logger, callerName);
		}


		// This code only processes a single sync action per call.
		// It recursively calls itself until no more sync actions are left to perform.
		//
		// A lot can change during the process of pushing/popping a page
		// i.e. Users might change the root page during an appearing event.
		// So, instead of just bull dozing through the whole sync we perform one
		// sync step then recalculate the state of affairs and then perform another
		// until no more sync operations are left.
		// Typically it's always a good idea to re-evaluate after any async operation has completed
		async Task SyncPlatformModalStackAsync()
		{
			if (!IsModalReady || syncing)
				return;

#if UNO
			var rootLifetime = _embeddedRootLifetime.Token;
			if (rootLifetime.IsCancellationRequested)
				return;
#endif
			bool syncAgain = false;

			try
			{
				syncing = true;

				int popTo;

				for (popTo = 0; popTo < _platformModalPages.Count && popTo < _modalPages.Count; popTo++)
				{
					if (_platformModalPages[popTo] != _modalPages[popTo].Page)
					{
						break;
					}
				}

				// This means the modal stacks are already synced so we don't have to do anything
				if (_platformModalPages.Count == _modalPages.Count && popTo == _platformModalPages.Count)
					return;

				// This ensures that appearing has fired on the final page that will be visible after 
				// the sync has finished
				CurrentPage?.SendAppearing();
#if UNO
				rootLifetime.ThrowIfCancellationRequested();
#endif

				// Pop platform modal pages until we get to the point where the xplat expectation
				// matches the platform modals
				if (_platformModalPages.Count > popTo && IsModalReady)
				{
					bool animated = false;
					if (_modalPages.TryGetValue(CurrentPlatformModalPage, out var request))
					{
						_modalPages.Remove(CurrentPlatformModalPage);
						animated = request.IsAnimated;
					}

					var page = await PopModalPlatformAsync(animated);
#if UNO
					rootLifetime.ThrowIfCancellationRequested();
#endif
					page.Parent?.RemoveLogicalChild(page);
#if UNO
					_outgoingModalPages.Remove(page);
#endif
					syncAgain = true;
				}

				if (!syncAgain)
				{
					//push any modals that need to be synced
					var i = _platformModalPages.Count;
					if (i < _modalPages.Count && IsModalReady)
					{
						var nextRequest = _modalPages[i];
						var nextPage = nextRequest.Page;
						bool animated = nextRequest.IsAnimated;

						await PushModalPlatformAsync(nextPage, animated);
#if UNO
						rootLifetime.ThrowIfCancellationRequested();
#endif
						syncAgain = true;
					}
				}
			}
			finally
			{
#if UNO
				if (!rootLifetime.IsCancellationRequested)
				{
#endif
				// Code has multiple exit points during the sync operation.
				// So we're using a try/finally to ensure that syncing always 
				// gets transitioned to false. If more exit points are added at a later point  
				// we don't have to always worry about the exit point setting syncing to false.
				syncing = false;

				// syncAgain is only set after a successful operation so we won't hit a case here
				// where we hit an infinite loop of syncing.
				if (syncAgain)
				{
					await SyncModalStackWhenPlatformIsReadyAsync().ConfigureAwait(false);
				}
#if UNO
				}
#endif
			}
		}

		Task _waitForModalToFinishTask = Task.CompletedTask;

		public async Task<Page?> PopModalAsync(bool animated)
		{
			if (_modalPages.Count <= 0)
				throw new InvalidOperationException("PopModalAsync failed because modal stack is currently empty.");

#if UNO
			var rootLifetime = _embeddedRootLifetime.Token;
			rootLifetime.ThrowIfCancellationRequested();
			await _waitForModalToFinishTask.WaitAsync(rootLifetime);
			rootLifetime.ThrowIfCancellationRequested();
#else
			await _waitForModalToFinishTask;
#endif

			Page modal = _modalPages[_modalPages.Count - 1].Page;

			if (_window.OnModalPopping(modal))
			{
				_window.OnPopCanceled();
				return null;
			}
#if UNO
			rootLifetime.ThrowIfCancellationRequested();
#endif

#if UNO
			// A pop leaves both stacks before the incoming platform page has loaded.
			// Keep ownership visible to embedded-root teardown throughout that transition.
			_outgoingModalPages.Add(modal);
#endif
			_modalPages.Remove(modal);

			if (FireLifeCycleEvents)
			{
				modal.SendNavigatingFrom(new NavigatingFromEventArgs(CurrentPage, NavigationType.Pop));
			}

			modal.SendDisappearing();

			// With shell we want to make sure to only fire the appearing event
			// on the final page that will be visible after the pop has completed
			if (_window.Page is Shell shell)
			{
				if (!shell.CurrentItem.CurrentItem.IsPoppingModalStack)
				{
					CurrentPage?.SendAppearing();
				}
			}
			else
			{
				CurrentPage?.SendAppearing();
			}

#if UNO
			rootLifetime.ThrowIfCancellationRequested();
#endif
			bool isPlatformReady = IsModalReady;
			Task popTask =
				(isPlatformReady && !syncing) ? PopModalPlatformAsync(animated) : Task.CompletedTask;

			await popTask;
#if UNO
			rootLifetime.ThrowIfCancellationRequested();
#endif
			modal.Parent?.RemoveLogicalChild(modal);
			_window.OnModalPopped(modal);
#if UNO
			_outgoingModalPages.Remove(modal);
			if (rootLifetime.IsCancellationRequested)
				return modal;
#endif

			if (FireLifeCycleEvents)
			{
				modal.SendNavigatedFrom(new NavigatedFromEventArgs(CurrentPage, NavigationType.Pop));
				CurrentPage?.SendNavigatedTo(new NavigatedToEventArgs(modal, NavigationType.Pop));
			}

			if (!isPlatformReady)
				SyncModalStackWhenPlatformIsReady();

			return modal;
		}

		public async Task PushModalAsync(Page modal, bool animated)
		{
#if UNO
			var rootLifetime = _embeddedRootLifetime.Token;
			rootLifetime.ThrowIfCancellationRequested();
			await _waitForModalToFinishTask.WaitAsync(rootLifetime);
			rootLifetime.ThrowIfCancellationRequested();
#else
			await _waitForModalToFinishTask;
#endif

			_window.OnModalPushing(modal);
#if UNO
			rootLifetime.ThrowIfCancellationRequested();
#endif

			var previousPage = CurrentPage;
			_modalPages.Add(new NavigationStepRequest(modal, true, animated));
			_window.AddLogicalChild(modal);

			if (FireLifeCycleEvents)
			{
				previousPage?.SendNavigatingFrom(new NavigatingFromEventArgs(CurrentPage, NavigationType.Push));
			}

			if (_window.Page is Shell shell)
			{
				// With shell we want to make sure to only fire the appearing event
				// on the final page that will be visible after the pop has completed
				if (!shell.CurrentItem.CurrentItem.IsPushingModalStack)
				{
					previousPage?.SendDisappearing();
					CurrentPage?.SendAppearing();
				}
			}
			else
			{
				previousPage?.SendDisappearing();
				CurrentPage?.SendAppearing();
			}

#if UNO
			rootLifetime.ThrowIfCancellationRequested();
#endif
			bool isPlatformReady = IsModalReady;
			if (isPlatformReady && !syncing)
			{
				if (ModalStack.Count == 0)
				{
					modal.NavigationProxy.Inner = _window.Navigation;
					await PushModalPlatformAsync(modal, animated);
#if UNO
					rootLifetime.ThrowIfCancellationRequested();
#endif
				}
				else
				{
					await PushModalPlatformAsync(modal, animated);
#if UNO
					rootLifetime.ThrowIfCancellationRequested();
#endif
					modal.NavigationProxy.Inner = _window.Navigation;
				}
			}

			if (FireLifeCycleEvents)
			{
				previousPage?.SendNavigatedFrom(new NavigatedFromEventArgs(CurrentPage, NavigationType.Push));
				CurrentPage?.SendNavigatedTo(new NavigatedToEventArgs(previousPage, NavigationType.Push));
			}

#if UNO
			rootLifetime.ThrowIfCancellationRequested();
#endif
			_window.OnModalPushed(modal);

			if (!isPlatformReady)
				SyncModalStackWhenPlatformIsReady();
		}

		void SettingNewPage()
		{
			if (_window.Page is null)
			{
				_currentPage = null;
				return;
			}

			if (_currentPage != _window.Page)
			{
				var previousPage = _currentPage;
				_currentPage = _window.Page;

				if (previousPage is not null)
				{
					previousPage.HandlerChanged -= OnCurrentPageHandlerChanged;
					ClearModalPages(xplat: true);
				}

				if (_currentPage is not null)
				{
					if (_currentPage.Handler is null)
					{
						_currentPage.HandlerChanged += OnCurrentPageHandlerChanged;
					}
					else
					{
						SyncModalStackWhenPlatformIsReady();
					}
				}
			}
		}

		void OnCurrentPageHandlerChanged(object? sender, EventArgs e)
		{
			if (_currentPage is not null)
			{
				_currentPage.HandlerChanged -= OnCurrentPageHandlerChanged;
				SyncModalStackWhenPlatformIsReady();
			}
		}

		partial void OnPageAttachedHandler();

		public void PageAttachedHandler() => OnPageAttachedHandler();

		internal void ClearModalPages(bool xplat = false, bool platform = false)
		{
			if (xplat)
				_modalPages.Clear();

			if (platform)
				_platformModalPages.Clear();
		}

		// Windows and Android have basically the same requirement that
		// we need to wait for the current page to finish loading before
		// satisfying Modal requests.
		// This will most likely change once we switch Android to using dialog fragments		
#if WINDOWS || ANDROID
		IDisposable? _platformPageWatchingForLoaded;

		async Task SyncModalStackWhenPlatformIsReadyAsync()
		{
			DisconnectPlatformPageWatchingForLoaded();

			if (IsModalPlatformReady)
			{
				await SyncPlatformModalStackAsync().ConfigureAwait(false);
			}
			else if (IsWindowReadyForModals)
			{
				if (CurrentPlatformPage.Handler is null)
				{
					CurrentPlatformPage.HandlerChanged += OnCurrentPlatformPageHandlerChanged;

					_platformPageWatchingForLoaded = new ActionDisposable(() =>
					{
						CurrentPlatformPage.HandlerChanged -= OnCurrentPlatformPageHandlerChanged;
					});
				}
				// This accounts for cases where we swap the root page out
				// We want to wait for that to finish loading before processing any modal changes
#if ANDROID
				else if (_window?.Page is not null && !_window.Page.IsLoadedOnPlatform())
				{
					var windowPage = _window.Page;
					_platformPageWatchingForLoaded =
						windowPage.OnLoaded(() => OnCurrentPlatformPageLoaded(windowPage, EventArgs.Empty));
				}
#endif

				if (!CurrentPlatformPage.IsLoadedOnPlatform() &&
						  CurrentPlatformPage.Handler is not null)
				{
					var currentPlatformPage = CurrentPlatformPage;
					_platformPageWatchingForLoaded =
						currentPlatformPage.OnLoaded(() => OnCurrentPlatformPageLoaded(currentPlatformPage, EventArgs.Empty));
				}
			}
		}

		void OnCurrentPlatformPageHandlerChanged(object? sender, EventArgs e)
		{
			DisconnectPlatformPageWatchingForLoaded();
			SyncModalStackWhenPlatformIsReady();
		}

		void DisconnectPlatformPageWatchingForLoaded()
		{
			_platformPageWatchingForLoaded?.Dispose();
		}

		void OnCurrentPlatformPageLoaded(object? sender, EventArgs e)
		{
			DisconnectPlatformPageWatchingForLoaded();
			SyncPlatformModalStack();
		}

		bool IsWindowReadyForModals =>
					_window?.Page?.Handler is not null &&
#if WINDOWS
					_firstActivated;
#else
					_window.IsActivated;
#endif

		bool IsModalPlatformReady
		{
			get
			{
				bool result =
					IsWindowReadyForModals
#if ANDROID
					&& _window?.Page?.IsLoadedOnPlatform() == true
#endif
					&& CurrentPlatformPage?.Handler is not null
					&& CurrentPlatformPage.IsLoadedOnPlatform();

				if (result)
					DisconnectPlatformPageWatchingForLoaded();

				return result;
			}
		}
#endif
	}
}
