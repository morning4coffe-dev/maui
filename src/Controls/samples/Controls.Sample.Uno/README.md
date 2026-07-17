# MAUI WinUI handlers on Uno

This sample runs one shared MAUI application through MAUI's existing Windows
handlers and hosts that WinUI surface on Uno Platform. It does not register a
second renderer or compile MAUI's Android, iOS, or Mac Catalyst handlers.

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

## Implementation

`MauiUnoSample.props` keeps every MAUI source reference on `net10.0` with
`MauiUnoTarget=true`, propagates the selected RID through restore and build,
and prevents the platform heads from pulling MAUI's native workload source
sets.

The Uno packages are temporarily pinned to `6.7.0-dev.704`. That build contains
the upstream WebAssembly startup-race fix that avoids rendering before Uno has
created its root window.

The sample exercises labels, formatted-text fallback, font images, entry input,
buttons, stack and scroll layouts, slider, progress bar, window creation,
resources, focus, and property mapper updates.

## Validation status

| Target | Status |
| --- | --- |
| Windows Desktop | Builds, launches, and handles input |
| Android x64 emulator | Builds, installs, launches, and handles button input |
| WebAssembly | Builds, renders, and handles entry/button input without browser errors |
| iOS simulator | Compiles; runtime requires macOS |
| Mac Catalyst x64 | Compiles; runtime requires macOS |
| X11, Linux framebuffer, macOS Desktop | Host registrations compile; runtime not yet exercised |

## Current limitations

- MAUI Essentials uses its portable `net10.0` implementation.
- HWND-specific window operations are no-ops on the Uno target.
- Arbitrary MAUI clip paths fall back to rectangular composition clips because
  Uno's path-geometry interop is internal.
- Formatted label spans fall back to plain text because the current Uno Skia
  inline/highlighter path is not reliable for this handler projection.
