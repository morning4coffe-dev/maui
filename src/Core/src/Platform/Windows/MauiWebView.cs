using System;
using System.Diagnostics;
using System.Text;
using Microsoft.Maui.ApplicationModel;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;

namespace Microsoft.Maui.Platform
{
	public partial class MauiWebView : WebView2, IWebViewDelegate
	{
		readonly WeakReference<WebViewHandler> _handler;

		[Obsolete("Constructor is no longer used, please use an overloaded version.")]
#pragma warning disable CS8618
		public MauiWebView()
		{
			SetupPlatformEvents();
		}
#pragma warning restore CS8618

		public MauiWebView(WebViewHandler handler)
		{
			ArgumentNullException.ThrowIfNull(handler, nameof(handler));
			_handler = new WeakReference<WebViewHandler>(handler);

			SetupPlatformEvents();
		}

		// Arbitrary local host name for virtual folder mapping
		const string LocalHostName = "appdir";
		const string LocalScheme = $"https://{LocalHostName}/";
		string? _pendingLocalHtml;
		int _navigationVersion;

		// Script to insert a <base> tag into an HTML document
		const string BaseInsertionScript = @"
			var head = document.getElementsByTagName('head')[0];
			var bases = head.getElementsByTagName('base');
			if(bases.length == 0) {
				head.innerHTML = 'baseTag' + head.innerHTML;
			}";

		// Allow for packaged/unpackaged app support
		static string ApplicationPath
		{
			get
			{
#if UNO
				try
				{
					return Package.Current?.InstalledLocation.Path ?? AppContext.BaseDirectory;
				}
				catch (InvalidOperationException)
				{
					return AppContext.BaseDirectory;
				}
#else
				return AppInfoUtils.IsPackagedApp
					? Package.Current.InstalledLocation.Path
					: AppContext.BaseDirectory;
#endif
			}
		}

		public async void LoadHtml(string? html, string? baseUrl)
		{
			try
			{
				var navigationVersion = BeginNavigation();
				if (string.IsNullOrEmpty(baseUrl))
					baseUrl = LocalScheme;

				await EnsureCoreWebView2Async();
				if (navigationVersion != _navigationVersion)
					return;

				// Native WebView2 reports NavigateToString as about:blank; Uno can report
				// the data URI instead. Only the HTML issued by this request may map appdir.
				var script = GetBaseTagInsertionScript(baseUrl);
				var htmlWithScript = $"{script}\n{html}";
				_pendingLocalHtml = IsUriWithLocalScheme(baseUrl) ? htmlWithScript : null;
				if (_pendingLocalHtml is not null)
					MapLocalHost();
				NavigateToString(htmlWithScript);
			}
			catch (Exception exc)
			{
				Debug.WriteLine(nameof(MauiWebView), $"Failed to load HTML: {exc}");
			}
		}

		public async void LoadUrl(string? url)
		{
			// Invalidate local HTML before any cookie/initialization await, not when
			// NavigationStarting eventually arrives for the superseding URL.
			try
			{
				var navigationVersion = BeginNavigation();
				Uri uri = new Uri(url ?? string.Empty, UriKind.RelativeOrAbsolute);

				if (!uri.IsAbsoluteUri || IsUriWithLocalScheme(uri.AbsoluteUri))
				{
					await EnsureCoreWebView2Async();
					if (navigationVersion != _navigationVersion)
						return;

					if (!uri.IsAbsoluteUri)
						uri = new Uri(LocalScheme + url, UriKind.RelativeOrAbsolute);
				}

				if (_handler?.TryGetTarget(out var handler) ?? false)
					await handler.SyncPlatformCookies(uri.AbsoluteUri);

				if (navigationVersion == _navigationVersion)
				{
					if (IsUriWithLocalScheme(uri.AbsoluteUri))
						MapLocalHost();
					Source = uri;
				}
			}
			catch (Exception exc)
			{
				Debug.WriteLine(nameof(MauiWebView), $"Failed to load URL: {exc}");
			}
		}

		int BeginNavigation()
		{
			_pendingLocalHtml = null;
			CoreWebView2?.ClearVirtualHostNameToFolderMapping(LocalHostName);
			return ++_navigationVersion;
		}

		void SetupPlatformEvents()
		{
			NavigationStarting += (sender, args) =>
			{
				var pendingLocalHtml = _pendingLocalHtml;
				_pendingLocalHtml = null;

				// Auto map local virtual app dir host, e.g. if navigating back to local site from a link to an external site
				if (IsUriWithLocalScheme(args?.Uri) ||
					(pendingLocalHtml is not null &&
						(string.Equals(args?.Uri, "about:blank", StringComparison.OrdinalIgnoreCase) ||
							IsWebView2DataUri(args?.Uri, pendingLocalHtml))))
				{
					MapLocalHost();
				}
				// Auto unmap local virtual app dir host if navigating to any other potentially unsafe domain
				else
				{
					CoreWebView2.ClearVirtualHostNameToFolderMapping(LocalHostName);
				}
			};
		}

		void MapLocalHost()
		{
			// Preserve ordinary HTML subresources without granting cross-origin fetch/XHR.
			CoreWebView2.SetVirtualHostNameToFolderMapping(
				LocalHostName,
				ApplicationPath,
				Web.WebView2.Core.CoreWebView2HostResourceAccessKind.DenyCors);
		}

		static bool IsUriWithLocalScheme(string? uri) =>
			Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
			parsed.Scheme == Uri.UriSchemeHttps &&
			string.Equals(parsed.Host, LocalHostName, StringComparison.OrdinalIgnoreCase) &&
			parsed.IsDefaultPort;

		static bool IsWebView2DataUri(string? uri, string expectedHtml)
		{
			// WebView2 sends the web page with inserted base tag as data URI
			const string dataUriBase64 = "data:text/html;charset=utf-8;base64,";
			if (uri == null ||
				uri.StartsWith(
					dataUriBase64,
					StringComparison.OrdinalIgnoreCase) == false)
				return false;

			try
			{
				var decodedHtml = Encoding.UTF8.GetString(Convert.FromBase64String(uri.Substring(dataUriBase64.Length)));
				return string.Equals(decodedHtml, expectedHtml, StringComparison.Ordinal);
			}
			catch (FormatException)
			{
				return false;
			}
		}

		static string GetBaseTagInsertionScript(string baseUrl)
		{
			var baseTag = $"<base href=\"{baseUrl}\"></base>";
			return $"<script>{BaseInsertionScript.Replace("baseTag", baseTag, StringComparison.Ordinal)}</script>";
		}
	}
}
