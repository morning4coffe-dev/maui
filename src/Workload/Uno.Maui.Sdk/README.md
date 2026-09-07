# Uno MAUI SDK

`Uno.Maui.Sdk` builds a neutral .NET MAUI application through generated Uno
Platform heads. The application project contains no Desktop, Android, or
WebAssembly bootstrap source.

The SDK keeps MAUI's WinUI handlers in the neutral `net10.0` application
assembly and generates target-specific projects under `obj/uno-maui-hosts`.
Generated projects are isolated per application and may be deleted safely.
Before a generated head is restored, the SDK writes a deterministic resource
manifest under the application's intermediate output. `MauiImage`, `MauiIcon`,
`MauiSplashScreen`, `MauiAsset`, and `MauiFont` items (including logical names
and font aliases) are then processed by the target-specific generated head.
Missing files and duplicate image, asset, font, or alias logical names fail the
build.

The application is built as a library with `CopyLocalLockFileAssemblies=true`
for generated-host builds. Its runtime package dependencies are then included
in the host, without replacing the host's framework or existing package
assemblies. This is required for dependencies used during startup, such as
logging providers, SQLite, and Blazor services.

WebAssembly `NativeFileReference` items also cross the generated-host boundary,
so package-provided archives such as `e_sqlite3.a` reach the native linker.
The generated browser host enables IndexedDB-backed filesystem persistence
by default. Set `WasmShellEnableIDBFS=false` only for applications that do not
need local files to survive a page reload.
Native libraries must also match the .NET WebAssembly toolchain. For example,
the TodoSQLite sample's transitive SQLitePCLRaw 2.1.2 browser archive loads but
fails with disk I/O errors on .NET 10; explicitly selecting
`SQLitePCLRaw.bundle_green` 2.1.11 supplies a compatible archive.

Package-only hosts activate the same Uno asset pipeline as source-based hosts.
Resource projection preserves inherited metadata, including renamed nested
asset paths, font aliases, image sizing and icon/splash settings.

The metadata regression checks require only MSBuild, not platform workloads:

```powershell
dotnet msbuild src/Workload/Uno.Maui.Sdk/tests/GeneratedResources.Tests.proj
dotnet msbuild src/Workload/Uno.Maui.Sdk/tests/GeneratedResources.Tests.proj -p:DeduplicateResources=true
```

## Application project

Import the SDK props before application items and the targets at the end:

```xml
<Import Project="path\to\Uno.Maui.Sdk.props" />

<PropertyGroup>
  <UnoMauiAppFactoryType>MyApp.MauiProgram</UnoMauiAppFactoryType>
  <UnoMauiApplicationId>com.example.myapp</UnoMauiApplicationId>
</PropertyGroup>

<Import Project="path\to\Uno.Maui.Sdk.targets" />
```

`MauiProgram.CreateMauiApp()` must be public and static. Set
`UnoMauiAppFactoryType` when it does not use the project root namespace.
Set `UnoMauiHostTemplateRoot` to replace a generated host completely when an
application needs custom platform lifecycle code.

WebAssembly accessibility remains disabled by default. Automated UI projects
can opt in before the generated host is compiled:

```xml
<UnoMauiAutoEnableAccessibility>true</UnoMauiAutoEnableAccessibility>
```

This sets `FeatureConfiguration.AutomationPeer.AutoEnableAccessibility` before
the WebAssembly host builder is created.

Startup phase timing is also opt-in:

```xml
<UnoMauiEnableStartupTracing>true</UnoMauiEnableStartupTracing>
```

When enabled, the WebAssembly entry point writes structured `phase` and
`elapsed_ms` fields around host build and run. Native WebAssembly compilation
is enabled by default to keep MAUI startup responsive. Set `WasmBuildNative`
to `false` explicitly when faster inner-loop builds are more important than
runtime startup.

Generated WebAssembly hosts use partial trimming by default because MAUI's
XAML and handler graph is not full-trim safe. Applications that have completed
their trimming annotations can set `UnoMauiTrimMode` to `full`.

Generated WebAssembly hosts also optimize and link application assemblies,
disable browser debugger payloads, and clean their isolated publish directory
by default. Set `UnoMauiOptimizeWasmRuntime` to `false` when source-level
browser debugging is required.

Uno's Skia native text overlay currently ignores ancestor transforms when it
positions Android input views and WebAssembly accessibility proxies. Nested
text controls can therefore expose a touch or accessibility rectangle near
the page origin instead of at their rendered position. This is tracked in
[unoplatform/uno#24280](https://github.com/unoplatform/uno/issues/24280).

Generated hosts can test independently versioned Uno runtime packages without
changing the rest of the Uno package graph:

```xml
<UnoMauiCoreRuntimeVersion>6.8.0-dev.123</UnoMauiCoreRuntimeVersion>
<UnoMauiAndroidRuntimeVersion>6.8.0-dev.123</UnoMauiAndroidRuntimeVersion>
<UnoMauiWasmRuntimeVersion>6.8.0-dev.123</UnoMauiWasmRuntimeVersion>
```

Each property defaults to `UnoMauiUnoVersion`. The core override is useful when
a platform runtime fix also requires a matching `Uno.WinUI` runtime assembly.

Set `UnoMauiWasmAot` to `true` for Release applications whose startup graph is
too large for the WebAssembly interpreter. The generated host imports the
installed .NET WebAssembly AOT task pack and enables Uno's
`InterpreterAndAOT` execution mode.

## Build

From the renderer root:

```powershell
.\Build.ps1 -Sample Automatic -Target Desktop
.\Build.ps1 -Sample Automatic -Target Android -RuntimeIdentifier android-x64
.\Build.ps1 -Sample Automatic -Target WebAssembly -Publish
```

Use `-Sample Gallery` to build the existing `Maui.Controls.Sample` project
directly, including its complete page and dependency graph.

The underlying MSBuild targets are also callable directly:

```powershell
dotnet msbuild MyApp.csproj -t:BuildUnoMaui -p:UnoMauiTarget=Desktop
dotnet msbuild MyApp.csproj -t:BuildUnoMaui -p:UnoMauiTarget=Android -p:UnoMauiRuntimeIdentifier=android-x64
dotnet msbuild MyApp.csproj -t:PublishUnoMaui -p:UnoMauiTarget=WebAssembly
```

Run the generated-host runtime asset contract checks without restoring packages:

```powershell
dotnet msbuild src\Workload\Uno.Maui.Sdk\tests\RuntimeAssets.proj
```

## Package mode

Set `UnoMauiUseSource=false` to consume `Uno.Maui.Runtime`,
`Microsoft.Maui.Controls.Build.Tasks`, and `Microsoft.Maui.Resizetizer`
packages instead of MAUI source project references. `Uno.Maui.Runtime`
contains the neutral Uno-backed `Microsoft.Maui.*` assemblies.

The current generated targets are Desktop, Android, and WebAssembly. Native
MAUI and Uno-rendered outputs must remain separate build graphs because they
select different handler assemblies.

For WebAssembly, `BuildUnoMaui` prepares the application's build static-web-
asset manifest and `PublishUnoMaui` also prepares its publish manifest. This
allows application and referenced-package assets, including fingerprinted
library initializers, to flow through the generated head without manual
`_content` copies. Hot Reload is disabled for generated WebAssembly hosts unless
explicitly enabled, so its browser initializer is not emitted accidentally.
