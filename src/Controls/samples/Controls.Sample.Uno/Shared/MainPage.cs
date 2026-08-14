using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Media;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Microsoft.UI.Windowing;
using Windows.Storage;

namespace Microsoft.Maui.Controls.Sample.Uno;

public sealed class MainPage : ContentPage
{
	const string FileSystemProbeAssetName = "FileSystemProbe.txt";
	const string FileSystemProbeAssetContents = "Uno FileSystem sample asset.";
	const string FileSystemProbeLocalContents = "Uno FileSystem local file probe.";
	const string SecureStorageProbeKey = "__uno_securestorage_probe__";
	const string SecureStorageProbeValue = "Uno SecureStorage probe.";

	public MainPage()
	{
		Title = "MAUI on Uno";

		var count = 0;
		var status = new Label
		{
			Text = "Rendered by MAUI's WinUI handlers on Uno",
			FontSize = 20,
		};

		var entry = new Entry
		{
			AutomationId = "MauiEntry",
			Placeholder = "Type into a MAUI Entry",
		};

		var button = new Button
		{
			AutomationId = "MauiButton",
			Text = "Click me",
		};

		var formattedLabel = new Label
		{
			AutomationId = "MauiFormattedLabel",
			FormattedText = new FormattedString
			{
				Spans =
				{
					new Span { Text = "Formatted ", TextColor = Microsoft.Maui.Graphics.Colors.DarkBlue },
					new Span { Text = "MAUI text", FontAttributes = FontAttributes.Bold },
				},
			},
		};

		var fontImage = new Image
		{
			AutomationId = "MauiFontImage",
			HeightRequest = 32,
			HorizontalOptions = LayoutOptions.Start,
			Source = new FontImageSource
			{
				Glyph = "A",
				FontFamily = "sans-serif",
				Size = 32,
				Color = Microsoft.Maui.Graphics.Colors.DarkBlue,
			},
			WidthRequest = 32,
		};

		var toolkitExpander = CreateToolkitExpander();
		var toolkitDrawingView = CreateToolkitDrawingView();
		var essentialsProbe = CreateEssentialsProbe();
		var runtimeDiagnosticsProbe = CreateRuntimeDiagnosticsProbe(this, entry);
		var windowOperationsProbe = CreateWindowOperationsProbe();
		var clipProbe = CreateClipProbe();

		button.Clicked += (_, _) =>
		{
			count++;
			status.Text = $"Clicked {count} time{(count == 1 ? string.Empty : "s")}. Entry: {entry.Text}";
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(32),
				Spacing = 16,
				Children =
				{
					new Label
					{
						Text = ".NET MAUI WinUI target running on Uno Platform",
						FontSize = 28,
						FontAttributes = FontAttributes.Bold,
					},
					status,
					formattedLabel,
					fontImage,
					entry,
					button,
					toolkitExpander,
					toolkitDrawingView,
					essentialsProbe,
					runtimeDiagnosticsProbe,
					windowOperationsProbe,
					clipProbe,
					new Slider
					{
						AutomationId = "MauiSlider",
						Minimum = 0,
						Maximum = 100,
						Value = 40,
					},
					new ProgressBar
					{
						Progress = 0.4,
					},
				},
			},
		};
	}

	static View CreateRuntimeDiagnosticsProbe(MainPage page, Entry entry)
	{
		var status = new Label
		{
			AutomationId = "RuntimeDiagnosticsStatus",
			Text = "Runtime diagnostics not run.",
		};

		var refreshButton = new Button
		{
			AutomationId = "RuntimeDiagnosticsRefresh",
			Text = "Refresh runtime diagnostics",
		};
		refreshButton.Clicked += (_, _) =>
		{
			status.Text = Probe("Runtime", () => GetRuntimeDiagnostics(page));
		};

		var screenshotButton = new Button
		{
			AutomationId = "RuntimeScreenshotCapture",
			Text = "Capture MAUI screenshot",
		};
		screenshotButton.Clicked += (_, _) => RunSerializedButtonAction(
			screenshotButton,
			status,
			() => ProbeAsync("Screenshot", () => RunScreenshotProbeAsync(page)),
			"Screenshot");

		var showSoftInputButton = new Button
		{
			AutomationId = "RuntimeSoftInputShow",
			Text = "Show soft input",
		};
		showSoftInputButton.Clicked += (_, _) => RunSerializedButtonAction(
			showSoftInputButton,
			status,
			() => ProbeAsync(
				"Soft input",
				() => RunSoftInputProbeAsync(entry, show: true)),
			"Soft input");

		var hideSoftInputButton = new Button
		{
			AutomationId = "RuntimeSoftInputHide",
			Text = "Hide soft input",
		};
		hideSoftInputButton.Clicked += (_, _) => RunSerializedButtonAction(
			hideSoftInputButton,
			status,
			() => ProbeAsync(
				"Soft input",
				() => RunSoftInputProbeAsync(entry, show: false)),
			"Soft input");

		var flowDirectionButton = new Button
		{
			AutomationId = "RuntimeFlowDirectionToggle",
			Text = "Toggle RTL/LTR",
		};
		flowDirectionButton.Clicked += (_, _) =>
		{
			page.FlowDirection = page.FlowDirection == FlowDirection.RightToLeft
				? FlowDirection.LeftToRight
				: FlowDirection.RightToLeft;
			status.Text = Probe("Runtime", () => GetRuntimeDiagnostics(page));
		};

		var themeButton = new Button
		{
			AutomationId = "RuntimeThemeToggle",
			Text = "Toggle light/dark theme",
		};
		themeButton.Clicked += (_, _) =>
		{
			var application = Application.Current;
			if (application is null)
			{
				status.Text = "Runtime: failed (Application.Current is unavailable.)";
				return;
			}

			application.UserAppTheme = application.UserAppTheme == AppTheme.Dark
				? AppTheme.Light
				: AppTheme.Dark;
			status.Text = Probe("Runtime", () => GetRuntimeDiagnostics(page));
		};

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label
				{
					FontAttributes = FontAttributes.Bold,
					Text = "MAUI runtime diagnostics",
				},
				refreshButton,
				screenshotButton,
				showSoftInputButton,
				hideSoftInputButton,
				flowDirectionButton,
				themeButton,
				status,
			},
		};
	}

	static async Task<string> RunScreenshotProbeAsync(MainPage page)
	{
		var screenshot = await page.CaptureAsync();
		if (screenshot is null)
			throw new FeatureNotSupportedException("The Uno screenshot capture hook is unavailable.");

		using var stream = await screenshot.OpenReadAsync(ScreenshotFormat.Png);
		var signature = new byte[8];
		var bytesRead = await stream.ReadAsync(signature.AsMemory());
		var isPng = bytesRead == signature.Length &&
			signature[0] == 0x89 &&
			signature.AsSpan(1).SequenceEqual("PNG\r\n\x1a\n"u8);

		return isPng
			? $"{screenshot.Width}x{screenshot.Height}; valid PNG stream"
			: $"{screenshot.Width}x{screenshot.Height}; invalid PNG stream";
	}

	static async Task<string> RunSoftInputProbeAsync(Entry entry, bool show)
	{
		var requested = show
			? await entry.ShowSoftInputAsync(CancellationToken.None)
			: await entry.HideSoftInputAsync(CancellationToken.None);

		var showing = await entry.Dispatcher.DispatchAsync(entry.IsSoftInputShowing);
		for (var attempt = 0; requested && showing != show && attempt < 20; attempt++)
		{
			await Task.Delay(100);
			showing = await entry.Dispatcher.DispatchAsync(entry.IsSoftInputShowing);
		}

		return $"request={requested}; showing={showing}";
	}

	static string GetRuntimeDiagnostics(MainPage page)
	{
		var platformWindow =
			Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow;
		var xamlRoot = (platformWindow?.Content as Microsoft.UI.Xaml.FrameworkElement)?.XamlRoot;
		var xamlRootText = xamlRoot is null
			? "unavailable"
			: $"{xamlRoot.Size.Width:0}x{xamlRoot.Size.Height:0} @ {xamlRoot.RasterizationScale:0.##}x";
		var windowHandle = platformWindow?.WindowHandle ?? IntPtr.Zero;
		var settingsSupport = OperatingSystem.IsWindows() ? "available" : "unsupported";

		return string.Join(
			Environment.NewLine,
			$"Device: {DeviceInfo.Platform}; {DeviceInfo.Idiom}; {DeviceInfo.DeviceType}; {DeviceInfo.Model}; {DeviceInfo.VersionString}",
			$"XamlRoot: {xamlRootText}; HWND: 0x{windowHandle.ToInt64():X}",
			$"Theme: {Application.Current?.UserAppTheme}; FlowDirection: {page.FlowDirection}; App settings: {settingsSupport}");
	}

	static View CreateClipProbe()
	{
		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label
				{
					FontAttributes = FontAttributes.Bold,
					Text = "MAUI rounded clip projection",
				},
				new Border
				{
					AutomationId = "RoundedClipProbe",
					HeightRequest = 140,
					Stroke = Microsoft.Maui.Graphics.Colors.DarkBlue,
					StrokeShape = new RoundRectangle
					{
						CornerRadius = new CornerRadius(56, 8, 8, 56),
					},
					StrokeThickness = 4,
					Content = new Grid
					{
						BackgroundColor = Microsoft.Maui.Graphics.Colors.DarkOrange,
						Children =
						{
							new Label
							{
								HorizontalOptions = LayoutOptions.Center,
								VerticalOptions = LayoutOptions.Center,
								Text = "Per-corner rounded content clip",
								TextColor = Microsoft.Maui.Graphics.Colors.White,
							},
						},
					},
				},
			},
		};
	}

	static View CreateWindowOperationsProbe()
	{
		var constraintsEnabled = false;
		var status = new Label
		{
			AutomationId = "WindowOperationsStatus",
			Text = "Window operations not run.",
		};

		var constraintsButton = new Button
		{
			AutomationId = "WindowConstraintsToggle",
			Text = "Enable window size constraints",
		};
		constraintsButton.Clicked += (_, _) =>
		{
			if (!TryGetWindowPresenter(out var window, out var presenter))
			{
				status.Text = "Overlapped window presenter unavailable.";
				return;
			}

			constraintsEnabled = !constraintsEnabled;
			window.MinimumWidth = constraintsEnabled ? 800 : double.NaN;
			window.MinimumHeight = constraintsEnabled ? 600 : double.NaN;
			window.MaximumWidth = constraintsEnabled ? 1600 : double.PositiveInfinity;
			window.MaximumHeight = constraintsEnabled ? 1200 : double.PositiveInfinity;

			constraintsButton.Text = constraintsEnabled
				? "Disable window size constraints"
				: "Enable window size constraints";
			status.Text = constraintsEnabled
				? $"Constraints: {presenter.PreferredMinimumWidth}x{presenter.PreferredMinimumHeight} to {presenter.PreferredMaximumWidth}x{presenter.PreferredMaximumHeight}"
				: "Window size constraints disabled.";
		};

		var stateButton = new Button
		{
			AutomationId = "WindowStateToggle",
			Text = "Maximize window",
		};
		stateButton.Clicked += (_, _) =>
		{
			if (!TryGetWindowPresenter(out _, out var presenter))
			{
				status.Text = "Overlapped window presenter unavailable.";
				return;
			}

			if (OperatingSystem.IsLinux())
			{
				status.Text = "X11 maximize and restore require the pending Uno presenter fix.";
				return;
			}

			if (presenter.State == OverlappedPresenterState.Maximized)
			{
				presenter.Restore();
				stateButton.Text = "Maximize window";
				status.Text = "Window restore requested.";
			}
			else
			{
				presenter.Maximize();
				stateButton.Text = "Restore window";
				status.Text = "Window maximize requested.";
			}
		};

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label
				{
					FontAttributes = FontAttributes.Bold,
					Text = "MAUI window operations",
				},
				constraintsButton,
				stateButton,
				status,
			},
		};
	}

	static bool TryGetWindowPresenter(
		[NotNullWhen(true)] out Window? window,
		[NotNullWhen(true)] out OverlappedPresenter? presenter)
	{
		window = Application.Current?.Windows.FirstOrDefault();
		presenter =
			(window?.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?
			.AppWindow?
			.Presenter as OverlappedPresenter;
		return window is not null && presenter is not null;
	}

	static View CreateEssentialsProbe()
	{
		var results = new Label
		{
			AutomationId = "EssentialsProbeResults",
			Text = "Probe not run.",
		};

		var runButton = new Button
		{
			AutomationId = "EssentialsProbeRun",
			Text = "Run Essentials compatibility probe",
		};
		runButton.Clicked += (_, _) => RunSerializedButtonAction(
			runButton,
			results,
			RunEssentialsProbeAsync,
			"Essentials");

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label
				{
					FontAttributes = FontAttributes.Bold,
					Text = "MAUI Essentials portability",
				},
				runButton,
				results,
			},
		};
	}

	static void RunSerializedButtonAction(
		Button button,
		Label status,
		Func<Task<string>> action,
		string failureName)
	{
		if (button.Dispatcher.IsDispatchRequired)
		{
			if (!button.Dispatcher.Dispatch(() =>
				RunSerializedButtonAction(button, status, action, failureName)))
			{
				System.Diagnostics.Debug.WriteLine(
					$"Unable to dispatch the {failureName} probe.");
			}

			return;
		}

		if (!button.IsEnabled)
			return;

		button.IsEnabled = false;
		_ = CompleteSerializedButtonActionAsync(button, status, action, failureName);
	}

	static async Task CompleteSerializedButtonActionAsync(
		Button button,
		Label status,
		Func<Task<string>> action,
		string failureName)
	{
		string result;
		try
		{
			result = await action().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			result = $"{failureName}: failed ({ex.Message})";
		}

		if (!button.Dispatcher.Dispatch(() =>
		{
			try
			{
				button.IsEnabled = true;
				status.Text = result;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(
					$"Unable to update the {failureName} probe UI: {ex}");
			}
		}))
		{
			System.Diagnostics.Debug.WriteLine(
				$"Unable to dispatch the {failureName} probe result.");
		}
	}

	static async Task<string> RunEssentialsProbeAsync()
	{
		var clipboard = await ProbeAsync("Clipboard", RunClipboardProbeAsync);
		var launcher = await ProbeAsync("Launcher", RunLauncherProbeAsync);
		var fileSystem = await ProbeAsync("FileSystem", RunFileSystemProbeAsync);
		var secureStorage = await ProbeAsync("SecureStorage", RunSecureStorageProbeAsync);

		return string.Join(
			Environment.NewLine,
			Probe(
				"AppInfo",
				() => $"{AppInfo.Name} {AppInfo.VersionString} (build {AppInfo.BuildString}; {AppInfo.PackagingModel})"),
			clipboard,
			launcher,
			Probe("Connectivity", RunConnectivityProbe),
			Probe("Preferences", RunPreferencesProbe),
			Probe("MainThread", () => MainThread.IsMainThread ? "current callback is on the main thread" : "dispatcher active; current callback requires dispatch"),
			Probe("DeviceDisplay", RunDeviceDisplayProbe),
			Probe("DeviceInfo", () =>
				DeviceInfo.Platform == DevicePlatform.Unknown && DeviceInfo.Idiom == DeviceIdiom.Unknown
					? "portable fallback (Unknown)"
					: $"{DeviceInfo.Platform}; {DeviceInfo.Idiom}"),
			fileSystem,
			secureStorage,
			"Permissions: not yet adapted");
	}

	static async Task<string> RunLauncherProbeAsync()
	{
		var supported = await Launcher.CanOpenAsync("https://example.com/");
		return supported
			? "HTTPS URI launching available"
			: "HTTPS URI launching unavailable";
	}

	static string RunDeviceDisplayProbe()
	{
		var display = DeviceDisplay.MainDisplayInfo;
		return $"{display.Width:0}x{display.Height:0} @ {display.Density:0.##}x; {display.Orientation}; {display.Rotation}";
	}

	static async Task<string> RunClipboardProbeAsync()
	{
		if (OperatingSystem.IsBrowser())
		{
			_ = await Clipboard.GetTextAsync();
			return "unexpected browser clipboard support";
		}

		if (!Clipboard.HasText)
			return "empty";

		var text = await Clipboard.GetTextAsync();
		return text is null ? "empty" : $"text available ({text.Length} characters)";
	}

	static string RunPreferencesProbe()
	{
		const string firstContainer = "a";
		const string firstKey = "_b";
		const string secondContainer = "a_";
		const string secondKey = "b";

		Preferences.Set(firstKey, 41, firstContainer);
		Preferences.Set(secondKey, 42, secondContainer);
		var firstValue = Preferences.Get(firstKey, -1, firstContainer);
		var secondValue = Preferences.Get(secondKey, -1, secondContainer);

		Preferences.Clear(firstContainer);
		var secondValueAfterFirstClear = Preferences.Get(secondKey, -1, secondContainer);
		Preferences.Clear(secondContainer);

		return firstValue == 41 && secondValue == 42 && secondValueAfterFirstClear == 42
			? "round-trip and container isolation passed"
			: $"unexpected values: {firstValue}, {secondValue}, {secondValueAfterFirstClear}";
	}

	static string RunConnectivityProbe()
	{
		var profiles = string.Join(", ", Connectivity.ConnectionProfiles);
		var notifications = "listener available";

		static void ConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
		{
		}

		try
		{
			Connectivity.ConnectivityChanged += ConnectivityChanged;
			Connectivity.ConnectivityChanged -= ConnectivityChanged;
		}
		catch (FeatureNotSupportedException ex)
		{
			notifications = $"listener unsupported ({ex.Message})";
		}
		catch (NotImplementedException ex)
		{
			notifications = $"listener unsupported ({ex.Message})";
		}
		catch (PlatformNotSupportedException ex)
		{
			notifications = $"listener unsupported ({ex.Message})";
		}

		return $"{Connectivity.NetworkAccess}; {(string.IsNullOrEmpty(profiles) ? "no active profile" : profiles)}; {notifications}";
	}

	static async Task<string> RunSecureStorageProbeAsync()
	{
		try
		{
			SecureStorage.Remove(SecureStorageProbeKey);
			await SecureStorage.SetAsync(SecureStorageProbeKey, SecureStorageProbeValue);
			var storedValue = await SecureStorage.GetAsync(SecureStorageProbeKey);
			var removed = SecureStorage.Remove(SecureStorageProbeKey);
			var removedValue = await SecureStorage.GetAsync(SecureStorageProbeKey);

			return storedValue == SecureStorageProbeValue && removed && removedValue is null
				? "round-trip and removal passed"
				: $"unexpected values: {storedValue ?? "<null>"}, removed={removed}, after remove={removedValue ?? "<null>"}";
		}
		finally
		{
			try
			{
				SecureStorage.Remove(SecureStorageProbeKey);
			}
			catch (FeatureNotSupportedException)
			{
			}
			catch (NotImplementedException)
			{
			}
		}
	}

	static string Probe(string name, Func<string> probe)
	{
		try
		{
			return $"{name}: {probe()}";
		}
		catch (FeatureNotSupportedException ex)
		{
			return $"{name}: unsupported ({ex.Message})";
		}
		catch (NotImplementedException ex)
		{
			return $"{name}: unsupported ({ex.Message})";
		}
		catch (NotSupportedException ex)
		{
			return $"{name}: unsupported ({ex.Message})";
		}
		catch (DllNotFoundException ex)
		{
			return $"{name}: unsupported ({ex.Message})";
		}
		catch (InvalidOperationException ex)
		{
			return $"{name}: failed ({ex.Message})";
		}
		catch (PermissionException ex)
		{
			return $"{name}: failed ({ex.Message})";
		}
		catch (UnauthorizedAccessException ex)
		{
			return $"{name}: failed ({ex.Message})";
		}
	}

	static async Task<string> ProbeAsync(string name, Func<Task<string>> probe)
	{
		try
		{
			return $"{name}: {await probe()}";
		}
		catch (FeatureNotSupportedException ex)
		{
			return $"{name}: unsupported ({ex.Message})";
		}
		catch (NotImplementedException ex)
		{
			return $"{name}: unsupported ({ex.Message})";
		}
		catch (NotSupportedException ex)
		{
			return $"{name}: unsupported ({ex.Message})";
		}
		catch (DllNotFoundException ex)
		{
			return $"{name}: unsupported ({ex.Message})";
		}
		catch (FileNotFoundException ex)
		{
			return $"{name}: failed ({ex.Message})";
		}
		catch (IOException ex)
		{
			return $"{name}: failed ({ex.Message})";
		}
		catch (InvalidOperationException ex)
		{
			return $"{name}: failed ({ex.Message})";
		}
		catch (PermissionException ex)
		{
			return $"{name}: failed ({ex.Message})";
		}
		catch (UnauthorizedAccessException ex)
		{
			return $"{name}: failed ({ex.Message})";
		}
	}

	static async Task<string> RunFileSystemProbeAsync()
	{
		await ApplicationData.Current.LocalFolder.CreateFolderAsync("FileSystemProbe", CreationCollisionOption.OpenIfExists);

		var localPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, $"FileSystemProbe_{Guid.NewGuid():N}.txt");
		await File.WriteAllTextAsync(localPath, FileSystemProbeLocalContents);

		try
		{
			using var localStream = await new FileResult(localPath).OpenReadAsync();
			using var localReader = new StreamReader(localStream);
			var localContents = await localReader.ReadToEndAsync();

			using var packageStream = await FileSystem.OpenAppPackageFileAsync(FileSystemProbeAssetName);
			using var packageReader = new StreamReader(packageStream);
			var packageContents = (await packageReader.ReadToEndAsync()).TrimEnd();

			var packageExists = await FileSystem.AppPackageFileExistsAsync(FileSystemProbeAssetName);
			var missingExists = await FileSystem.AppPackageFileExistsAsync("MissingFile.txt");
			var traversalExists = await FileSystem.AppPackageFileExistsAsync("../" + FileSystemProbeAssetName);

			return localContents == FileSystemProbeLocalContents &&
				packageContents == FileSystemProbeAssetContents &&
				packageExists &&
				!missingExists &&
				!traversalExists
				? "local file round-trip, package asset access, and traversal guard passed"
				: $"unexpected file system values: local={localContents}, package={packageContents}, exists={packageExists}, missing={missingExists}, traversal={traversalExists}";
		}
		finally
		{
			if (File.Exists(localPath))
				File.Delete(localPath);
		}
	}

	[DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Expander))]
	[UnconditionalSuppressMessage(
		"Trimming",
		"IL2026",
		Justification = "The Expander's public IsExpanded property is explicitly preserved for its string-based binding.")]
	static Expander CreateToolkitExpander()
	{
		var header = new Button
		{
			AutomationId = "ToolkitExpanderToggle",
			FontAttributes = FontAttributes.Bold,
			Text = "Collapse CommunityToolkit.Maui Expander",
		};

		var expander = new Expander
		{
			AutomationId = "ToolkitExpander",
			Content = new Label
			{
				AutomationId = "ToolkitExpanderContent",
				Padding = new Thickness(16, 8),
				Text = "This package-only composite control is running through MAUI's WinUI handlers on Uno.",
			},
			Header = header,
			IsExpanded = true,
		};
		// Button.Clicked is the accessible toggle; remove Expander's attached tap gesture to avoid duplicate activation.
		header.GestureRecognizers.Clear();
		header.Clicked += (_, _) =>
		{
			expander.IsExpanded = !expander.IsExpanded;
			header.Text = expander.IsExpanded
				? "Collapse CommunityToolkit.Maui Expander"
				: "Expand CommunityToolkit.Maui Expander";
		};

		return expander;
	}

	static View CreateToolkitDrawingView()
	{
		var status = new Label
		{
			AutomationId = "ToolkitDrawingViewStatus",
			Text = "Draw with a mouse, pen, or touch.",
		};

		var drawingView = new DrawingView
		{
			AutomationId = "ToolkitDrawingView",
			BackgroundColor = Microsoft.Maui.Graphics.Colors.White,
			HeightRequest = 180,
			IsMultiLineModeEnabled = true,
			LineColor = Microsoft.Maui.Graphics.Colors.DarkBlue,
			LineWidth = 4,
		};
		drawingView.DrawingLineCompleted += (_, _) =>
		{
			status.Text = $"Drawing lines: {drawingView.Lines.Count}";
		};

		var clearButton = new Button
		{
			AutomationId = "ToolkitDrawingViewClear",
			Text = "Clear drawing",
		};
		clearButton.Clicked += (_, _) =>
		{
			drawingView.Clear();
			status.Text = "Draw with a mouse, pen, or touch.";
		};

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label
				{
					FontAttributes = FontAttributes.Bold,
					Text = "CommunityToolkit.Maui DrawingView",
				},
				drawingView,
				status,
				clearButton,
			},
		};
	}
}
