# Uno-root MAUI embedding

This sample inverts the ownership model used by `Controls.Sample.Uno`.

| | `Controls.Sample.Uno` | `Controls.Sample.Embedding.Uno` (this sample) |
| --- | --- | --- |
| Application root | MAUI (`MauiWinUIApplication`) | Uno (`Microsoft.UI.Xaml.Application`) |
| Visual tree owner | MAUI | Uno |
| MAUI content | the whole app | islands hosted by `MauiHost` |

It exists to answer one question: can this fork host **embedded** MAUI content on targets MAUI itself
does not support — WebAssembly above all?

## Why this works without a bridge

Uno's shipping MAUI embedding on Android and iOS has to bridge two unrelated native view trees. This
fork does not, because MAUI's Windows handlers are compiled against `Uno.WinUI`: a MAUI handler's
platform view **is** an Uno `UIElement`. Uno measures, arranges, renders, and routes input to it
exactly as it does for any other child.

The entire embedding surface is already present in the neutral `net10.0` assemblies, because
`UnoTargeting.props` defines `WINDOWS` and so MAUI's `#if ANDROID || IOS || MACCATALYST || WINDOWS`
embedding files compile. `Microsoft.Maui.Controls` exposes, bound to Uno types:

```csharp
IMauiContext              CreateEmbeddedWindowContext(this MauiApp, Microsoft.UI.Xaml.Window);
FrameworkElement          ToPlatformEmbedded(this IElement, IMauiContext);
```

No new MAUI API was needed for Tier 1; it is built entirely on the public embedding surface. Tier 2
required exactly one addition, described below.

## Structure

The reusable runtime lives outside the sample, in `src/Controls/src/Embedding.Uno/`
(`Microsoft.Maui.Controls.Embedding.Uno`). The sample is a consumer of it, not the owner.

| File | Role |
| --- | --- |
| `src/Controls/src/Embedding.Uno/MauiEmbeddingSession.cs` | One `MauiApp` per process, one `IMauiContext` per `Window`, exactly-once teardown |
| `src/Controls/src/Embedding.Uno/MauiHost.cs` | `ContentControl` that realizes a MAUI element into the Uno tree |
| `Shared/UnoEmbeddingApplication.cs` | Uno application root — a plain `Application`, not `MauiWinUIApplication` |
| `Shared/MainShell.cs` | Uno-owned UI; MAUI islands interleaved with Uno content |
| `Shared/MauiIslandPage.cs` | Tier 2 island: a `Page` exercising alerts and modal navigation |
| `Shared/MyMauiContent.cs` | Tier 1 island: a plain `ContentView` |
| `Shared/AdvancedMauiContent.cs` | Tier 1 island: a gallery of the more demanding MAUI controls |
| `Shared/ControlCensus.cs` | Per-control report of what actually reached the platform |
| `Shared/Tier2Probe.cs` | Code-driven verification of the window-scoped features |
| `Shared/LifecycleRegressionProbe.cs` | Modal push/pop cancellation, failed-handler rollback/retry, restored input, observable-list updates and large-grid regressions |
| `Shared/ImageDensityRegressionProbe.cs` | Opt-in slow-image density changes, source supersession and disconnect cleanup |
| `Shared/MauiProgram.cs`, `Shared/App.cs` | The embedded MAUI app |

The embedded `MauiApp` is supplied by the host, not hard-wired:

```csharp
MauiEmbeddingSession.UseMauiApp(MauiProgram.CreateMauiApp);
```

Registering the factory is cheap and safe from the Uno application's constructor; the `MauiApp` itself is
built lazily on the UI thread when the first island is realized, which is the only point at which the
embedding bootstrap's requirements are met.

## Lifetime model

This follows `Controls.Sample.Embedding`'s `Scenario3_Correct`, which is the only correct shape:

- **One `MauiApp` per process.** Created on the UI thread from `OnLaunched` — never from
  `Program.Main`, because MAUI's embedding bootstrap captures `Application.Current` while the builder
  is being configured.
- **One `IMauiContext` per native `Window`,** created once via `CreateEmbeddedWindowContext` and
  shared by every host in that window. The convenience
  `ToPlatformEmbedded(element, mauiApp, window)` overload is deliberately **not** used: it mints a
  new context *and* a new `EmbeddedWindow` in `Application.Windows` on every call.
- **`MauiHost.Unloaded` never disposes the scope.** Unloading is transient — it also happens during
  navigation, reparenting, virtualization, and template changes — and the scope is shared.
  `IWindow.Destroying()` is called exactly once, from `Window.Closed`.
- **Replacing content** unparents the previous element from the embedded window and disconnects its
  handlers; otherwise it stays rooted for the lifetime of the window.
- **Failed realization** rolls back logical parenting, the window page, navigation-root registration and
  handlers while preserving the original handler exception. The same content can then be retried.

The Android head links the same activity and DayNight/system-bar resources as
the generated SDK host via `Uno.Maui.AndroidHost.targets`; it does not maintain
a separate policy copy. API 35+ uses enforced edge-to-edge geometry, with Uno
owning insets. The shared contract also validates the API 24 minimum and the
API 27 navigation-bar resource. Application ownership remains in this sample's
`MainApplication`, so sharing the activity does not change Uno-root embedding.
The Uno-owned shell applies visible-bounds padding around its chrome on Android;
the activity does not replace Uno's inset listener or shrink the native surface.

## Supported today

### Tier 1 — view-level embedding

Any MAUI `VisualElement` hosted in the Uno tree: rendering, layout, input, property mappers, app-level
MAUI resources resolved through the logical tree, multiple hosts sharing one window context, content
replacement, and detach/re-attach.

### Tier 2 — window-level embedding

A MAUI `Page` assigned to the embedded window's `Window.Page` additionally gets the window-scoped MAUI
services. Verified working on Windows Desktop:

| Feature | Status |
| --- | --- |
| `DisplayAlertAsync` (1 and 2 button) | Works |
| `DisplayActionSheetAsync` | Works |
| `DisplayPromptAsync` | Works |
| `PushModalAsync` / `PopModalAsync` | Works — the modal is really realized and rendered |
| `PushAsync` / `PopAsync` (`NavigationPage`) | Works — the pushed page is really realized and rendered |
| Host theme → embedded content | Works — including runtime theme switches |
| Host `DataContext` → `BindingContext` | Works — MAUI bindings resolve against the host's data context |

### Host integration

`MauiHost` flows its Uno `DataContext` into the embedded element's `BindingContext`, so a MAUI binding
resolves against whatever the surrounding Uno tree provides. A null data context is only propagated once
the bridge has actually carried a value, so hosts that never set one cannot silently wipe a
`BindingContext` set on the MAUI element directly.

`MauiEmbeddingSession` forwards the host's effective theme. This is needed because MAUI's Windows theme
plumbing is a Win32 `WM_THEMECHANGE` hook that the Uno target compiles out, and embedding has no
`MauiWinUIWindow` either — so without the bridge nothing ever calls `IApplication.ThemeChanged`, the
embedded application's theme stays `Unspecified` for the life of the process, and `AppThemeBinding` never
resolves.

The theme is read from the window root's `ActualTheme` rather than from `Application.RequestedTheme`,
because Uno rejects a runtime application theme change with `NotSupportedException`; an Uno app switches
theme by setting `RequestedTheme` on a root element, which moves `ActualTheme` and leaves the application
theme untouched. The value is assigned to `UserAppTheme`, since `PlatformAppTheme` is not settable and its
only route in re-reads the application theme this bridge deliberately avoids. **Embedded content must
therefore leave `UserAppTheme` alone — the host owns the theme.**

Three things were required:

1. **`Window.Page` must be set.** `AlertManager.Subscribe()` only runs from `Window.OnPageHandlerChanged`,
   so without a page the awaited dialog task never completes. `MauiEmbeddingSession` promotes the first
   page-based island to the embedded window's page. Because `Window.Page` already parents the page, that
   path uses `ToPlatform` rather than `ToPlatformEmbedded`, which would parent it a second time.
2. **The window must report that it was created and activated.** `ModalNavigationManager` gates every
   platform push on `_firstActivated`, which a standalone app gets from `MauiWinUIWindow`. Nothing raises
   it for an embedded window, so modals stay queued in the virtual stack forever — `PushModalAsync` even
   returns successfully while nothing renders. The session raises `IWindow.Created()` and
   `IWindow.Activated()` once.
3. **Dialogs must be marshalled to the UI thread.** Uno materializes the `ContentDialog` template inside
   `ShowAsync`, which touches the dependency property system, so it is only legal on the UI thread. MAUI's
   `AlertManager` handlers are `async void`, so an off-thread request did not fail the awaited call — it
   terminated the process. This was found by clicking the button during QA, not by the probe, because the
   probe happened to request its alerts from the UI thread.

Alerts needed no MAUI change to *function*. Modals and the dialog threading fix needed the changes below.

## The MAUI changes Tier 2 required

A standalone MAUI app puts an internal `WindowRootViewContainer` in the *native window's* content, and
modal navigation locates it from there:

```csharp
WindowRootViewContainer Container =>
    _window.NativeWindow.Content as WindowRootViewContainer ??
    throw new InvalidOperationException("Root container Panel not found");
```

Under Uno-root embedding the native window's content is the Uno tree, so that cast fails. Rather than
take over the hosting window, the container is created *inside the host* and registered on the
window-scoped `MauiContext`:

- `EmbeddingExtensions.ToPlatformEmbeddedWindowRoot(page, windowContext)` (new, `#if UNO`) creates the
  container, registers it on the context, connects the `NavigationRootManager`, and returns the container
  for `MauiHost` to display.
- `ModalNavigationManager.Container` (`#if UNO`) prefers the context-registered container and falls back
  to the standalone lookup, so MAUI-root apps are unaffected.
- `AlertManager.AlertRequestHelper` (`#if UNO`) re-dispatches `OnAlertRequested`, `OnPromptRequested` and
  `OnActionSheetRequested` to the platform window's dispatcher when they are entered off the UI thread.
  The dispatcher has to come from the platform window rather than the dialog, because the dialog would
  itself have been constructed on the wrong thread and reports thread access for it.

The upshot is that modals stay inside the embedded region instead of covering the whole hosting window.
Window overlays are a separate mechanism and remain unsupported; see below.

## Still not supported

The opt-in `MauiUnoTier2Probe=true` browser build also runs lifecycle and
collection regressions after the Tier 2 scenarios. It requires real modal
unparenting/unloading, native button invocation, pending push and pop cancellation,
successful subsequent navigation, and immediate observable insertion/reset.
It then requires bounded realization for a 100,000-item grid after initial
empty or small sources, followed by failing view/page handlers and successful
retry with a working modal root. An overall `TIER2-RESULT FAIL` is not a pass even when
earlier individual scenarios succeeded.

The additional `MAUI_UNO_DENSITY_PROBE=1` environment opt-in requires a browser
driver. Start at device scale 1, then respond to the `DENSITY-REQUEST 1.5` and
`DENSITY-REQUEST 2` console markers by changing Chromium device metrics and
dispatching `resize`. The probe waits for the real `XamlRoot.RasterizationScale`
before completing each deliberately slow image request; it does not override
the renderer's density accessor. It requires the final density-2 bitmap, disposal
of superseded results, and no assignment after disconnect. Ordinary Tier 2 runs
do not require this external driver.

The tested Uno Skia/WASM core runtime supplies a stub `ItemsWrapGrid`. The probe
detects this before assigning 100,000 items (which would freeze the browser)
and fails explicitly. A real virtualizing implementation is still required;
panel selection alone does not establish large-grid support.

- **`WindowOverlay`, visual diagnostics, MAUI hot reload.** `WindowOverlay.Windows.cs` casts
  `Window.Handler as WindowHandler`, and embedding uses `EmbeddedWindowHandler`, which is an
  `IWindowHandler` but not a `WindowHandler`. It fails closed and simply disables overlays.
- **Shell.** Not attempted.
- **`CreateWindow`, `Application.Current.MainPage`, `OpenWindow`.** Embedding creates a synthetic
  `EmbeddedWindow`; `TApp.CreateWindow` is never called.
- **One window page per Uno window.** A window has exactly one `Page`, so only the first page-based
  island gets Tier 2 treatment. A second page-based island now throws rather than silently inheriting the
  first island's navigation proxy and alert manager; host further islands as views, or use another window.

## Advanced control gallery

The third island (`AdvancedMauiContent`) is a gallery of the more demanding MAUI controls, used to map what
actually survives the trip through Uno's renderer. `ControlCensus` runs on load and reports, per control,
the realized Uno platform view, its arranged size, how many descendants it realized, and how many of them
carry text. It writes to `control-census.log` and to the console, and is shown in the app.

Earlier trimmed Release results, updated with current Release WebAssembly build rendering checks.
The current graphics checks are not fresh trimmed/AOT acceptance:

| Control | Platform view | Result |
| --- | --- | --- |
| `Editor`, `SearchBar`, `Picker`, `DatePicker`, `TimePicker`, `Stepper`, `Switch`, `CheckBox`, `RadioButton`, `ProgressBar` | `TextBox`, `AutoSuggestBox`, `ComboBox`, `CalendarDatePicker`, `TimePicker`, `MauiStepper`, `ToggleSwitch`, `CheckBox`, `RadioButton`, `ProgressBar` | Works |
| `Ellipse`, `Polygon`, `Border` with gradient stroke and asymmetric corners | `W2DGraphicsView`, `ContentPanel` | Works, gradients included |
| `FlexLayout`, `AbsoluteLayout` | `LayoutPanel` | Works, including wrapping and overlap |
| `SwipeView` | `SwipeControl` | Content renders |
| `IndicatorView` | `MauiPageControl` | Works |
| Gestures (`Tap`, `Pan`) and animation | `ContentPanel`, `MauiButton` | Realized |
| `CommunityToolkit UniformItemsLayout`, `CommunityToolkit DockLayout` | `LayoutPanel` | Works — third-party library, compiled from source |
| `CollectionView`, `CarouselView`, `RefreshView` | `FormsListView`, `RefreshContainer` | Paint in the current Default runtime; Full is not required for these sample pixels. |
| `GraphicsView` | `PlatformTouchGraphicsView` / `W2DGraphicsView` | Enabled by default; first draw, color invalidation and drawable removal verified with pixels in Default and Full. |

### Graphics regression checks

The previous graphics hang had two shared causes, not a vendor-specific layout failure:
`SKXamlCanvas` could paint from its `SizeChanged` callback before the derived graphics view updated its
cached bounds, and text wrapping did not advance when no glyph fit the resulting zero-width line.
The view now reads its arranged dimensions at draw time. Text layout rejects an empty effective width
and consumes a complete text element when a glyph is wider than a positive-width line. Removing the
drawable also clears the surface instead of retaining the previous image.

The graphics card supplies **Change drawing color** and **Clear drawing** buttons. Require the initial
purple drawing and white ellipses, a teal drawing after normal invalidation, and no retained drawing
after clearing. `MAUI_UNO_RENDER_PROBE=1` additionally reports `GRAPHICS-FIRST-BOUNDS PASS` or `FAIL`;
that bounds assertion complements, but never replaces, pixel checks.

The separate `MAUI_UNO_CANVAS_PROBE=1` fixture exercises point text, rectangle-aligned text and a
polyline through the third-party source adapter. Require black and blue text and a green polyline.
An `EXECUTED` operation is not a painting verdict; unsupported operations report `FAIL`, and missing
pixels fail the visual gate. The adapter uses ordinary `ICanvas` text/path operations and the registered
MAUI font manager, not Win2D sessions, vendor control checks or a production runtime dependency.

The sample can isolate cards without changing their normal layout or painting:

```powershell
$env:MAUI_UNO_GALLERY_CARDS = "5"
$env:MAUI_UNO_GALLERY_SKIP = "CollectionView,CarouselView,RefreshView,SwipeView"
$env:MAUI_UNO_GALLERY_ONLY = "1"   # omit the two unrelated demonstration islands
$env:MAUI_UNO_RENDER_PROBE = "1"
```

For WebAssembly these variables must be supplied through the bootstrap environment before managed
startup. The default remains the full interleaved-island demonstration. The census reports
**realized**, not rendered; dimensions and descendant counts alone do not establish painting.

## Handler modes

Embedding runs in one of two handler modes.

| Mode | Handlers | Use |
| --- | --- | --- |
| `Default` | MAUI's own, recompiled against Uno.WinUI | Shared handler configuration |
| `Full` | The same default MAUI handler set | Legacy alias for applications that selected the former experimental mode |

```csharp
MauiApp.CreateBuilder()
    .UseMauiEmbeddedApp<App>()
    .UseUnoHandlers(UnoHandlerMode.Full)
    .Build();
```

Selecting the mode in the sample: `MAUI_UNO_HANDLER_MODE=full` or a `handlers=full` command-line argument on
Desktop; on WebAssembly the choice is baked in with `-p:MauiUnoFullHandlers=true`, because the browser has
neither an environment nor a command line the runtime can read. (The query string does **not** reach
`Environment.GetCommandLineArgs` under Uno WebAssembly, which is worth knowing before relying on it.)

`Full` no longer replaces CollectionView or CarouselView. Both modes use the repaired default path
instead of maintaining a second partial implementation. The old handler class names remain obsolete
aliases deriving from MAUI's handlers. This preserves names, not the former platform-view API or binary
contract: code depending on the old `ScrollViewer`/`ItemsRepeater` implementation must migrate and rebuild.

The production compatibility probe covers handler resolution, grouping, multiple selection,
headers/footers, empty-state transitions, incremental loading, dynamic sources, carousel state,
scrolling, failure rollback and teardown. Neither mode name implies complete platform or third-party
product support.

The managed `ItemsWrapGrid` implementation still rejects grouped grids, sticky group headers and
drag-and-drop reordering rather than silently changing their layout. These are explicit primitive
boundaries; linear grouped collections remain supported.

## Accessibility transition probes

`MAUI_UNO_ITEM_NAMES_PROBE=1` runs a bounded item-template fixture and publishes
`ITEM-NAMES-PROBE PASS` or `FAIL` in the window title. It checks the realized item container's
automation name through visibility, insertion, removal, text/description changes, accessibility
exclusions and template replacement. Native coverage is in
`ItemAutomationNameTracksVisibilityChildrenAndExclusions`.

`MAUI_UNO_MODAL_SCOPE_PROBE=1` exposes a named background page, nested modals and explicit
open/close/finish buttons. Check the actual semantic tree, not only `AccessibilityView` values:
covered background and first-modal actions must be absent, then restored after pop. Repeat with
accessibility enabled before opening and after opening (`MAUI_UNO_MODAL_SCOPE_AUTO=1`).
Set `MAUI_UNO_MODAL_SCOPE_BACKGROUND=transparent` or `opaque` to exercise retained page roots:
the default-background navigation path detaches the covered root and cannot prove accessibility
isolation for a still-painted background. The probe reports each page's loaded state.
Semantic DOM invocation exercises assistive-technology activation, not physical pointer input.
These opt-in fixtures restore their host content; they are not production platform workarounds.

## Third-party MAUI controls

The tables below record particular source-built subsets and adapter builds, not
production certification of entire third-party products. The combined candidate
still requires per-control painting, interaction, accessibility and failure-path
acceptance. A `44/44 realized` census is not a rendering or production verdict.

Three genuinely external libraries run in the gallery, all **compiled from source** and consumed unmodified:

| Library | License | Pinned at | What runs |
| --- | --- | --- | --- |
| CommunityToolkit.Maui | MIT | tag `9.1.1` | `UniformItemsLayout`, `DockLayout`, converters (`InvertedBoolConverter`, `TextCaseConverter`), behaviours (`MaskedBehavior`, `NumericValidationBehavior`, `TextValidationBehavior`, `MaxLengthReachedBehavior`, `AnimationBehavior`, `ProgressBarAnimationBehavior`) |
| Syncfusion .NET MAUI Toolkit | MIT | `main` | `SfCartesianChart` (column, stacked column, line, spline, area, scatter), `SfCircularChart` (doughnut, pie), `SfFunnelChart`, `SfPyramidChart`, `SfChartLegend`; polar area, axis labels, ticks and gridlines with explicitly configured axes — see qualifications below |
| Maui.DataGrid | MIT | `main` (`506312fd`) | `DataGrid` with sortable columns, selection and pagination |

**Telerik UI for .NET MAUI is commercial**, not open source, and cannot be used here at all. Of the other
OSS candidates, Microcharts, LiveCharts2 and FreakyControls all render through SkiaSharp, and UraniumUI
depends on `InputKit.Maui` and `Plainer.Maui`, which are NuGet-only with no source repository.

### Maui.DataGrid: the most informative of the three

`Maui.DataGrid` is the one worth reading about, because it is not a control that happens to work — it is a
real library whose rows are rendered by a MAUI `RefreshView` wrapping a `CollectionView`. This exercises
composition beyond a synthetic item template. Historical blank Default-mode results do not describe
the current runtime; the grid's sorting, selection and pagination still require independent acceptance.

It is also the least modified of the three. Upstream already targets a bare `net10.0` with no `Platforms`
folder and a single `Microsoft.Maui.Controls` package reference, so nothing is excluded: the whole library
compiles, and the only change is that the package reference becomes a project reference. Three details were
still needed:

- **Pinned to `main`, not to the `4.0.6` tag.** The released tag calls `TemplatedView.Children`, which MAUI 10
  marks obsolete *as an error*; `main` has already migrated to `IVisualTreeElement.GetVisualChildren()`.
  Pinning forward keeps the library unmodified rather than patching it.
- **`Microsoft.Maui.Devices` had to be referenced explicitly**, because `DataGrid.xaml.cs` uses `DeviceInfo`
  and the plain SDK does not inject the MAUI global usings.
- **The assembly keeps its upstream name.** `DataGrid.xaml` declares
  `xmlns:local="clr-namespace:Maui.DataGrid;assembly=Maui.DataGrid"`, and XamlC resolves that by name at
  compile time, so a `.Uno` suffix breaks the build.

Its columns bind by property name and are resolved reflectively, which is the same trimming hazard the
Syncfusion charts hit — a trimmed build would draw the headers over an empty body. A `DynamicDependency` on
`DemoItem` is what keeps the bound properties alive.

### Binary compatibility must be established per package

This repository builds `Microsoft.Maui.Controls` against Uno rather than the
native Windows App SDK. Platform-specific binaries and portable handlers with
incompatible platform-view signatures can therefore fail to load. Portable
composite controls may work unchanged; it is not correct to reject every MAUI
package categorically. These showcase integrations are rebuilt from pinned
source, with the exclusions and adapters described below. Neither restore nor
successful compilation establishes compatibility.

Initialise both before building:

```powershell
git submodule update --init --recursive
```

### CommunityToolkit: what is compiled, and what is not

`ThirdParty/CommunityToolkit.Maui.Core.Uno.csproj` and `ThirdParty/CommunityToolkit.Maui.Uno.csproj`
compile a curated subset. Three constraints decided that subset, and each is worth knowing before extending
it:

- **The toolkit's own source generator cannot be built here.** Current `main` generates its bindable
  properties from a `[BindableProperty]` attribute, and that generator needs `Microsoft.CodeAnalysis.CSharp`
  and `PolySharp`, neither of which is in the local package cache while nuget.org is unreachable. On `main`
  31 files depend on it; on the pinned **9.1.1** only one does (`Expander`), which is why 9.1.1 is pinned
  and `Expander` is excluded.
- **Core and the main library need different global usings.** Core resolves `ILayout` to
  `Microsoft.Maui.ILayout`; the main library resolves `Layout` to `Microsoft.Maui.Controls.Layout`. Merging
  them into one assembly makes `ILayout` ambiguous, so they stay split exactly as upstream ships them. Core
  still *references* `Microsoft.Maui.Controls` — MAUI's own source generators emit code that needs it — but
  deliberately does not import it as a global using.
- **9.1.1 targets MAUI 8, and this repository is MAUI 10.** Areas that drifted (SpeechToText, Popup and
  DrawingView handlers, Essentials) do not compile and are excluded.

The `WINDOWS` define is also removed for these two projects. `UnoTargeting.props` defines it so MAUI's
Windows handlers compile against Uno.WinUI, but the toolkit's Windows sources reach past XAML into
`System.Speech`, `Windows.UI.Input.Inking`, `Windows.Storage.Pickers` and `Windows.UI.Notifications`, none
of which exist in a browser. Dropping the define selects the toolkit's own supported neutral build.

### Syncfusion: what it took

Syncfusion draws through its own `SfDrawableView : View` rather than MAUI's `GraphicsView`.
Both handlers ultimately use the shared Skia-backed graphics surface.

- **`WINDOWS` stays defined**, unlike the CommunityToolkit build. Syncfusion's neutral "Standard" handlers
  are deliberate stubs: `SfDrawableViewHandler.Standard.cs` throws `NotImplementedException` and types its
  platform view as `object`, which does not even satisfy the `FrameworkElement` constraint here. The Windows
  handlers are the real implementations and target `Microsoft.UI.Xaml`, which is what Uno provides.
- **Three files needed replacing**, because they use real Win2D (`Microsoft.Graphics.Canvas`), which does
  not exist here — on the Uno target `W2DGraphicsView` is a *Skia-backed shim* living in the
  `Microsoft.Maui.Graphics.Win2D` namespace for source compatibility. `ThirdParty/SyncfusionUnoShims.cs`
  supplies a drawing panel that hands the `IDrawable` straight to that view, written against the surface
  the toolkit's own handlers use rather than copied from them.
- **Core and Charts only.** Taking the whole library pulls in controls that wrap native WinUI views
  (Carousel) or blur through Win2D composition (Popup), and each drags more of the library with it.
- **XAML is scoped to what is compiled.** XamlC runs on Release but not Debug, and resolves every type the
  theme dictionaries reference, so shipping themes for uncompiled controls fails the publish.

### The trimming trap worth knowing

Syncfusion resolves `XBindingPath`/`YBindingPath` by reflection. In a trimmed build that silently binds
**zero points** — and because the axis still draws its gridlines, the chart looks very nearly right while
plotting nothing. Annotating the model type with `DynamicallyAccessedMembers` does **not** preserve its
members; a `DynamicDependency` declared from a method that is kept does.

This is why the census now reports `chartPoints=[...]`. The measurement is what caught it:

| | Desktop (untrimmed) | Trimmed WASM, before | Trimmed WASM, after |
| --- | --- | --- | --- |
| `ColumnSeries` | 5 | **0** | 5 |
| `DoughnutSeries` | 5 | **0** | 5 |

### Polar chart configuration and qualification

The sample originally omitted `PrimaryAxis` and `SecondaryAxis`. The toolkit leaves these null by
default and does not generate polar segment data without the associated axes, even when its point
count is five. The fixture now supplies `CategoryAxis` and `NumericalAxis` through their normal public
properties; no handler, composition, clipping or vendor-specific runtime branch was added.

There was a second, independent fixture error: its project selected upstream neutral canvas extension
stubs. Both `DrawText` overloads threw, and `DrawLines` silently did nothing. The fixture now maps these
operations to general `ICanvas.DrawString`, `GetStringSize` and `DrawPath` primitives, preserving drawing
state and honoring font size, slant, weight, alignment, scaling and line styling. Incomplete polyline
coordinate pairs throw rather than silently dropping a coordinate.

The Release WASM gate requires area fill, all five category labels, radial tick labels and gridline
pixels. The earlier fill-only image fails this gate. Separate neutral drawing cases fail against the
selected stubs and pass against the portable adapter. This covers the default-themed source fixture,
not full third-party product, accessibility or interaction parity. The excluded theme dictionaries
remain **unsupported/unaccepted**; no themed-mode acceptance is inferred from coded defaults.

## Remaining gaps

P1–P4 of the original gap list are closed, and P5 is closed apart from actual package distribution. The
details of what changed are in the commit history.

**Closed in P5:**

- **G11. `MauiHost` host integration** — the `DataContext` → `BindingContext` bridge and theme bridging
  are implemented and asserted. `Source` remains deliberately absent: type activation is not trim-safe,
  and the trimmed WebAssembly run below is what that choice buys.
- **G12. Heads** — Desktop, WebAssembly, Android and Apple (iOS + Mac Catalyst) heads all exist, and
  `Build.ps1` already routes `-Target Android|iOS|MacCatalyst` to them. "Skia desktop" was never a separate
  head: the Desktop head *is* the Skia desktop head, carrying the Win32, X11, macOS and framebuffer
  runtimes.
- **G10, partly. A reusable library** — `MauiEmbeddingSession` and `MauiHost` now live in
  `Microsoft.Maui.Controls.Embedding.Uno` rather than in the sample, and the hard-wired dependency on the
  sample's `MauiProgram` is gone in favour of `MauiEmbeddingSession.UseMauiApp(...)`.

**What genuinely remains:**

- **G10. The embedding library is not distributed as a consumer package.**
  The renderer has SDK/runtime package-mode builds, but the embedding assembly
  remains `IsPackable=false`, and the required combined Uno fixes have not been
  published as a supported release. Shipping and validating a complete,
  reproducible consumer dependency graph remains a release blocker.
- **The Apple head is authored but unbuilt.** It mirrors the working MAUI-root Apple head, but the `ios`
  and `maccatalyst` workloads are not installed on the machine used here, so it has had no compile pass.

WebAssembly window-scoped behaviour is verified by a **real browser run**: the probe is enabled by the
`MauiUnoTier2Probe` build switch, publishes its verdict to the document title, and a headless Chromium run
reads that title over the DevTools endpoint. **Trimmed Release** WebAssembly reports `TIER2-RESULT PASS`
with all 27 assertions passing — alerts, prompts, action sheets, modal navigation, stack navigation,
second-page rejection, theme bridging including a runtime theme switch, the `DataContext` bridge including
a resolved MAUI binding, the off-UI-thread alert regression, and content replacement.

The trimmed run is the interesting one, because MAUI reaches a lot of its Windows handler surface through
reflection and dynamic resource lookup. `PublishTrimmed=true` takes `Microsoft.Maui.Controls.dll` from
2105 KB to 1119 KB and the probe still passes, so nothing on the embedding path is being reached in a way
the trimmer cannot see. Two design choices are what make that hold, and both are load-bearing rather than
stylistic: `MauiHost` takes an element instance rather than activating a `Type`, and the probe's binding
assertion uses a typed binding rather than a string path — a string path carries `RequiresUnreferencedCode`
and fails the publish outright.

Publishing to WebAssembly is also what catches missing references. Transitive project references are
disabled across this sample, so a head that only referenced `Shared` still built and ran on Desktop — where
the assembly is copied to the output folder regardless — while silently omitting
`Microsoft.Maui.Controls.Embedding.Uno` from the WebAssembly publish, whose asset set is computed from the
head's own resolved references. Every head therefore references the library explicitly, from
`MauiUnoSample.props`.

Reproducing a trimmed run needs a machine with no competing WebAssembly build. The Emscripten native cache
under `%TEMP%\emsdk-cache` is shared by every build on the machine and by every installed emsdk version, and
a concurrent build re-linking it leaves it half-populated (`unable to find library -lsockets`, then a
missing `sysroot/include/emscripten/version.h`). Uno's emsdk wrapper does not honour `EM_CACHE`, so the
cache cannot be isolated per build; clear it and rebuild if a run has already corrupted it.

### Verifying

```powershell
# Desktop
.\Build.ps1 -Sample Embedding -Target Desktop -Run     # then press "Run Tier 2 probe"

# WebAssembly, headless
dotnet build WebAssembly\Controls.Sample.Embedding.Uno.WebAssembly.csproj -p:MauiUnoTier2Probe=true
dotnet run --project WebAssembly\Controls.Sample.Embedding.Uno.WebAssembly.csproj --no-build
chrome --headless=new --remote-debugging-port=9222 http://127.0.0.1:<port>/
# poll http://127.0.0.1:9222/json/list until the page title reports TIER2-RESULT PASS or FAIL

# WebAssembly, trimmed Release
dotnet publish WebAssembly\Controls.Sample.Embedding.Uno.WebAssembly.csproj -c Release -p:MauiUnoTier2Probe=true
# `dotnet run` serves build output, not publish output, so serve the published wwwroot with any static
# server that returns application/wasm for .wasm, then point the same headless Chromium run at it.
```

Restoring one head overwrites the shared `project.assets.json` and drops the other head's target, so
re-restore for the head being built when switching between Desktop and WebAssembly.

The probe writes `tier2-probe.log` to the temp directory where a filesystem is available, shows its report
in the app, and always publishes `TIER2-RESULT PASS`/`FAIL` to the document title and standard output so a
headless run can collect it. Uno renders to a canvas on WebAssembly, so the per-assertion report is only
readable from the console or the on-screen text — the document title is what an automated run should key
off. Note that the probe deliberately leaves its last dialog open — the off-UI-thread alert cannot dismiss
itself — so it has to be the **last** thing an interactive pass clicks, or the open dialog swallows
subsequent clicks.

A check whose precondition a supported host action removed is reported as `SKIP`, not `FAIL`. There is one
today: pressing **Replace content in island 1** swaps the island's `NavigationPage` for a plain
`ContentPage`, so there is no stack left to push onto. Skips do not fail the run, and the verdict reports
them (`RESULT: PASS (1 skipped)`).

### Interactive pass

Driving the host buttons and then the probe exercises the paths the probe itself cannot reach. Verified in a
trimmed Release WebAssembly publish in headless Chromium, with real DevTools mouse events rather than
synthetic DOM events — Uno renders to a canvas, so only trusted input reaches managed hit-testing:

| Step | Result |
| --- | --- |
| Baseline | `CENSUS-RESULT 43/43 realized, handlers=Full`, all ten chart series bound 5 points |
| Replace content in island 1 | Island 1 re-renders as "First MAUI island (replaced 1x)", no exception |
| Detach island 2 | Status flips to "Island 2 attached: no", button becomes "Re-attach island 2" |
| Re-attach island 2 | Status flips back to "yes", island renders again |
| Run Tier 2 probe | `PASS` from a pristine start; after the replacement above, `PASS (1 skipped)` |

Do **not** reload the page between steps: the DevTools connection is starved while the WebAssembly runtime
re-boots, and the driving script hangs rather than failing.

### What was closed, and how

| Gap | Resolution |
| --- | --- |
| G1 `MauiHost.Session` reassignment | The host tracks the session that actually realized the content, so reassigning or clearing `Session` releases through the owner rather than the newly assigned one. |
| G2 window located by position | `CreateEmbeddedWindowContext` now has an overload returning the window it created; the guess at `Application.Windows` is gone. |
| G3 second page island | `MauiEmbeddingSession.Embed` throws instead of silently routing a second page's dialogs and modals through the first island. |
| G4 incomplete root release | `CreateEmbeddedWindowRoot` returns a disposable `EmbeddedWindowRoot` whose disposal tears down modal pages, disconnects the navigation root, clears the container and unregisters it from the window scope. |
| G5 synthetic lifecycle | `Created`/`Activated`/`Deactivated` are relayed from the real native window events instead of being raised while the host is still being constructed. |
| G6 alert dispatch | A failed `TryEnqueue` now completes the caller's arguments instead of hanging it, and the alert and prompt queue slots are cleared in `finally` so a failed dialog cannot poison later ones. |
| G7 API shape | `ToPlatformEmbeddedWindowRoot` is replaced by `CreateEmbeddedWindowRoot`, which validates its context and its page-is-the-window-page precondition, and returns a disposable root. |
| G8 probe asserted nothing | `Tier2Probe` is now a pass/fail harness with per-operation timeouts, and covers two-button alerts, prompts, action sheets and the second-page rejection. |
| G9 WebAssembly never actually run | The probe is driven in headless Chromium and reports its verdict through the document title. Trimmed Release passes all 27 assertions. |
| G10 no reusable library | `MauiEmbeddingSession` and `MauiHost` moved to `Microsoft.Maui.Controls.Embedding.Uno`, and the embedded `MauiApp` is supplied via `UseMauiApp(...)` instead of a hard reference to the sample's `MauiProgram`. Distribution as a package remains blocked on the MAUI-for-Uno stack shipping at all. |
| G11 no host integration | `MauiHost` bridges `DataContext` to `BindingContext`; `MauiEmbeddingSession` bridges the host's effective theme. Both are asserted, the theme one by performing a real runtime theme switch. |
| G12 missing heads | Android and Apple heads added; `Build.ps1` already routed to them. Android compiles; Apple is unbuilt for lack of workloads. |

### Checked and found not to be a problem

Content replacement was reported as unsound, on the theory that a fresh `WindowRootViewContainer` per call
re-parents the single `NavigationRootManager.RootView` and violates XAML's single-parent rule. Three
consecutive page replacements render the current page each time with no stale content and no exception —
Uno's collection re-parents rather than throwing. That is asserted by the probe, and it holds on
WebAssembly as well as on Desktop.

## Build and run

```powershell
.\Build.ps1 -Sample Embedding -Target Desktop -Run
.\Build.ps1 -Sample Embedding -Target WebAssembly -Run
.\Build.ps1 -Sample Embedding -Target Android -Run
.\Build.ps1 -Sample Embedding -Target iOS -Run          # untested: needs the ios workload
.\Build.ps1 -Sample Embedding -Target MacCatalyst -Run  # untested: needs the maccatalyst workload
```

This sample is deliberately large, because its job is to map what works. For the smallest app that embeds
MAUI in a plain Uno application — five files, one project, no gallery — see
[`Controls.Sample.Embedding.Uno.Minimal`](../Controls.Sample.Embedding.Uno.Minimal/README.md).

## Notes

- The project removes the MAUI SDK's `Microsoft.Maui`, `Microsoft.Maui.Controls` and
  `Microsoft.Maui.Graphics` global usings. In an Uno-root app almost every one of those names
  collides with a WinUI equivalent (`Application`, `Window`, `Grid`, `Button`, `Border`, `Thickness`,
  `CornerRadius`, `GridLength`, `Colors`), so the MAUI namespaces are imported per file instead.
- `MauiHost` takes a MAUI element **instance** rather than a `Type` to activate. Type-based
  activation via `ActivatorUtilities` is not trim-safe, and this library is validated
  against a trimmed WebAssembly publish.
- The Shared project uses a distinct `RootNamespace` from the heads so that Uno's XAML source
  generator does not emit two conflicting `GlobalStaticResources` types.
