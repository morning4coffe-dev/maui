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
