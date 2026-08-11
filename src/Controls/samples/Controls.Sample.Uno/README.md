# MAUI WinUI handlers on Uno

This sample runs one shared MAUI application through MAUI's existing Windows
handlers and hosts that WinUI surface on Uno Platform. It does not register a
second renderer or compile MAUI's Android, iOS, or Mac Catalyst handlers.

The sample owns the application root through `MauiWinUIApplication`. It is
separate from Uno's `EmbeddingApplication`/`MauiHost` integration; combining
both bootstrap models would create competing application scopes, windows, and
`IPlatformApplication.Current` ownership.

The handler assemblies remain plain `net10.0`; each Uno head supplies the
platform host:

| Project | Uno host | Target |
| --- | --- | --- |
| `Shared` | Shared `MauiWinUIApplication` and MAUI controls | `net10.0` |
| `Desktop` | Win32, X11, Linux framebuffer, macOS | `net10.0` |
| `Android` | Skia Android | `net10.0-android36.0` |
| `Apple` | Apple UIKit | `net10.0-ios26.0`, `net10.0-maccatalyst26.0` |
| `WebAssembly` | Skia WebAssembly browser | `net10.0` |

## Build and run

When this fork is checked out through `uno.maui.renderer`, use its root wrapper:

```powershell
.\Build.ps1
.\Build.ps1 -Target Android -Run
.\Build.ps1 -Target WebAssembly -Run
.\Build.ps1 -Target iOS
.\Build.ps1 -Target MacCatalyst
```

`Desktop` is the default target. Android, iOS, Mac Catalyst, and WebAssembly
require their corresponding .NET 10 workloads. Apple heads compile on Windows,
but running them requires macOS. Use `-RuntimeIdentifier` to override the
default x64 simulator or desktop RID, for example:

```powershell
.\Build.ps1 -Target iOS -RuntimeIdentifier iossimulator-arm64 -Run
```

Release publishing is currently available for the heads that do not require
signing and have a trim-clean sample:

```powershell
.\Build.ps1 -Configuration Release -Target Desktop -Publish
```

WebAssembly publish remains blocked on trimming warnings in the Toolkit sample.

## Implementation

`MauiUnoSample.props` keeps every MAUI source reference on `net10.0` with
`MauiUnoTarget=true`, propagates the selected RID through restore and build,
prevents the platform heads from pulling MAUI's native workload source sets,
and serializes the explicit project-reference graph so repeated framework
references cannot write the same output concurrently.

The Uno packages are temporarily pinned to `6.7.0-dev.704`. That build contains
the upstream WebAssembly startup-race fix that avoids rendering before Uno has
created its root window.

`CommunityToolkit.Maui` is consumed through exact `PackageDownload` items and
explicit `lib/net10.0` references. This keeps every head on the neutral
implementation instead of letting Android or Apple select the Toolkit's native
MAUI assets. The normal `UseMauiCommunityToolkit()` initializer is
intentionally not called because it eagerly registers neutral platform-handler
stubs. The sample registers its own Uno-compatible `DrawingView` handler,
which renders through MAUI's Skia-backed `PlatformTouchGraphicsView`.

The sample exercises labels, formatted text, font images, entry input,
buttons, stack and scroll layouts, slider, progress bar, window creation,
resources, focus, property mapper updates, and a package-only
`CommunityToolkit.Maui` Expander and interactive `DrawingView`.

The Essentials compatibility probe uses Uno-specific implementations for
`AppInfo`, `Clipboard`, `Connectivity`, and `Preferences`. `MainThread` remains
bridged to the MAUI dispatcher. The probe reports APIs that still use the
portable unsupported implementation instead of hiding the gap.

The window operations probe exercises MAUI minimum and maximum dimensions
through Uno's `OverlappedPresenter` constraints and verifies maximize/restore
without requiring a native HWND.

Rounded rectangle clips preserve independent corner radii through Uno's public
`RectangleClip` API. Other `IShape` clips continue to use their rectangular
bounds until Uno exposes a public geometry source for `CompositionPath`.

## Validation status

| Target | Status |
| --- | --- |
| Windows Desktop | Builds, launches, and handles input |
| Android x64 emulator | Builds, installs, launches, and toggles the Toolkit Expander |
| WebAssembly | Builds, renders, and toggles the Toolkit Expander without browser errors |
| iOS simulator | Compiles; runtime requires macOS |
| Mac Catalyst x64 | Compiles; runtime requires macOS |
| X11, Linux framebuffer, macOS Desktop | Host registrations compile; runtime not yet exercised |

## Current limitations

- Uno-root MAUI embedding through `MauiHost` is not supported by this
  standalone application bootstrap.
- MAUI Essentials remains partial. `AppInfo`, `Clipboard`, `Connectivity`,
  `Preferences`, and `MainThread` have Uno implementations; `DeviceInfo`,
  `FileSystem`, `SecureStorage`, permissions, and most sensors still use their
  portable unsupported implementations.
- Native window handles and Win32 message callbacks remain unavailable. Window
  position, size, constraints, minimize, maximize, and restore use Uno's public
  `AppWindow` APIs where the host supports them; mobile and WebAssembly hosts
  may intentionally ignore desktop-only operations. X11 minimize, maximize,
  and restore are disabled until Uno can reliably deiconify and reactivate the
  window during restore.
- Arbitrary non-rectangular MAUI paths still fall back to rectangular
  composition clips because Uno's path-geometry interop is internal. Rounded
  rectangles, including independent corner radii, are preserved.
- Formatted label spans preserve per-span fonts, foreground colors, character
  spacing, and text decorations. Span background colors render on Skia heads;
  span gesture hit testing remains unavailable because Uno does not implement
  the required `TextPointer` APIs.
- Other third-party controls with custom handlers still need their Windows
  sources rebuilt or adapted for `MauiUnoTarget`; neutral NuGet assets commonly
  contain unsupported platform stubs.
