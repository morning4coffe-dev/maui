using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Microsoft.UI.Windowing;

namespace Microsoft.Maui.Controls.Sample.Uno;

public sealed class MainPage : ContentPage
{
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
		runButton.Clicked += (_, _) =>
		{
			results.Text = string.Join(
				Environment.NewLine,
				Probe(
					"AppInfo",
					() => $"{AppInfo.Name} {AppInfo.VersionString} (build {AppInfo.BuildString}; {AppInfo.PackagingModel})"),
				Probe("Clipboard", () => Clipboard.HasText ? "text available" : "empty"),
				Probe("Connectivity", () => $"{Connectivity.NetworkAccess}; {string.Join(", ", Connectivity.ConnectionProfiles)}"),
				Probe("Preferences", RunPreferencesProbe),
				Probe("MainThread", () => MainThread.IsMainThread ? "current callback is on the main thread" : "dispatcher active; current callback requires dispatch"),
				Probe("DeviceInfo", () =>
					DeviceInfo.Platform == DevicePlatform.Unknown && DeviceInfo.Idiom == DeviceIdiom.Unknown
						? "portable fallback (Unknown)"
						: $"{DeviceInfo.Platform}; {DeviceInfo.Idiom}"),
				Probe("FileSystem", () => string.IsNullOrWhiteSpace(FileSystem.AppDataDirectory) ? "no app-data path" : "app-data path available"),
				"SecureStorage: not yet adapted",
				"Permissions: not yet adapted");
		};

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
