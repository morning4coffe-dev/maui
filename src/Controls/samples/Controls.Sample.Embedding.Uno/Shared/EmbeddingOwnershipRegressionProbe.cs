using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Controls.Embedding.Uno;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using NativeBorder = Microsoft.UI.Xaml.Controls.Border;
using NativeWindow = Microsoft.UI.Xaml.Window;
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace Maui.Controls.Sample.Uno;

internal static class EmbeddingOwnershipRegressionProbe
{
	static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	internal static MauiAppBuilder ConfigureOwnershipProbe(this MauiAppBuilder builder)
	{
		builder.Services.AddScoped<IMauiInitializeScopedService, ScopeProbe>();
		return builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<ProbeView, ProbeViewHandler>());
	}

	internal static Task<(bool Passed, string Report)> RunAsync(MauiEmbeddingSession session, MauiHost first, MauiHost second) =>
		RunCoreAsync(session, first, second, includeSessionTransfers: true);

	internal static Task<(bool Passed, string Report)> RunSingleWindowAsync(MauiEmbeddingSession session, MauiHost first, MauiHost second) =>
		RunCoreAsync(session, first, second, includeSessionTransfers: false);

	static async Task<(bool Passed, string Report)> RunCoreAsync(MauiEmbeddingSession session, MauiHost first, MauiHost second, bool includeSessionTransfers)
	{
		var report = new StringBuilder();
		var passed = true;
		var originalFirst = first.MauiContent;
		var originalSecond = second.MauiContent;
		var app = (MauiApplication)MauiEmbeddingSession.SharedApp.Services.GetRequiredService<IApplication>();
		try
		{
			await WaitForLoadedAsync(first);
			await WaitForLoadedAsync(second);
			await RunCaseAsync("public overload duplicate", () => VerifyPublicDuplicateAsync(session, app));
			await RunCaseAsync("public overload creation failure", () => VerifyPublicFailureAsync(session, app, true));
			await RunCaseAsync("public overload connection failure", () => VerifyPublicFailureAsync(session, app, false));
			await RunCaseAsync("public overload scoped initialization failure", () => VerifyScopeFailureAsync(session, app));
			await RunCaseAsync("public overload application registration failure", () => VerifyRegistrationFailureAsync(session, app));
			await RunCaseAsync("MauiContent dependency-property rejection", () => VerifyContentRejectionAsync(session, first, second));
			if (includeSessionTransfers)
			{
				await RunCaseAsync("disposed Session rejection", () => VerifySessionRejectionAsync(session, first, true));
				await RunCaseAsync("page-conflicting Session rejection", () => VerifySessionRejectionAsync(session, first, false));
				await RunCaseAsync("valid same-host Session transfer", () => VerifyValidTransferAsync(session, first));
			}
			else
			{
				report.AppendLine("SCOPE single-window: cross-session transfers require the Desktop probe.");
			}
		}
		finally
		{
			first.MauiContent = null;
			second.MauiContent = null;
			first.Session = session;
			second.Session = session;
			first.MauiContent = originalFirst;
			second.MauiContent = originalSecond;
		}
		return (passed, report.ToString());

		async Task RunCaseAsync(string name, Func<Task> test)
		{
			first.MauiContent = null;
			second.MauiContent = null;
			var windows = app.Windows.ToArray();
			try
			{
				await test();
				report.AppendLine($"PASS {name}");
				Console.WriteLine($"EMBEDDING-OWNERSHIP PASS {name}");
			}
			catch (Exception error)
			{
				passed = false;
				report.AppendLine($"FAIL {name}: {error}");
				Console.WriteLine($"EMBEDDING-OWNERSHIP FAIL {name}: {error}");
			}
			finally
			{
				ScopeProbe.FailInitialization = false;
				first.MauiContent = null;
				second.MauiContent = null;
				first.Session = session;
				second.Session = session;
				// Retain the failed assertion, but do not let a red scope leak contaminate the next case.
				foreach (var window in app.Windows.Except(windows).ToArray())
					((IWindow)window).Destroying();
			}
		}
	}

	static Task VerifyPublicDuplicateAsync(MauiEmbeddingSession session, MauiApplication app)
	{
		var content = new ProbeView();
		var platform = content.ToPlatformEmbedded(MauiEmbeddingSession.SharedApp, session.PlatformWindow);
		var owner = content.Parent;
		var handler = content.Handler;
		var windows = app.Windows.Count;
		var created = ScopeProbe.Created;
		var disposed = ScopeProbe.Disposed;
		for (var attempt = 0; attempt < 3; attempt++)
			ExpectRejected(() => content.ToPlatformEmbedded(MauiEmbeddingSession.SharedApp, session.PlatformWindow));
		Verify(app.Windows.Count == windows && ScopeProbe.Created == created && ScopeProbe.Disposed == disposed,
			"Duplicate public-overload calls must not create windows or scoped services.");
		Verify(ReferenceEquals(content.Parent, owner) && ReferenceEquals(content.Handler, handler) &&
			ReferenceEquals(handler?.PlatformView, platform), "Duplicate calls must retain the original owner and handler.");
		((IView)content).DisconnectHandlers();
		return Task.CompletedTask;
	}

	static Task VerifyPublicFailureAsync(MauiEmbeddingSession session, MauiApplication app, bool creation)
	{
		var content = new ProbeView { FailCreation = creation, FailConnection = !creation };
		var windows = app.Windows.Count;
		var created = ScopeProbe.Created;
		var disposed = ScopeProbe.Disposed;
		for (var attempt = 1; attempt <= 3; attempt++)
		{
			ExpectFailure(() => content.ToPlatformEmbedded(MauiEmbeddingSession.SharedApp, session.PlatformWindow), content.Failure);
			Verify(content.Parent is null && content.Handler is null, "Failed public-overload realization must release the view.");
			Verify(app.Windows.Count == windows && ScopeProbe.Created == created + attempt && ScopeProbe.Disposed == disposed + attempt,
				$"Failed public-overload realization must dispose only its new scope (windows={app.Windows.Count - windows}, created={ScopeProbe.Created - created}, disposed={ScopeProbe.Disposed - disposed}).");
		}
		content.FailCreation = false;
		content.FailConnection = false;
		content.ToPlatformEmbedded(MauiEmbeddingSession.SharedApp, session.PlatformWindow);
		Verify(app.Windows.Count == windows + 1 && ScopeProbe.Disposed == disposed + 3,
			"A successful retry must retain its new window scope.");
		((IView)content).DisconnectHandlers();
		return Task.CompletedTask;
	}

	static Task VerifyScopeFailureAsync(MauiEmbeddingSession session, MauiApplication app)
	{
		var windows = app.Windows.Count;
		var created = ScopeProbe.Created;
		var disposed = ScopeProbe.Disposed;
		ScopeProbe.FailInitialization = true;
		for (var attempt = 1; attempt <= 3; attempt++)
		{
			var content = new ProbeView();
			ExpectFailure(() => content.ToPlatformEmbedded(MauiEmbeddingSession.SharedApp, session.PlatformWindow), ScopeProbe.Failure);
			Verify(app.Windows.Count == windows && ScopeProbe.Created == created + attempt && ScopeProbe.Disposed == disposed + attempt,
				"Failed window-context initialization must dispose the new scope before returning.");
			Verify(content.Parent is null && content.Handler is null, "Initialization failure must leave content available.");
		}
		return Task.CompletedTask;
	}

	static async Task VerifyContentRejectionAsync(MauiEmbeddingSession session, MauiHost first, MauiHost second)
	{
		var owned = new ContentPage { Content = new Label { Text = "retained page owner" } };
		var retained = new ContentView { Content = new Label { Text = "retained second host" } };
		first.MauiContent = owned;
		second.MauiContent = retained;
		var handler = retained.Handler;
		var platform = second.Content;
		ExpectRejected(() => second.SetValue(MauiHost.MauiContentProperty, owned));
		var restoredBeforeReload = ReferenceEquals(second.MauiContent, retained);
		await ReloadAsync(second);
		Verify(restoredBeforeReload && ReferenceEquals(second.MauiContent, retained) &&
			ReferenceEquals(second.Session, session) && ReferenceEquals(second.Content, platform) &&
			ReferenceEquals(retained.Handler, handler), "Rejected SetValue must restore the effective DP value and survive reload unchanged.");

		ExpectRejected(() => second.MauiContent = new ContentPage());
		await ReloadAsync(second);
		Verify(ReferenceEquals(second.MauiContent, retained) && ReferenceEquals(retained.Handler, handler),
			"Rejected page replacement must retain its effective configuration.");
		var replacement = new ContentView { Content = new Label { Text = "valid recovery" } };
		second.MauiContent = replacement;
		Verify(ReferenceEquals(second.MauiContent, replacement) && replacement.Handler is not null && retained.Handler is null,
			"A valid replacement after rejection must work without manually clearing content.");
	}

	static Task VerifyRegistrationFailureAsync(MauiEmbeddingSession session, MauiApplication app)
	{
		var windows = app.Windows.Count;
		var created = ScopeProbe.Created;
		var disposed = ScopeProbe.Disposed;
		var failure = new InvalidOperationException("Expected application registration failure.");
		void OnChildAdded(object? sender, ElementEventArgs args)
		{
			if (args.Element is MauiWindow)
				throw failure;
		}
		app.ChildAdded += OnChildAdded;
		try
		{
			ExpectFailure(() => new ProbeView().ToPlatformEmbedded(MauiEmbeddingSession.SharedApp, session.PlatformWindow), failure);
			Verify(app.Windows.Count == windows && ScopeProbe.Created == created + 1 && ScopeProbe.Disposed == disposed + 1,
				"Failed application registration must unwind the newly created window and scope.");
		}
		finally
		{
			app.ChildAdded -= OnChildAdded;
		}
		return Task.CompletedTask;
	}

	static async Task VerifySessionRejectionAsync(MauiEmbeddingSession session, MauiHost host, bool disposedSession)
	{
		var window = new NativeWindow();
		var other = MauiEmbeddingSession.GetOrCreate(window);
		try
		{
			if (disposedSession)
				other.Dispose();
			else
				other.Embed(new ContentPage { Content = new Label { Text = "other session page" } });

			var page = new ContentPage { Content = new Label { Text = "retained original session" } };
			host.MauiContent = page;
			var handler = page.Handler;
			var platform = host.Content;
			ExpectRejected(() => host.Session = other);
			var restoredBeforeReload = ReferenceEquals(host.Session, session);
			await ReloadAsync(host);
			Verify(restoredBeforeReload && ReferenceEquals(host.Session, session) && ReferenceEquals(host.MauiContent, page) &&
				ReferenceEquals(host.Content, platform) && ReferenceEquals(page.Handler, handler),
				"Rejected Session assignment must retain the effective session, content and handler through unload/reload.");
			var bindingContext = new object();
			host.DataContext = bindingContext;
			Verify(ReferenceEquals(page.BindingContext, bindingContext), "Retained content must keep its binding-context bridge.");
			if (disposedSession)
				other = MauiEmbeddingSession.GetOrCreate(window);
			else
				other.Release(other.EmbeddedWindow!.Page!);
			host.Session = other;
			Verify(ReferenceEquals(host.Session, other) && ReferenceEquals(host.MauiContent, page) &&
				ReferenceEquals(other.EmbeddedWindow?.Page, page), "Recovery via valid transfer must not require clearing content.");
			host.Session = session;
			Verify(ReferenceEquals(session.EmbeddedWindow?.Page, page) && other.EmbeddedWindow?.Page is null,
				"Recovery must allow transferring the retained content back.");
		}
		finally
		{
			host.MauiContent = null;
			host.Session = session;
			host.DataContext = null;
			other.Dispose();
			window.Close();
		}
	}

	static async Task VerifyValidTransferAsync(MauiEmbeddingSession session, MauiHost host)
	{
		var window = new NativeWindow();
		var other = MauiEmbeddingSession.GetOrCreate(window);
		try
		{
			var page = new ContentPage { Content = new Label { Text = "transfer across sessions" } };
			host.MauiContent = page;
			var previousHandler = page.Handler;
			host.Session = other;
			Verify(ReferenceEquals(host.Session, other) && ReferenceEquals(host.MauiContent, page) &&
				ReferenceEquals(other.EmbeddedWindow?.Page, page) && session.EmbeddedWindow?.Page is null &&
				page.Handler is not null && !ReferenceEquals(page.Handler, previousHandler), "Valid transfer must update both effective and realized ownership.");
			var handler = page.Handler;
			await ReloadAsync(host);
			Verify(ReferenceEquals(page.Handler, handler), "Transferred content must survive transient unload.");
			host.Session = session;
			Verify(ReferenceEquals(session.EmbeddedWindow?.Page, page) && other.EmbeddedWindow?.Page is null,
				"Transferring back must release only the previous session registration.");
		}
		finally
		{
			host.MauiContent = null;
			host.Session = session;
			other.Dispose();
			window.Close();
		}
	}

	static async Task ReloadAsync(MauiHost host)
	{
		await WaitForLoadedAsync(host);
		var border = host.Parent as NativeBorder ?? throw new InvalidOperationException("The probe host needs a Border parent.");
		var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		void OnUnloaded(object sender, RoutedEventArgs args) => unloaded.TrySetResult();
		void OnLoaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();
		host.Unloaded += OnUnloaded;
		host.Loaded += OnLoaded;
		try
		{
			border.Child = null;
			await unloaded.Task.WaitAsync(Timeout);
			border.Child = host;
			await loaded.Task.WaitAsync(Timeout);
		}
		finally
		{
			host.Unloaded -= OnUnloaded;
			host.Loaded -= OnLoaded;
			if (border.Child is null)
				border.Child = host;
		}
	}

	static async Task WaitForLoadedAsync(FrameworkElement element)
	{
		if (element.IsLoaded)
			return;
		var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		void OnLoaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();
		element.Loaded += OnLoaded;
		try
		{
			await loaded.Task.WaitAsync(Timeout);
		}
		finally
		{
			element.Loaded -= OnLoaded;
		}
	}

	static void ExpectRejected(Action action)
	{
		try
		{ action(); }
		catch (InvalidOperationException) { return; }
		throw new InvalidOperationException("The duplicate or invalid assignment was accepted.");
	}

	static void ExpectFailure(Action action, Exception expected)
	{
		try
		{ action(); }
		catch (Exception error) when (ReferenceEquals(error, expected)) { return; }
		throw new InvalidOperationException("The original realization exception was not preserved.");
	}

	static void Verify(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	internal sealed class ScopeProbe : IMauiInitializeScopedService, IDisposable
	{
		internal static int Created { get; private set; }
		internal static int Disposed { get; private set; }
		internal static bool FailInitialization { get; set; }
		internal static Exception Failure { get; } = new InvalidOperationException("Expected scoped initialization failure.");
		public ScopeProbe() => Created++;
		public void Initialize(IServiceProvider services)
		{
			if (FailInitialization)
				throw Failure;
		}
		public void Dispose() => Disposed++;
	}

	internal sealed class ProbeView : ContentView
	{
		internal bool FailCreation { get; set; }
		internal bool FailConnection { get; set; }
		internal Exception Failure { get; } = new InvalidOperationException("Expected public-overload handler failure.");
	}

	internal sealed class ProbeViewHandler : ContentViewHandler
	{
		protected override ContentPanel CreatePlatformView()
		{
			if (VirtualView is ProbeView { FailCreation: true } view)
				throw view.Failure;
			return base.CreatePlatformView();
		}
		protected override void ConnectHandler(ContentPanel platformView)
		{
			base.ConnectHandler(platformView);
			if (VirtualView is ProbeView { FailConnection: true } view)
				throw view.Failure;
		}
	}
}
