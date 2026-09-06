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
require their corresponding .NET 10 workloads. Apple native builds and runtime
require macOS and a compatible Xcode installation. On Apple Silicon macOS the default
simulator/Catalyst RID is arm64; on Intel macOS it remains x64. Windows keeps
the existing Debug x64 / Release arm64 defaults. Use `-RuntimeIdentifier` to
override the default RID, for example:

```powershell
.\Build.ps1 -Target iOS -RuntimeIdentifier iossimulator-arm64 -Run
```

The automatic SDK path generates platform heads from one MAUI application
project:

```powershell
.\Build.ps1 -Sample Automatic -Target Desktop
.\Build.ps1 -Sample Automatic -Target Android -RuntimeIdentifier android-x64
.\Build.ps1 -Sample Automatic -Target WebAssembly -Publish
.\Build.ps1 -Sample Gallery -Target WebAssembly -Publish
```

The generated projects live under `obj/uno-maui-hosts`; applications do not
need to add or maintain Uno host projects.

### Apple native dependencies

Use an Xcode version supported by the selected .NET Apple SDK. A successful
restore or static-graph evaluation does not validate the native linker.

The published `Uno.icu-ios` archives target iOS devices and simulators, not
Mac Catalyst. Catalyst builds therefore compile ICU 77.1 from its upstream
source archive, checked against the release SHA-512, using Xcode's `macabi`
target. The build retains the complete Unicode data archive, caches output by
architecture, minimum OS version, compiler/SDK and recipe, and includes ICU's
license in the app. Neither the package cache nor iOS native assets are modified.
The first Catalyst build requires internet access, Bash, curl, make and Xcode;
later builds reuse the verified source and native-build cache.

`UnoMauiCatalystIcuDirectory` optionally selects a reusable native cache root.
Each configuration is published atomically to its own immutable keyed directory;
concurrent architectures never replace archives already selected by a linker.
`MAUI_ICU_BUILD_JOBS` controls native build parallelism (default: 4).

The cache regressions run without downloading or compiling ICU:

```powershell
pwsh -NoProfile -File src/Workload/Uno.Maui.Runtime/tests/BuildIcu.Tests.ps1
```

Mac Catalyst Keychain access also requires an appropriately provisioned signing
identity and entitlements. An ad-hoc signature (`CodesignKey=-`) is sufficient
for local rendering experiments, but does not establish that `SecureStorage`
works. The Essentials probe reports Keychain entitlement failures rather than
falling back to unencrypted storage. See
[Mac Catalyst capabilities](https://learn.microsoft.com/dotnet/maui/mac-catalyst/capabilities)
for signing and provisioning setup.

### Runtime regression probes

The sample's colors use app-theme bindings, including page surfaces, button
captions, formatted text and the drawing canvas. The runtime section includes
theme, RTL and safe-area toggles, and the Essentials probe verifies missing
secure-storage keys as well as round-trip/removal.

For an automated, in-process pass against the real rendered controls, build with
`MauiUnoRuntimeQa=true` and launch the app normally. This opt-in runs first-load
font layout and display-density sizing, entry/command, light/dark/light caption
and surface, post-load color/border updates without a theme change, RTL/LTR
alignment, font-pixel, screenshot and Essentials probes
after the page loads. The first-load assertions run before any capture or forced
layout, so screenshot rendering cannot conceal an image-loading defect. Results are written to
`FileSystem.CacheDirectory/uno-maui-runtime-qa.txt`; require the `COMPLETE` marker
and no `FAIL:` records. This does not replace physical pointer/touch, keyboard,
rotation or lifecycle testing. Normal builds do not run the probes automatically.

Release publishing is currently available for the heads that do not require
signing:

```powershell
.\Build.ps1 -Configuration Release -Target Desktop -Publish
.\Build.ps1 -Configuration Release -Target WebAssembly -Publish
```

The WebAssembly publish preserves the two CommunityToolkit assemblies in full
because the pinned Toolkit build is not trim-analysis clean.

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
`CommunityToolkit.Maui` Expander, interactive `DrawingView`, and a
file-system probe. A runtime diagnostics section also exercises the keyed Uno
screenshot backend, soft-input show/hide, XamlRoot metrics, theme propagation,
flow direction, and native window-handle availability.

The Essentials compatibility probe uses Uno-specific implementations for
`AppInfo`, `Browser`, `Clipboard`, `Connectivity`, `DeviceDisplay`, `Launcher`,
`Preferences`, `FileSystem`, and `SecureStorage`. `Connectivity.NetworkAccess` falls back to
`NetworkInterface.GetIsNetworkAvailable()` when a host does not project the
WinRT internet profile, while `ConnectivityChanged` reports unsupported
notifications explicitly on those heads. Browser clipboard operations report
unsupported behavior consistently instead of touching incomplete projections.
The Android head declares an HTTPS intent query so `Launcher.CanOpenAsync`
can report browser availability under Android package-visibility rules.
`SecureStorage` uses Uno's `PasswordVault` on Windows, iOS, and Mac Catalyst,
and intentionally throws on Android, browser, and desktop Linux/macOS hosts
instead of silently falling back to plain-text storage. `MainThread` remains
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
| Android x64 emulator | Builds, installs, relaunches in-process, handles input and DrawingView, and completes screenshot/runtime/Essentials probes |
| WebAssembly | Builds, renders, handles input and DrawingView, and completes screenshot/runtime/Essentials probes without browser errors |
| iOS simulator | Compiles; runtime requires macOS |
| Mac Catalyst x64 | Compiles; runtime requires macOS |
| X11, Linux framebuffer, macOS Desktop | Host registrations compile; runtime not yet exercised |

## Current limitations

- Uno-root MAUI embedding through `MauiHost` is not supported by this
  standalone application bootstrap.
- MAUI Essentials remains partial. `AppInfo`, `Clipboard`, `Connectivity`,
  `Browser`, `DeviceDisplay`, `Launcher`, `Preferences`, `FileSystem`,
  `SecureStorage`, and `MainThread` have Uno implementations. `SecureStorage` is available on Windows, iOS, and Mac
  Catalyst, and stays unsupported on Android, browser, and desktop Linux/macOS
  hosts. `DeviceDisplay` metrics are projected on every Uno head, while
  `KeepScreenOn` remains unsupported on desktop hosts. `DeviceInfo` is
  conservative; permissions, share/picker UI, communication APIs, and most
  sensors still use their portable unsupported implementations.
- Native HWND access is available on the Windows Uno host. Win32 message
  callbacks remain unavailable, and non-Windows heads do not expose native
  window handles. Window position, size, constraints, minimize, maximize, and
  restore use Uno's public `AppWindow` APIs where the host supports them;
  mobile and WebAssembly hosts may intentionally ignore desktop-only
  operations. X11 minimize, maximize, and restore are disabled until Uno can
  reliably deiconify and reactivate the window during restore.
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
