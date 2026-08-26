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
| `PushAsync` / `PopAsync` (`NavigationPage`) | Works — the pushed page is really realized and rendered |

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
  island gets Tier 2 treatment. See gap G3 below — this currently degrades silently and incorrectly.

## Remaining gaps and the plan to close them

This sample is a verified proof of the architecture on Desktop. It is **not yet consumer-usable**. The
gaps below came out of two independent reviews plus targeted testing, and are ordered by the sequence
they should be fixed in.

### P1 — correctness bugs a consumer hits immediately

**G1. `MauiHost.Session` reassignment is broken.**
The setter replaces `_session` before calling `UpdateContent`, and `UpdateContent` returns early when the
realized content is unchanged. So assigning a different session does nothing, assigning `null` does not
detach, and a later content change calls `Release` on the *new* session, which does not own the element.
Reparenting a host between windows corrupts state.
*Fix:* track the session that actually realized the content (`_realizedSession`) separately from the
assigned one; release through the realizing session; handle `null` by detaching; on `Loaded`, validate
that the host's `XamlRoot`/window still matches the session.
*Verify:* probe step that moves a host between two sessions and asserts the old session no longer tracks
the content.

**G2. The embedded window is located by guesswork.**
`MauiEmbeddingSession` takes `MauiApplication.Current.Windows[^1]` immediately after
`CreateEmbeddedWindowContext`, assuming nothing else appended a window. Anything that touches
`Application.Windows` during initialization, or a future change to when MAUI appends the window, silently
selects the wrong window — after which `Page`, `Created`/`Activated` and `Destroying` are applied to it.
*Fix:* use the exact lookup that already exists — `WindowExtensions.GetWindow(this UI.Xaml.Window)`
matches by `Handler.PlatformView` — and throw if it cannot be correlated instead of degrading to
view-level.
*Verify:* assert the located window's handler `PlatformView` is the session's platform window.

**G3. A second `Page` island cross-wires into the first island.**
Only the first page becomes `Window.Page`. Later page islands are still parented to the same
`EmbeddedWindow` by `ToPlatformEmbedded`, so they inherit its navigation proxy and alert manager: their
`DisplayAlertAsync` and `PushModalAsync` render into the **first** island's region. That is worse than
being unsupported, because it looks like it works.
*Fix:* throw from `MauiEmbeddingSession.Embed` when a second distinct `Page` is supplied, until per-island
window scopes exist.
*Verify:* probe step asserting the second page island throws rather than rendering into island one.

### P2 — lifetime and teardown

**G4. Root release is incomplete.**
`Release` clears `Window.Page` but never calls `NavigationRootManager.Disconnect()`, never unregisters the
`WindowRootViewContainer` from the window scope, and never drains the modal stack. Replacing a root while
a modal is open strands the modal in a detached container while `PopModalAsync` targets the new one.
*Fix:* make the container a session-owned root lease whose release disconnects the root manager, drains
and cancels the modal stack, clears the container's children, and only then clears `Window.Page`.
*Verify:* replace the root with zero, one and nested active modals.

**G5. Window lifecycle is synthetic and fires too early.**
`IWindow.Created()`/`Activated()` are raised from `Embed`, which on startup runs while the shell is still
being constructed — before `Window.Content` is assigned and before native activation. Deactivate, resume
and visibility changes are never relayed, and view-only sessions never receive them at all.
*Fix:* raise `Created` when the context is ready and `Activated` from the real native activation event,
then relay subsequent transitions; track each transition separately so a failure cannot leave the session
half-initialized.

**G6. `AlertManager` dispatch is not failure-safe.**
`TryEnqueue`'s return value is ignored, so a failed enqueue leaves the caller awaiting forever. A page
released while a request is queued never completes its arguments. `CurrentAlert`/`CurrentPrompt` are
cleared outside `finally`, so a `ShowAsync` failure can poison the queue, and action sheets are not
serialized with either queue. These affect MAUI-root Uno apps too, because the change is `#if UNO` rather
than embedding-only.
*Fix:* honour the enqueue result and fault or cancel the arguments; serialize all dialog types through one
window-owned queue; clear queue state in `finally`.

### P3 — API shape, required before any of this is upstreamable

**G7. `ToPlatformEmbeddedWindowRoot` is not a defensible public API yet.**
Its documented precondition — that the page is already the embedded window's `Page` — cannot be satisfied
through public API, because `CreateEmbeddedWindowContext` does not return the window it created. It also
silently no-ops its DI registration when `IMauiContext` is not a `MauiContext`, leaving a valid-looking
view whose modal push later throws, and it has no inverse for releasing the container it creates.
*Fix:* have `CreateEmbeddedWindowContext` return the created `EmbeddedWindow` (or expose
`EmbeddedWindowProvider`); throw rather than no-op on an unexpected `IMauiContext`; provide a disposable
handle for the created root. Consider making the primitive internal and shipping a higher-level
`session.EmbedWindowPage(page)` that returns the view plus an `IDisposable`.

### P4 — verification

**G8. The probe reports; it does not assert.**
`Tier2Probe` logs `False` and swallows exceptions rather than failing, has no timeouts around pushes, and
does not automatically cover prompts, action sheets or two-button alerts.
*Fix:* turn it into a pass/fail harness with timeouts that exits non-zero, and cover the whole dialog and
navigation surface.

**G9. WebAssembly window-level behaviour is unverified at runtime.**
The probe is enabled by an environment variable, which the browser head cannot set, and the WASM build
suppresses trimming warnings (`IL2xxx` in `MauiUnoSample.props`), so a clean build is weak evidence.
The most likely WASM-only failure is `ContentDialog.ShowAsync` never completing, which would hang every
dialog silently.
*Fix:* enable the probe from a query string or host configuration, and add a Release/trimmed WebAssembly
smoke run that fails on any false result.

### P5 — productization

**G10. There is no consumable package.** `MauiEmbeddingSession` and `MauiHost` are sample code, and
`MauiUnoSample.props` references MAUI source projects by repository-relative path. A consumer has no
package, SDK target or reusable host library.

**G11. `MauiHost` lacks parity with Uno's `MauiHost`** — no `DataContext` → `BindingContext` bridge, no
theme bridging, and no `Source` property, so XAML usage needs code-behind. `Source` was omitted
deliberately for trim safety; the binding-context bridge is simply missing.

**G12. Only Desktop and WebAssembly heads exist.** Android, iOS and Skia-desktop heads should also work
and are untested.

### Checked and found not to be a problem

Content replacement was reported as unsound, on the theory that a fresh `WindowRootViewContainer` per call
re-parents the single `NavigationRootManager.RootView` and violates XAML's single-parent rule. Testing
three consecutive page replacements shows the current page rendering each time with no stale content and
no exception — Uno's collection re-parents rather than throwing. The assertion is now part of the probe so
this cannot regress silently. It has **not** been checked on WebAssembly, where the collection may be
stricter.


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
PushAsync returned: OK (stack depth 2)
pushed page handler created: True
pushed page attached to XamlRoot: True
pushed page actually rendered: True
PopAsync: OK
off-UI-thread alert opened a popup: True
off-UI-thread alert completed without crashing: True
```

The modal and navigation assertions deliberately check the platform view rather than the stack depth:
MAUI records a push in the virtual stack even when the platform never realizes the page, so
`PushModalAsync` succeeding proves nothing on its own. The off-UI-thread check is a regression test for
the dialog threading crash; it runs last because it cannot dismiss its own dialog.


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
