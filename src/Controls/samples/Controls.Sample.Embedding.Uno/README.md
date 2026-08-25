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

| File | Role |
| --- | --- |
| `Shared/UnoEmbeddingApplication.cs` | Uno application root — a plain `Application`, not `MauiWinUIApplication` |
| `Shared/MainShell.cs` | Uno-owned UI; MAUI islands interleaved with Uno content |
| `Shared/Embedding/MauiEmbeddingSession.cs` | One `MauiApp` per process, one `IMauiContext` per `Window`, exactly-once teardown |
| `Shared/Embedding/MauiHost.cs` | `ContentControl` that realizes a MAUI element into the Uno tree |
| `Shared/MauiIslandPage.cs` | Tier 2 island: a `Page` exercising alerts and modal navigation |
| `Shared/MyMauiContent.cs` | Tier 1 island: a plain `ContentView` |
| `Shared/Tier2Probe.cs` | Code-driven verification of the window-scoped features |
| `Shared/MauiProgram.cs`, `Shared/App.cs` | The embedded MAUI app |

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

Two things were required:

1. **`Window.Page` must be set.** `AlertManager.Subscribe()` only runs from `Window.OnPageHandlerChanged`,
   so without a page the awaited dialog task never completes. `MauiEmbeddingSession` promotes the first
   page-based island to the embedded window's page. Because `Window.Page` already parents the page, that
   path uses `ToPlatform` rather than `ToPlatformEmbedded`, which would parent it a second time.
2. **The window must report that it was created and activated.** `ModalNavigationManager` gates every
   platform push on `_firstActivated`, which a standalone app gets from `MauiWinUIWindow`. Nothing raises
   it for an embedded window, so modals stay queued in the virtual stack forever — `PushModalAsync` even
   returns successfully while nothing renders. The session raises `IWindow.Created()` and
   `IWindow.Activated()` once.

Alerts needed no MAUI change at all. Modals needed one, described below.

## The one MAUI change Tier 2 required

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

The upshot is that modals and overlays stay inside the embedded region instead of covering the whole
hosting window.

## Still not supported

- **`WindowOverlay`, visual diagnostics, MAUI hot reload.** `WindowOverlay.Windows.cs` casts
  `Window.Handler as WindowHandler`, and embedding uses `EmbeddedWindowHandler`, which is an
  `IWindowHandler` but not a `WindowHandler`. It fails closed and simply disables overlays.
- **Shell.** Not attempted.
- **`CreateWindow`, `Application.Current.MainPage`, `OpenWindow`.** Embedding creates a synthetic
  `EmbeddedWindow`; `TApp.CreateWindow` is never called.
- **One window page per Uno window.** A window has exactly one `Page`, so only the first page-based
  island gets Tier 2 treatment. Further islands remain Tier 1.

## Verifying Tier 2

`Tier2Probe` drives the window-scoped features from code and reports what actually happened, so the
result does not depend on UI automation. Run the app with `MAUI_UNO_TIER2_PROBE=1` to run it at startup,
or press **Run Tier 2 probe** in the app. Results appear in the app and in `tier2-probe.log` in the temp
directory:

```
window.Page is the island page: True
alert opened a popup: True (count 1)
alert task completed after dismissal: True
PushModalAsync returned: OK (virtual stack depth 1)
modal handler created: True
modal platform view: ContentPanel
modal attached to XamlRoot: True
modal actually rendered: True
PopModalAsync: OK
```

The modal assertions deliberately check the platform view rather than the stack depth: MAUI records a
push in the virtual stack even when the platform never realizes the page, so `PushModalAsync` succeeding
proves nothing on its own.


## Build and run

```powershell
.\Build.ps1 -Sample Embedding -Target Desktop -Run
.\Build.ps1 -Sample Embedding -Target WebAssembly -Run
```

Only Desktop and WebAssembly heads exist so far.

## Notes

- The project removes the MAUI SDK's `Microsoft.Maui`, `Microsoft.Maui.Controls` and
  `Microsoft.Maui.Graphics` global usings. In an Uno-root app almost every one of those names
  collides with a WinUI equivalent (`Application`, `Window`, `Grid`, `Button`, `Border`, `Thickness`,
  `CornerRadius`, `GridLength`, `Colors`), so the MAUI namespaces are imported per file instead.
- `MauiHost` takes a MAUI element **instance** rather than a `Type` to activate. Type-based
  activation via `ActivatorUtilities` is not trim-safe, and this sample is intended to be validated
  against a trimmed WebAssembly publish.
- The Shared project uses a distinct `RootNamespace` from the heads so that Uno's XAML source
  generator does not emit two conflicting `GlobalStaticResources` types.
