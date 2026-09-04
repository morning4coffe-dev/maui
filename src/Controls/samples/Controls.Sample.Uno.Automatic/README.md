# Automatic MAUI on Uno

This package-only sample has one application project and no platform heads.
`Uno.Maui.Sdk` generates Desktop, Android, and WebAssembly hosts under `obj`.

The sample validates:

- package consumption without MAUI source project references;
- XAML compilation;
- generated platform entry points and manifests;
- generated-host image, font, and raw-asset packaging;
- opt-in WebAssembly accessibility for automation;
- MAUI input and event dispatch through Uno.

Build it from the renderer root:

```powershell
.\Build.ps1 -Sample Automatic -Target Desktop
.\Build.ps1 -Sample Automatic -Target Android -RuntimeIdentifier android-x64
.\Build.ps1 -Sample Automatic -Target WebAssembly -Publish
```
