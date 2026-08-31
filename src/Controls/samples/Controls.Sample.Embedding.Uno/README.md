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

Results from a **trimmed Release WebAssembly publish in headless Chromium**:

| Control | Platform view | Result |
| --- | --- | --- |
| `Editor`, `SearchBar`, `Picker`, `DatePicker`, `TimePicker`, `Stepper`, `Switch`, `CheckBox`, `RadioButton`, `ProgressBar` | `TextBox`, `AutoSuggestBox`, `ComboBox`, `CalendarDatePicker`, `TimePicker`, `MauiStepper`, `ToggleSwitch`, `CheckBox`, `RadioButton`, `ProgressBar` | Works |
| `Ellipse`, `Polygon`, `Border` with gradient stroke and asymmetric corners | `W2DGraphicsView`, `ContentPanel` | Works, gradients included |
| `FlexLayout`, `AbsoluteLayout` | `LayoutPanel` | Works, including wrapping and overlap |
| `SwipeView` | `SwipeControl` | Content renders |
| `IndicatorView` | `MauiPageControl` | Works |
| Gestures (`Tap`, `Pan`) and animation | `ContentPanel`, `MauiButton` | Realized |
| `CommunityToolkit UniformItemsLayout`, `CommunityToolkit DockLayout` | `LayoutPanel` | Works — third-party library, compiled from source |
| `CollectionView`, `CarouselView`, `RefreshView` | `FormsListView`, `RefreshContainer` | **Realized and arranged, but nothing is painted** in Default mode. Full mode fixes all three; see Handler modes. |
| `GraphicsView` | — | **Hangs the layout; omitted by default** |

### The two failures, precisely

**`FormsListView`-backed controls paint nothing on WebAssembly.** This is not a data, template or binding
problem, and it is not a missing handler. The census shows the item subtrees fully realized *and* arranged
with correct sizes — `CollectionView` reports 70 descendants of which 52 have a non-zero size,
`CarouselView` 105 and 70, `RefreshView` 81 and 62 — and the containers themselves are laid out at the
right dimensions. They simply never draw. `RefreshView` is blank only because it contains a
`CollectionView`; the two genuinely distinct casualties are `CollectionView` and `CarouselView`, which
share the `FormsListView` platform view. `IndicatorView`, which is a different platform control, paints its
dots correctly right next to the blank carousel.

This is why the census reports **realized**, not rendered: no cheap in-process signal distinguishes "laid
out" from "painted", so painting is only ever confirmed from a screenshot.

**`GraphicsView` puts the layout into a loop that never settles.** No exception is raised and nothing is
logged; the UI thread simply never completes a pass and the working set climbs without bound — roughly
2 GB to 6 GB in fifteen seconds before the process has to be killed. Because a hung layout takes the whole
app down, it cannot be left in a demo gallery, so it is omitted by default. Note that `Ellipse` and
`Polygon` render through the very same `W2DGraphicsView` platform view without trouble, so the fault is in
the `GraphicsView` control rather than in Win2D-on-Uno generally.

Both are triageable without a rebuild:

```powershell
$env:MAUI_UNO_GALLERY_CARDS = "4"   # build only the first four cards, to bisect a hang
$env:MAUI_UNO_GALLERY_SKIP  = ""    # clear the default omissions, to reproduce the GraphicsView hang
```

## Handler modes

Embedding runs in one of two handler modes.

| Mode | Handlers | Use |
| --- | --- | --- |
| `Default` | MAUI's own, recompiled against Uno.WinUI | Unchanged behaviour; this is what every earlier result in this file describes |
| `Full` | MAUI's own, **except** the ones that do not survive every Uno target | Opt-in, additive — only the handlers listed below are replaced |

```csharp
MauiApp.CreateBuilder()
    .UseMauiEmbeddedApp<App>()
    // After UseMauiEmbeddedApp: handler registration is last-one-wins, so replacing only works from here.
    .UseUnoHandlers(UnoHandlerMode.Full)
    .Build();
```

Selecting the mode in the sample: `MAUI_UNO_HANDLER_MODE=full` or a `handlers=full` command-line argument on
Desktop; on WebAssembly the choice is baked in with `-p:MauiUnoFullHandlers=true`, because the browser has
neither an environment nor a command line the runtime can read. (The query string does **not** reach
`Environment.GetCommandLineArgs` under Uno WebAssembly, which is worth knowing before relying on it.)

### What Full mode replaces, and what it fixes

| Virtual view | Default handler renders through | Full mode renders through |
| --- | --- | --- |
| `CollectionView` | `FormsListView` — a `ListViewBase` with a custom control template and `ItemsStackPanel` virtualization | `ScrollViewer` + `ItemsRepeater` with a `StackLayout` or `UniformGridLayout` |
| `CarouselView` | `FormsListView`, same cause | `ScrollViewer` + `ItemsRepeater`, items sized to the viewport, position synced both ways |

Measured on trimmed Release WebAssembly, same build, same page:

| Control | Default mode | Full mode |
| --- | --- | --- |
| `CollectionView` | blank | **paints** |
| `CarouselView` | blank | **paints**, with `IndicatorView` in step |
| `RefreshView` | blank | **paints** |

`RefreshView` is fixed without being touched: it was only ever blank because it *contains* a
`CollectionView`. That is the useful shape of this result — it identifies `FormsListView` rather than the
items controls as the actual fault, so replacing it fixes everything built on top of it.

### Why a replacement handler rather than a fix

The default handler's item containers are realized *and arranged at correct sizes* on WebAssembly and then
never painted, so there is nothing the embedding layer can correct from the outside — the failure is inside
`ListViewBase`'s templated virtualization path. `ItemsRepeater` is the portable primitive: a layout plus an
element factory, with no control template and no platform-specific panel.

Two details cost real time and are worth knowing before writing another one:

- `ItemsRepeater.ItemTemplate` is typed `object` but accepts only a `DataTemplate` or something it can treat
  as its internal element-factory shim. Assigning a bare `IElementFactory` throws
  `ArgumentException: ItemTemplate` at assignment. Derive from `ElementFactory` instead.
- `ElementFactory`'s `GetElementCore`/`RecycleElementCore` take the `Microsoft.UI.Xaml.Controls` args types,
  not the identically named ones in `Microsoft.UI.Xaml`.

### What the replacement does not map

`UnoCollectionViewHandler` covers `ItemsSource`, `ItemTemplate` (including `DataTemplateSelector`),
`ItemsLayout` (linear and grid, both orientations) and single selection by tap. Grouping, reordering,
incremental loading, headers and footers, multiple selection and the empty view are **not** implemented —
those properties have no effect rather than throwing. Items are also not recycled into new data, because a
recycled MAUI view would need re-binding and handler re-attachment; MAUI's own Windows handler does not
recycle either.

`UnoCarouselViewHandler` covers `ItemsSource`, `ItemTemplate`, `Position`, `CurrentItem`, `IsSwipeEnabled`,
`Loop`, `PeekAreaInsets`, `IsBounceEnabled` and `VisibleViews`. Snapping is done by hand — `ItemsRepeater`
does not implement `IScrollSnapPointsInfo`, so the `ScrollViewer` has no snap points and the nearest item is
scrolled to once the view stops moving. `Loop` is implemented by repeating the source three times and
re-centring on the middle block, so wrapping is seamless in both directions without an unbounded source.
**`IsBounceEnabled` is an approximation**: Uno has no rubber-band overscroll, so it toggles scroll inertia
instead, which is the closest available behaviour rather than an exact match.

## Third-party MAUI controls

Two genuinely external libraries run in the gallery, both **compiled from source** and consumed unmodified:

| Library | License | Pinned at | What runs |
| --- | --- | --- | --- |
| CommunityToolkit.Maui | MIT | tag `9.1.1` | `UniformItemsLayout`, `DockLayout`, converters (`InvertedBoolConverter`, `TextCaseConverter`), behaviours (`MaskedBehavior`, `NumericValidationBehavior`, `TextValidationBehavior`, `MaxLengthReachedBehavior`, `AnimationBehavior`, `ProgressBarAnimationBehavior`) |
| Syncfusion .NET MAUI Toolkit | MIT | `main` | `SfCartesianChart` (column, stacked column, line, spline, area, scatter, polar), `SfCircularChart` (doughnut, pie), `SfFunnelChart`, `SfPyramidChart`, `SfChartLegend` |

**Telerik UI for .NET MAUI is commercial**, not open source, and cannot be used here at all. Of the other
OSS candidates, Microcharts, LiveCharts2 and FreakyControls all render through SkiaSharp, and UraniumUI
depends on `InputKit.Maui` and `Plainer.Maui`, which are NuGet-only with no source repository.

### Why the NuGet package cannot be used

A third-party MAUI package ships assemblies compiled against the shipping `Microsoft.Maui.Controls`, which
is itself compiled against the Windows App SDK. This repository's `Microsoft.Maui.Controls` is a *different
build of the same assembly*, compiled against `Uno.WinUI`. Any platform-specific type in that package would
bind to the wrong `Microsoft.UI.Xaml`. The library therefore has to be recompiled from source — which is
precisely the move this repository already makes for MAUI itself. **The interesting result is that this
generalises: the trick is not MAUI-specific.**

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

The charts were the interesting case, because Syncfusion draws through its own
`SfDrawableView : View` rather than MAUI's `GraphicsView` — so they sidestep the layout hang described
above entirely.

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

- **G10. There is still no consumable package.** This is not a gap in the embedding layer; it is a
  prerequisite owned by the renderer project. The whole MAUI-for-Uno stack is consumed as *source* project
  references built for a neutral `net10.0` target — there are no MAUI-for-Uno NuGet packages at all — so
  packaging this one assembly would produce something with nothing to reference. The library is therefore
  marked `IsPackable=false` until the underlying stack ships. Using this still means building the fork.
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
off.

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
