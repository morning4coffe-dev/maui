# Minimal Uno-root MAUI embedding

The smallest app that hosts embedded .NET MAUI content inside a plain Uno application on WebAssembly.

The [gallery sample](../Controls.Sample.Embedding.Uno/README.md) next door exists to *map* what works: it
has multiple islands, a control census, third-party libraries and a Tier 2 probe. This one exists to show
what an app actually has to write. Five source files, one project, no shared project and no other heads.

## The four moving parts

Everything specific to embedding is in `MinimalUnoApp.cs`, numbered in the source:

1. **`MauiEmbeddingSession.UseMauiApp(MauiProgram.CreateMauiApp)`** — in the `Application` constructor, says
   how the embedded `MauiApp` is built. Registering is cheap: the `MauiApp` itself is built lazily on the UI
   thread when the first island is realized, which is the only point at which MAUI's bootstrap requirements
   are met.
2. **`MauiEmbeddingSession.GetOrCreate(window)`** — one session per Uno window. It owns the MAUI window
   scope that alerts, modals and navigation need. It has to be created in `OnLaunched`, on the UI thread and
   after `Application.Current` exists, because MAUI's embedding bootstrap captures `Application.Current`
   while the `MauiApp` builder is being configured.
3. **`new MauiHost { Session = …, MauiContent = new MinimalMauiContent() }`** — hands a MAUI element to Uno.
   From that point down it is an ordinary Uno visual tree, because the MAUI handlers in this repository are
   compiled against `Uno.WinUI` and emit Uno `UIElement`s directly. There is no interop boundary and no
   second renderer.
4. **Relaying `Window.Activated`** to `NotifyWindowActivated` / `NotifyWindowDeactivated`. MAUI's
   window-scoped services wait on real activation, so it is relayed from the native window rather than
   raised while the content is still being constructed.

`MinimalMauiContent.cs` is deliberately *not* Uno-aware. It is an ordinary MAUI `ContentView` written the
ordinary way — a `Label`, a `Button` with a `Clicked` handler, and an `Entry` with `TextChanged`. The only
thing that differs from a normal MAUI app is who owns the window.

## Two things that are easy to get wrong

Both were found by running this sample, and both look like "the app is blank" rather than like an error.

- **Give the root a themed background *and* foreground.** Uno resolves a plain `TextBlock`'s default
  foreground from the application theme only once it inherits one. A bare `StackPanel` as `Window.Content`
  inherits nothing, so its text — and any MAUI `Label` with no explicit `TextColor` — renders white on a
  light background and looks like it never painted. The sample wraps its panel in a `UserControl` and
  applies `ApplicationPageBackgroundThemeBrush` and `TextFillColorPrimaryBrush` explicitly.
- **Wrap the content construction in a `try`/`catch`.** On a platform with no attached debugger, an
  exception thrown while building the island leaves a blank window and nothing else. The sample renders the
  exception into the window instead, and also traces `Application.UnhandledException`.

## Build and run

```powershell
dotnet publish src\Controls\samples\Controls.Sample.Embedding.Uno.Minimal\Controls.Sample.Embedding.Uno.Minimal.WebAssembly.csproj -c Release
```

Then serve the published `wwwroot` with any static server that returns `application/wasm` for `.wasm`, and
open it. `dotnet run` serves build output rather than publish output, so publish is what to serve when
checking the trimmed configuration.

**Delete the `publish` directory before republishing.** Uno's `GenerateUnoWasmAssets` runs before the SDK
recomputes static-web-asset fingerprints, so an incremental publish can leave `uno-config.js` pointing at a
previous `dotnet.<hash>.js`. The app then boots the *old* assemblies, which presents as source changes
having no effect.

## Verified

Trimmed Release WebAssembly publish, headless Chromium: the Uno chrome and the embedded MAUI panel both
render, and clicking the MAUI `Button` through a real browser mouse event advances its counter.

One environmental note that is not a fault in the app: Uno removes the bootstrapper splash from a
`CompositionTarget.FrameRendered` callback scheduled after font preload completes. A completely static app
can produce no further frame, in which case the splash stays up and covers the canvas. Any real interaction
— or any animated content — clears it.
