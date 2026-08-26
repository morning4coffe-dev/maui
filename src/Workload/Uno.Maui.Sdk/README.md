# Uno MAUI SDK

`Uno.Maui.Sdk` builds a neutral .NET MAUI application through generated Uno
Platform heads. The application project contains no Desktop, Android, or
WebAssembly bootstrap source.

The SDK keeps MAUI's WinUI handlers in the neutral `net10.0` application
assembly and generates target-specific projects under `obj/uno-maui-hosts`.
Generated projects are isolated per application and may be deleted safely.

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

## Package mode

Set `UnoMauiUseSource=false` to consume `Uno.Maui.Runtime`,
`Microsoft.Maui.Controls.Build.Tasks`, and `Microsoft.Maui.Resizetizer`
packages instead of MAUI source project references. `Uno.Maui.Runtime`
contains the neutral Uno-backed `Microsoft.Maui.*` assemblies.

The current generated targets are Desktop, Android, and WebAssembly. Native
MAUI and Uno-rendered outputs must remain separate build graphs because they
select different handler assemblies.
