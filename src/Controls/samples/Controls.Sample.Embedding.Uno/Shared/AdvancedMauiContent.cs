using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Animations;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Layouts;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Maui.Controls.Sample.Uno;

/// <summary>An item rendered by the templated collection controls in the gallery.</summary>
/// <remarks>
/// Public and top level so MAUI's typed binding interceptor can name it. The gallery deliberately avoids
/// string-path bindings, which carry <c>RequiresUnreferencedCode</c> and fail a trimmed publish.
/// </remarks>
public sealed class DemoItem
{
	public DemoItem(string title, string subtitle, Color accent)
	{
		Title = title;
		Subtitle = subtitle;
		Accent = accent;
		AccentBrush = new SolidColorBrush(accent);
	}

	public string Title { get; }

	public string Subtitle { get; }

	public Color Accent { get; }

	/// <summary>Gets the accent as a brush, so templates can bind without a conversion in the getter.</summary>
	/// <remarks>
	/// MAUI's typed binding interceptor only accepts property access, indexing and casts, so a template
	/// cannot construct the brush inline.
	/// </remarks>
	public Brush AccentBrush { get; }
}

/// <summary>A drawing that exercises <c>Microsoft.Maui.Graphics</c> rather than the handler pipeline.</summary>
sealed class DemoDrawable : IDrawable
{
	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		canvas.SaveState();

		canvas.FillColor = Color.FromArgb("#512BD4");
		canvas.FillRoundedRectangle(dirtyRect, 10);

		canvas.StrokeColor = Colors.White;
		canvas.StrokeSize = 3;

		for (var i = 0; i < 4; i++)
		{
			var inset = 8 + (i * 14);
			canvas.DrawEllipse(inset, inset, dirtyRect.Width - (inset * 2), dirtyRect.Height - (inset * 2));
		}

		canvas.FontColor = Colors.White;
		canvas.FontSize = 13;
		canvas.DrawString(
			"GraphicsView + IDrawable",
			0,
			dirtyRect.Height - 22,
			dirtyRect.Width,
			18,
			HorizontalAlignment.Center,
			VerticalAlignment.Center);

		canvas.RestoreState();
	}
}

/// <summary>
/// A gallery of the more demanding .NET MAUI controls, used to map what actually survives the trip through
/// Uno's renderer on platforms MAUI does not support.
/// </summary>
/// <remarks>
/// Every showcased control is registered in <see cref="ShowcasedControls"/> so that
/// <see cref="ControlCensus"/> can assert on the realized platform view rather than on the fact that
/// construction did not throw. Templated and virtualizing controls are given an explicit height, because a
/// self-sizing scrolling control nested in a stack has no bounded height to measure against.
/// </remarks>
public sealed class AdvancedMauiContent : ContentView
{
	readonly List<(string Name, VisualElement Element, bool ExpectsItems)> _showcased = new();
	readonly ObservableCollection<DemoItem> _items = new();
	readonly Label _eventLog;
	int _refreshCount;

	public AdvancedMauiContent()
		: this(int.MaxValue, DefaultSkippedCards)
	{
	}

	/// <summary>
	/// Builds the gallery with only the first <paramref name="cardLimit"/> cards, omitting any card whose
	/// key is in <paramref name="skippedCards"/>.
	/// </summary>
	/// <remarks>
	/// A layout that never settles takes the whole app down without an exception, so the gallery is
	/// triageable at runtime: one build, then one short run per card count or per suspect.
	/// </remarks>
	public AdvancedMauiContent(int cardLimit, IReadOnlyCollection<string>? skippedCards = null)
	{
		var skipped = new HashSet<string>(skippedCards ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

		for (var i = 1; i <= 6; i++)
		{
			_items.Add(new DemoItem($"Item {i}", $"Templated row {i}, bound with a typed binding", Accent(i)));
		}

		_eventLog = new Label
		{
			Text = "Interaction log: (nothing yet)",
			FontSize = 12,
			Opacity = 0.75,
		};

		var cards = new (string Key, string Title, Func<View> Build)[]
		{
			("CollectionView", "CollectionView", BuildCollectionView),
			("CarouselView", "CarouselView + IndicatorView", BuildCarousel),
			("RefreshView", "RefreshView", BuildRefreshView),
			("SwipeView", "SwipeView", BuildSwipeView),
			(GraphicsViewCard, "GraphicsView (Microsoft.Maui.Graphics)", BuildGraphicsView),
			("Shapes", "Shapes and gradient brushes", BuildShapes),
			("Inputs", "Input controls", BuildInputs),
			("Layouts", "FlexLayout and AbsoluteLayout", BuildLayouts),
			("Gestures", "Gestures and animation", BuildGesturesAndAnimation),
			("ThirdParty", "Third party: CommunityToolkit.Maui layouts", BuildThirdPartyControls),
			("ThirdPartyBehaviors", "Third party: CommunityToolkit.Maui behaviours and converters", BuildThirdPartyBehaviors),
		};

		var layout = new VerticalStackLayout
		{
			Spacing = 12,
			Padding = new Thickness(12),
		};

		for (var i = 0; i < cards.Length && i < cardLimit; i++)
		{
			if (skipped.Contains(cards[i].Key))
			{
				SkippedCardKeys.Add(cards[i].Key);
				continue;
			}

			layout.Children.Add(Card(cards[i].Title, cards[i].Build()));
		}

		if (SkippedCardKeys.Count > 0)
		{
			layout.Children.Add(new Label
			{
				Text = $"Omitted: {string.Join(", ", SkippedCardKeys)} — see the sample README.",
				FontSize = 11,
				Opacity = 0.7,
			});
		}

		layout.Children.Add(_eventLog);

		Content = layout;
	}

	/// <summary>The key of the card that is omitted by default.</summary>
	public const string GraphicsViewCard = "GraphicsView";

	/// <summary>
	/// Cards omitted by default because they do not currently survive this stack.
	/// </summary>
	/// <remarks>
	/// <c>GraphicsView</c> puts the layout into a loop that never settles: no exception is raised, the UI
	/// thread simply never finishes a pass, and the working set climbs without bound until the process is
	/// killed. Because a hung layout takes the whole app with it, it cannot be left in a demo gallery.
	/// Re-enable it with <c>MAUI_UNO_GALLERY_SKIP=</c> (empty) to reproduce.
	/// </remarks>
	public static IReadOnlyCollection<string> DefaultSkippedCards { get; } = new[] { GraphicsViewCard };

	/// <summary>Gets the keys of the cards that were not built.</summary>
	public List<string> SkippedCardKeys { get; } = new();

	/// <summary>Gets the controls the census asserts on, in display order.</summary>
	public IReadOnlyList<(string Name, VisualElement Element, bool ExpectsItems)> ShowcasedControls => _showcased;

	static Color Accent(int index) => (index % 3) switch
	{
		0 => Color.FromArgb("#512BD4"),
		1 => Color.FromArgb("#2B8A3E"),
		_ => Color.FromArgb("#C2255C"),
	};

	/// <summary>Registers a control for the census.</summary>
	/// <param name="expectsItems">
	/// Set for controls that must materialize templated children. A container can report a perfectly
	/// healthy size while rendering nothing at all inside it, so those controls need the stronger check.
	/// </param>
	T Track<T>(string name, T element, bool expectsItems = false)
		where T : VisualElement
	{
		_showcased.Add((name, element, expectsItems));
		return element;
	}

	static View Card(string title, View content) =>
		new Border
		{
			Stroke = new SolidColorBrush(Color.FromArgb("#40808080")),
			StrokeThickness = 1,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
			Padding = new Thickness(10),
			Content = new VerticalStackLayout
			{
				Spacing = 6,
				Children =
				{
					new Label
					{
						Text = title,
						FontAttributes = FontAttributes.Bold,
						FontSize = 14,
					},
					content,
				},
			},
		};

	View BuildCollectionView()
	{
		var collectionView = Track("CollectionView", expectsItems: true, element: new CollectionView
		{
			HeightRequest = 150,
			ItemsSource = _items,
			SelectionMode = SelectionMode.Single,
			ItemTemplate = new DataTemplate(() =>
			{
				var swatch = new BoxView { WidthRequest = 6, HeightRequest = 30, CornerRadius = 3 };
				swatch.SetBinding(BoxView.ColorProperty, static (DemoItem item) => item.Accent);

				var title = new Label { FontAttributes = FontAttributes.Bold, FontSize = 13 };
				title.SetBinding(Label.TextProperty, static (DemoItem item) => item.Title);

				var subtitle = new Label { FontSize = 11, Opacity = 0.7 };
				subtitle.SetBinding(Label.TextProperty, static (DemoItem item) => item.Subtitle);

				return new HorizontalStackLayout
				{
					Spacing = 8,
					Padding = new Thickness(2, 4),
					Children =
					{
						swatch,
						new VerticalStackLayout { Children = { title, subtitle } },
					},
				};
			}),
		});

		collectionView.SelectionChanged += (_, args) =>
			Log(args.CurrentSelection.Count > 0 && args.CurrentSelection[0] is DemoItem item
				? $"CollectionView selected {item.Title}"
				: "CollectionView selection cleared");

		return collectionView;
	}

	View BuildCarousel()
	{
		var carousel = Track("CarouselView", expectsItems: true, element: new CarouselView
		{
			HeightRequest = 110,
			ItemsSource = _items,
			Loop = false,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label
				{
					FontSize = 16,
					HorizontalOptions = LayoutOptions.Center,
					VerticalOptions = LayoutOptions.Center,
					TextColor = Colors.White,
				};
				label.SetBinding(Label.TextProperty, static (DemoItem item) => item.Title);

				var border = new Border
				{
					Margin = new Thickness(6),
					StrokeThickness = 0,
					StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
					Content = label,
				};
				border.SetBinding(Border.BackgroundProperty, static (DemoItem item) => item.AccentBrush);

				return border;
			}),
		});

		var indicator = Track("IndicatorView", new IndicatorView
		{
			HorizontalOptions = LayoutOptions.Center,
			IndicatorColor = Color.FromArgb("#40808080"),
			SelectedIndicatorColor = Color.FromArgb("#512BD4"),
		});

		carousel.IndicatorView = indicator;

		return new VerticalStackLayout { Spacing = 4, Children = { carousel, indicator } };
	}

	View BuildRefreshView()
	{
		var status = new Label { Text = "Pull down inside the list to refresh.", FontSize = 12 };

		var list = new CollectionView
		{
			ItemsSource = _items,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { FontSize = 12, Padding = new Thickness(4, 2) };
				label.SetBinding(Label.TextProperty, static (DemoItem item) => item.Title);
				return label;
			}),
		};

		var refreshView = Track("RefreshView", expectsItems: true, element: new RefreshView
		{
			HeightRequest = 120,
			Content = list,
		});

		refreshView.Refreshing += async (_, _) =>
		{
			_refreshCount++;
			status.Text = $"Refreshed {_refreshCount}x";
			Log($"RefreshView refreshed ({_refreshCount})");
			await Task.Delay(600);
			refreshView.IsRefreshing = false;
		};

		return new VerticalStackLayout { Spacing = 4, Children = { status, refreshView } };
	}

	View BuildSwipeView()
	{
		var swipeView = Track("SwipeView", new SwipeView
		{
			HeightRequest = 60,
			LeftItems = new SwipeItems
			{
				new SwipeItem
				{
					Text = "Archive",
					BackgroundColor = Color.FromArgb("#2B8A3E"),
					Command = new Command(() => Log("SwipeView archive invoked")),
				},
			},
			Content = new Border
			{
				BackgroundColor = Color.FromArgb("#20512BD4"),
				StrokeThickness = 0,
				StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
				Content = new Label
				{
					Text = "Swipe me from the left edge",
					FontSize = 12,
					Margin = new Thickness(8),
					VerticalOptions = LayoutOptions.Center,
				},
			},
		});

		return swipeView;
	}

	View BuildGraphicsView() =>
		Track("GraphicsView", new GraphicsView
		{
			HeightRequest = 120,
			Drawable = new DemoDrawable(),
		});

	View BuildShapes()
	{
		var gradient = new LinearGradientBrush
		{
			StartPoint = new Point(0, 0),
			EndPoint = new Point(1, 1),
			GradientStops =
			{
				new GradientStop { Color = Color.FromArgb("#512BD4"), Offset = 0f },
				new GradientStop { Color = Color.FromArgb("#C2255C"), Offset = 1f },
			},
		};

		var ellipse = Track("Ellipse", new Ellipse
		{
			WidthRequest = 70,
			HeightRequest = 70,
			Fill = gradient,
		});

		var polygon = Track("Polygon", new Polygon
		{
			WidthRequest = 70,
			HeightRequest = 70,
			Fill = new SolidColorBrush(Color.FromArgb("#2B8A3E")),
			Stroke = new SolidColorBrush(Colors.White),
			StrokeThickness = 2,
			Points = new PointCollection
			{
				new Point(35, 0),
				new Point(70, 26),
				new Point(56, 68),
				new Point(14, 68),
				new Point(0, 26),
			},
		});

		var roundedBorder = Track("Border with gradient stroke", new Border
		{
			WidthRequest = 110,
			HeightRequest = 70,
			Stroke = gradient,
			StrokeThickness = 4,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18, 4, 18, 4) },
			Content = new Label
			{
				Text = "Asymmetric\ncorners",
				FontSize = 11,
				HorizontalTextAlignment = TextAlignment.Center,
				VerticalOptions = LayoutOptions.Center,
			},
		});

		return new HorizontalStackLayout
		{
			Spacing = 10,
			Children = { ellipse, polygon, roundedBorder },
		};
	}

	View BuildInputs()
	{
		var editor = Track("Editor", new Editor
		{
			Placeholder = "Editor: multi-line text",
			HeightRequest = 54,
		});

		var searchBar = Track("SearchBar", new SearchBar { Placeholder = "SearchBar" });
		searchBar.SearchButtonPressed += (_, _) => Log($"SearchBar searched for '{searchBar.Text}'");

		var picker = Track("Picker", new Picker { Title = "Picker" });
		foreach (var item in _items)
		{
			picker.Items.Add(item.Title);
		}

		picker.SelectedIndexChanged += (_, _) => Log($"Picker selected index {picker.SelectedIndex}");

		var datePicker = Track("DatePicker", new DatePicker());
		var timePicker = Track("TimePicker", new TimePicker());
		var stepper = Track("Stepper", new Stepper { Minimum = 0, Maximum = 10, Increment = 1 });
		var mauiSwitch = Track("Switch", new Switch { IsToggled = true });
		var checkBox = Track("CheckBox", new CheckBox { IsChecked = true });
		var radioButton = Track("RadioButton", new RadioButton { Content = "RadioButton", IsChecked = true });
		var activity = Track("ActivityIndicator", new ActivityIndicator { IsRunning = true });
		var progress = Track("ProgressBar", new ProgressBar { Progress = 0.6, WidthRequest = 120 });

		stepper.ValueChanged += (_, args) => Log($"Stepper value {args.NewValue}");
		mauiSwitch.Toggled += (_, args) => Log($"Switch toggled {args.Value}");

		return new VerticalStackLayout
		{
			Spacing = 6,
			Children =
			{
				editor,
				searchBar,
				new HorizontalStackLayout { Spacing = 8, Children = { picker, datePicker, timePicker } },
				new HorizontalStackLayout { Spacing = 8, Children = { stepper, mauiSwitch, checkBox, radioButton } },
				new HorizontalStackLayout { Spacing = 8, Children = { activity, progress } },
			},
		};
	}

	View BuildLayouts()
	{
		var flex = Track("FlexLayout", new FlexLayout
		{
			Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
			JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
			HeightRequest = 62,
		});

		foreach (var item in _items)
		{
			flex.Children.Add(new Border
			{
				Margin = new Thickness(3),
				Padding = new Thickness(8, 4),
				BackgroundColor = item.Accent,
				StrokeThickness = 0,
				StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
				Content = new Label { Text = item.Title, FontSize = 11, TextColor = Colors.White },
			});
		}

		var absolute = Track("AbsoluteLayout", new AbsoluteLayout { HeightRequest = 70 });

		for (var i = 0; i < 3; i++)
		{
			var box = new BoxView { Color = Accent(i), Opacity = 0.75, CornerRadius = 6 };
			AbsoluteLayout.SetLayoutBounds(box, new Rect(i * 26, i * 12, 70, 42));
			absolute.Children.Add(box);
		}

		return new VerticalStackLayout { Spacing = 6, Children = { flex, absolute } };
	}

	View BuildGesturesAndAnimation()
	{
		var target = Track("Gesture target", new Border
		{
			WidthRequest = 130,
			HeightRequest = 60,
			BackgroundColor = Color.FromArgb("#512BD4"),
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
			Content = new Label
			{
				Text = "Tap or pan me",
				TextColor = Colors.White,
				FontSize = 12,
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center,
			},
		});

		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) => Log("TapGestureRecognizer fired");
		target.GestureRecognizers.Add(tap);

		var pan = new PanGestureRecognizer();
		pan.PanUpdated += (_, args) => Log($"PanGestureRecognizer {args.StatusType}");
		target.GestureRecognizers.Add(pan);

		var animate = Track("Animation button", new Button { Text = "Animate", FontSize = 12 });
		animate.Clicked += async (_, _) =>
		{
			Log("Animation started");
			await target.RotateToAsync(360, 500);
			target.Rotation = 0;
			await target.FadeToAsync(0.3, 200);
			await target.FadeToAsync(1, 200);
			Log("Animation finished");
		};

		return new HorizontalStackLayout { Spacing = 10, Children = { target, animate } };
	}

	void Log(string message) => _eventLog.Text = $"Interaction log: {message}";

	/// <summary>
	/// Controls from the .NET MAUI Community Toolkit — a genuinely external library, compiled from source
	/// against this repository's neutral MAUI build rather than consumed as a NuGet package.
	/// </summary>
	View BuildThirdPartyControls()
	{
		var uniform = Track("CommunityToolkit UniformItemsLayout", new UniformItemsLayout
		{
			MaxColumns = 3,
			MaxRows = 2,
			HeightRequest = 96,
		});

		foreach (var item in _items)
		{
			uniform.Children.Add(new Border
			{
				Margin = new Thickness(3),
				BackgroundColor = item.Accent,
				StrokeThickness = 0,
				StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
				Content = new Label
				{
					Text = item.Title,
					FontSize = 11,
					TextColor = Colors.White,
					HorizontalOptions = LayoutOptions.Center,
					VerticalOptions = LayoutOptions.Center,
				},
			});
		}

		var dock = Track("CommunityToolkit DockLayout", new DockLayout
		{
			HeightRequest = 130,
			BackgroundColor = Color.FromArgb("#10808080"),
		});

		dock.Children.Add(DockedBlock("Top", Color.FromArgb("#512BD4"), DockPosition.Top, dock));
		dock.Children.Add(DockedBlock("Bottom", Color.FromArgb("#C2255C"), DockPosition.Bottom, dock));
		dock.Children.Add(DockedBlock("Left", Color.FromArgb("#2B8A3E"), DockPosition.Left, dock));
		dock.Children.Add(DockedBlock("Right", Color.FromArgb("#B36A00"), DockPosition.Right, dock));
		dock.Children.Add(DockedBlock("Fill", Color.FromArgb("#40808080"), DockPosition.None, dock));

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label { Text = "UniformItemsLayout — every cell the same size", FontSize = 11, Opacity = 0.7 },
				uniform,
				new Label { Text = "DockLayout — children docked to the edges, last one fills", FontSize = 11, Opacity = 0.7 },
				dock,
			},
		};
	}

	/// <summary>
	/// Toolkit behaviours and converters, which are the other half of what a third-party MAUI library
	/// provides: they attach to stock MAUI controls rather than being controls themselves.
	/// </summary>
	View BuildThirdPartyBehaviors()
	{
		// MaskedBehavior rewrites input as it is typed. Seeded so the converter below has something to show.
		var masked = Track("CommunityToolkit MaskedBehavior", new Entry
		{
			Placeholder = "Masked: XX-XX-XXXX",
			Text = "ab-cd-1234",
		});
		masked.Behaviors.Add(new MaskedBehavior { Mask = "XX-XX-XXXX" });

		// NumericValidationBehavior recolours the entry as the value moves in and out of range.
		var numeric = Track("CommunityToolkit NumericValidationBehavior", new Entry
		{
			Placeholder = "Numeric 1-100, invalid turns red",
			Keyboard = Keyboard.Numeric,
		});
		numeric.Behaviors.Add(new NumericValidationBehavior
		{
			MinimumValue = 1,
			MaximumValue = 100,
			MaximumDecimalPlaces = 0,
			InvalidStyle = new Style(typeof(Entry))
			{
				Setters = { new Setter { Property = Entry.TextColorProperty, Value = Colors.Red } },
			},
			ValidStyle = new Style(typeof(Entry))
			{
				Setters = { new Setter { Property = Entry.TextColorProperty, Value = Color.FromArgb("#2B8A3E") } },
			},
			Flags = ValidationFlags.ValidateOnValueChanged,
		});

		// AnimationBehavior runs a toolkit animation on tap, with no code in the event handler.
		var animated = Track("CommunityToolkit AnimationBehavior", new Border
		{
			HeightRequest = 44,
			BackgroundColor = Color.FromArgb("#512BD4"),
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
			Content = new Label
			{
				Text = "Tap to run a toolkit FadeAnimation",
				TextColor = Colors.White,
				FontSize = 12,
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center,
			},
		});
		animated.Behaviors.Add(new AnimationBehavior
		{
			AnimationType = new FadeAnimation { Opacity = 0.2, Length = 300 },
		});

		// InvertedBoolConverter driving a stock MAUI control through an ordinary binding. The switch starts
		// off so the inverted result is visible; toggling it on hides the label, which is the point.
		// Typed rather than string-path: a string path carries RequiresUnreferencedCode and fails a trimmed
		// publish, and the typed overload takes the converter just the same.
		var toggle = new Switch { IsToggled = false };
		var invertedLabel = Track("CommunityToolkit InvertedBoolConverter", new Label { FontSize = 12 });
		invertedLabel.SetBinding(
			Label.IsVisibleProperty,
			static (Switch source) => source.IsToggled,
			converter: new InvertedBoolConverter(),
			source: toggle);
		invertedLabel.Text = "Visible while the switch is off — InvertedBoolConverter";

		var caseLabel = Track("CommunityToolkit TextCaseConverter", new Label { FontSize = 12 });
		caseLabel.SetBinding(
			Label.TextProperty,
			static (Entry source) => source.Text,
			converter: new TextCaseConverter { Type = TextCaseType.Upper },
			source: masked);

		// The same switch bound straight through, so the pair shows the converter inverting and also keeps
		// the census honest: this one starts hidden, which is the case that used to be reported as a failure.
		var directLabel = Track("Bound visibility, starts hidden", new Label
		{
			FontSize = 12,
			Text = "Visible while the switch is on — no converter",
		});
		directLabel.SetBinding(Label.IsVisibleProperty, static (Switch source) => source.IsToggled, source: toggle);

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				masked,
				numeric,
				animated,
				new HorizontalStackLayout { Spacing = 8, Children = { toggle, invertedLabel } },
				directLabel,
				caseLabel,
			},
		};
	}

	static View DockedBlock(string text, Color color, DockPosition position, DockLayout owner)
	{
		var block = new Border
		{
			BackgroundColor = color,
			StrokeThickness = 0,
			Padding = new Thickness(6, 4),
			Content = new Label
			{
				Text = text,
				FontSize = 11,
				TextColor = position == DockPosition.None ? Colors.Black : Colors.White,
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center,
			},
		};

		DockLayout.SetDockPosition(block, position);

		return block;
	}
}
