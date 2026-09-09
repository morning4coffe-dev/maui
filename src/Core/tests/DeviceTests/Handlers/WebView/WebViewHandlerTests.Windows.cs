using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DeviceTests.Stubs;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.Maui.DeviceTests
{
	[Category("WebViewLocalMapping")]
	public class WebViewLocalMappingTests : CoreHandlerTestBase<Microsoft.Maui.Handlers.WebViewHandler, WebViewStub>
	{
		[Theory]
		[InlineData("white_raw.png")]
		[InlineData("https://appdir/white_raw.png")]
		public async Task LocalUrlLoadsWithoutReload(string url)
		{
			await InvokeOnMainThreadAsync(async () =>
			{
				var webView = new WebViewStub { Width = 100, Height = 100 };
				await AttachAndRun(webView, async handler =>
				{
					var native = (Microsoft.Maui.Platform.MauiWebView)handler.PlatformView;
					await native.EnsureCoreWebView2Async();
					var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
					ulong? navigationId = null;
					void OnStarting(WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
					{
						if (args.Uri == "https://appdir/white_raw.png")
							navigationId = args.NavigationId;
					}
					void OnCompleted(WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
					{
						if (args.NavigationId == navigationId)
							loaded.TrySetResult(args.IsSuccess);
					}
					native.NavigationStarting += OnStarting;
					native.NavigationCompleted += OnCompleted;
					try
					{
						native.LoadUrl(url);
						Assert.True(await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15)));
						Assert.Equal("true", await native.ExecuteScriptAsync(
							"document.images.length === 1 && document.images[0].complete && document.images[0].naturalWidth > 0"));
					}
					finally
					{
						native.NavigationStarting -= OnStarting;
						native.NavigationCompleted -= OnCompleted;
					}
				});
			});
		}

		[Fact]
		public async Task ExternalNavigationCannotInheritPendingLocalHtmlMapping()
		{
			await InvokeOnMainThreadAsync(async () =>
			{
				var webView = new WebViewStub { Width = 100, Height = 100 };
				await AttachAndRun(webView, async handler =>
				{
					var native = (Microsoft.Maui.Platform.MauiWebView)handler.PlatformView;
					await native.EnsureCoreWebView2Async();

					async Task NavigateAsync(Action navigate, string expectedUrl = null)
					{
						var loaded = new TaskCompletionSource<bool>();
						ulong? navigationId = null;
						void OnStarting(Microsoft.UI.Xaml.Controls.WebView2 sender,
							Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
						{
							if (expectedUrl is null || args.Uri == expectedUrl)
								navigationId = args.NavigationId;
						}
						void OnCompleted(Microsoft.UI.Xaml.Controls.WebView2 sender,
							Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
						{
							if (args.IsSuccess && args.NavigationId == navigationId)
								loaded.TrySetResult(true);
						}
						native.NavigationStarting += OnStarting;
						native.NavigationCompleted += OnCompleted;
						try
						{
							navigate();
							Assert.True(await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15)));
						}
						finally
						{
							native.NavigationStarting -= OnStarting;
							native.NavigationCompleted -= OnCompleted;
						}
					}

					const string fileName = "white_raw.png";
					static string ImageTag(string url) => $"<img src='{url}' onload='window.localResource = true' onerror='window.localResource = false'>";
					var html = $"<html><head></head><body>{ImageTag(fileName)}</body></html>";
					await NavigateAsync(() => native.LoadHtml(html, null));
					Assert.Equal("true", await native.ExecuteScriptAsync("window.localResource"));
					await native.ExecuteScriptAsync(
						$"fetch('https://appdir/{fileName}').then(r => window.localFetch = r.ok, () => window.localFetch = false)");
					var fetchDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
					while (await native.ExecuteScriptAsync("window.localFetch === undefined") == "true" && DateTime.UtcNow < fetchDeadline)
						await Task.Delay(50);
					Assert.Equal("false", await native.ExecuteScriptAsync("window.localFetch"));

					using var portReservation = new TcpListener(IPAddress.Loopback, 0);
					portReservation.Start();
					var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
					portReservation.Stop();
					using var listener = new HttpListener();
					var url = $"http://127.0.0.1:{port}/";
					listener.Prefixes.Add(url);
					listener.Start();
					var request = listener.GetContextAsync();

					// Issue both requests in the same UI turn so the URL can supersede
					// NavigateToString. Only the external navigation's completion is accepted.
					var navigation = NavigateAsync(() =>
					{
						native.LoadHtml(html, null);
						native.LoadUrl(url);
					}, url);
					var response = (await request.WaitAsync(TimeSpan.FromSeconds(15))).Response;
					var body = Encoding.UTF8.GetBytes($"<html><body>{ImageTag($"https://appdir/{fileName}")}</body></html>");
					response.ContentType = "text/html";
					response.ContentLength64 = body.Length;
					await response.OutputStream.WriteAsync(body);
					response.Close();
					await navigation;
					Assert.Equal("false", await native.ExecuteScriptAsync("window.localResource"));
				});
			});
		}
	}

	public partial class WebViewHandlerTests
	{
		[Theory(DisplayName = "UrlSource Updates Correctly")]
		[InlineData("<h1>Old Source</h1><br>", "<p>New Source</p>\"")]
		[InlineData("<p>Old Source</p><br>", "<h1>New Source</h1>\"")]
		public async Task HtmlSourceUpdatesCorrectly(string oldSource, string newSource)
		{
			var pageLoadTimeout = TimeSpan.FromSeconds(2);

			await InvokeOnMainThreadAsync(async () =>
			{
				var webView = new WebViewStub()
				{
					Width = 100,
					Height = 100,
					Source = new HtmlWebViewSourceStub { Html = oldSource }
				};

				var handler = CreateHandler(webView);

				var platformView = handler.PlatformView;

				// Setup the view to be displayed/parented and run our tests on it
				await AttachAndRun(webView, async (handler) =>
				{
					// Wait for the page to load
					var tcsLoaded = new TaskCompletionSource<bool>();
					var ctsTimeout = new CancellationTokenSource(pageLoadTimeout);
					ctsTimeout.Token.Register(() => tcsLoaded.TrySetException(new TimeoutException($"Failed to load HTML")));

					webView.NavigatedDelegate = (evnt, url, result) =>
					{
						// Set success when we have a successful nav result
						if (result == WebNavigationResult.Success)
							tcsLoaded.TrySetResult(result == WebNavigationResult.Success);
					};

					// Load the new Source
					webView.Source = new HtmlWebViewSourceStub { Html = newSource };

					handler.UpdateValue(nameof(IWebView.Source));

					// If the new source is loaded without exceptions, the test has passed
					Assert.True(await tcsLoaded.Task);
				});
			});
		}

		[Fact(DisplayName = "Closing Window With WebView Doesnt Crash")]
		public async Task ClosingWindowWithWebViewDoesntCrash()
		{
			EnsureHandlerCreated(builder =>
			{
				builder.Services.AddSingleton(typeof(UI.Xaml.Window), (services) => new UI.Xaml.Window());
			});

			var webView = new WebViewStub()
			{
				Source = new UrlWebViewSourceStub { Url = "https://dotnet.microsoft.com/" }
			};

			var handler = await CreateHandlerAsync(webView);

			await InvokeOnMainThreadAsync(async () =>
			{
				TaskCompletionSource navigationComplete = new TaskCompletionSource();
				handler.PlatformView.NavigationCompleted += (_, _) =>
				{
					navigationComplete?.SetResult();
				};

				await AttachAndRun(webView, async (handler) =>
				{
					await handler.PlatformView.OnLoadedAsync();
					await navigationComplete.Task;
					navigationComplete = null;
				});
			});
		}

		WebView2 GetNativeWebView(WebViewHandler webViewHandler) =>
			webViewHandler.PlatformView;

		string GetNativeSource(WebViewHandler webViewHandler)
		{
			var plaformWebView = GetNativeWebView(webViewHandler);
			return plaformWebView.Source.AbsoluteUri;
		}
	}
}
