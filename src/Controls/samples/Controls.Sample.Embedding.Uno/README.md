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

No new MAUI API was needed for this sample; it is built entirely on the public embedding surface.

## Structure

| File | Role |
| --- | --- |
| `Shared/UnoEmbeddingApplication.cs` | Uno application root — a plain `Application`, not `MauiWinUIApplication` |
| `Shared/MainShell.cs` | Uno-owned UI; MAUI islands interleaved with Uno content |
| `Shared/Embedding/MauiEmbeddingSession.cs` | One `MauiApp` per process, one `IMauiContext` per `Window`, exactly-once teardown |
| `Shared/Embedding/MauiHost.cs` | `ContentControl` that realizes a MAUI element into the Uno tree |
| `Shared/MauiProgram.cs`, `Shared/App.cs`, `Shared/MyMauiContent.cs` | The embedded MAUI app and its content |

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

## Supported today (Tier 1: view-level embedding)

Verified running on Windows Desktop: rendering, layout, input, property mappers, app-level MAUI
resources resolved through the logical tree, multiple hosts sharing one window context, content
replacement, and detach/re-attach.

## Not supported (Tier 2: window-level semantics)

These fail because MAUI's Windows implementation assumes it owns the window:

- **Modal navigation** — `ModalNavigationManager.Windows.cs` requires
  `platformWindow.Content is WindowRootViewContainer`; under an Uno root that is the Uno tree.
- **`DisplayAlert` / prompt / action sheet** — `AlertManager.Subscribe()` only runs from
  `Window.OnPageHandlerChanged`, and embedding never assigns `Window.Page`, so the awaited task
  never completes.
- **Shell / NavigationPage chrome, toolbars, title bars** — `EmbeddedWindowHandler` deliberately
  omits the `NavigationRootManager.Connect` that `WindowHandler` performs.
- **`WindowOverlay`, visual diagnostics, MAUI hot reload** — same root-container assumption.
- **`CreateWindow`, `Application.Current.MainPage`, `OpenWindow`** — embedding creates a synthetic
  `EmbeddedWindow` whose `IWindow.Content` is always `null`.

Closing Tier 2 means hosting `NavigationRootManager.RootView` inside the `MauiHost` and resolving the
modal container from the window scope rather than from `Window.Content`.

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
