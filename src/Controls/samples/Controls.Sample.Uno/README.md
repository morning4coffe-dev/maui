# MAUI WinUI handlers on Uno

This sample runs MAUI's existing Windows handlers on Uno Platform's Skia
Win32 host. It does not register a separate renderer or replace MAUI's handler
set.

## Build and run

From the repository root:

```powershell
dotnet build Microsoft.Maui.BuildTasks.slnf
dotnet build src\Controls\samples\Controls.Sample.Uno\Controls.Sample.Uno.csproj
dotnet run --no-build --project src\Controls\samples\Controls.Sample.Uno\Controls.Sample.Uno.csproj
```

The sample project sets `MauiUnoTarget=true`. That opt-in:

- compiles MAUI's Windows source files for `net10.0`;
- replaces Windows App SDK references with `Uno.WinUI`;
- compiles MAUI's Windows XAML resource dictionaries with Uno's source
  generator;
- uses `SkiaSharp.Views.Uno.WinUI` for MAUI Graphics views; and
- hosts `MauiWinUIApplication` through Uno's Win32 host.

## Current scope

The sample exercises labels, formatted-text fallback, font images, entry input,
buttons, stack and scroll layouts, slider, progress bar, window creation,
resources, focus, and property mapper updates.

Known limitations:

- MAUI Essentials uses its portable `net10.0` implementation.
- HWND-specific window operations are no-ops on the Uno target.
- Arbitrary MAUI clip paths currently fall back to rectangular composition
  clips because Uno's path-geometry interop is internal.
- Formatted label spans currently fall back to plain text because Uno 6.5's
  WinUI inline/highlighter path is not reliable on Skia.
- Only the Win32 Skia host is wired into this sample.
