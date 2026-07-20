# Native-backed MAUI handlers experiment handoff

- **Handoff date:** 2026-07-20
- **Status:** Experimental implementation complete through the planned probes; Android evidence collected; Apple execution pending
- **Base commit:** `0395a53b66f85d7b6fd6732f9b2f8a50eb7d70cb`
- **Original worktree:** `D:\Projects\maui-native-handler-experiment`
- **Original branch:** `experiment/native-handler-batching-phase0`
- **Temporary publication branch:** `temp/native-backed-handlers-handoff-20260720`

## Provenance

This document extracts the durable context from the Copilot session titled
`Research Maui Handlers Integration`.

- Cloud session: `e473147b-fc21-4585-b42d-15480de4b12a`
- Cloud task: `f2ecb878-102f-4187-9e57-64b71001aa81`
- Local evidence folder used by that session:
  `C:\Users\t-dotitl\.copilot\session-state\dc82b8fa-2188-4583-aa32-600d5ac78521`

The raw logs are not committed. Their important results and caveats are transcribed below so another
agent can continue from the branch alone.

## Original question

Explore a React-Native-like direction for MAUI handlers:

1. Move more platform behavior into native Swift/Kotlin/Java.
2. Marshal coarse or batched state from .NET instead of crossing the interop boundary once per
   property.
3. Determine whether that makes SwiftUI and Jetpack Compose easier to adopt later.
4. Ground the work in MAUI, React Native, Flutter, Uno, Swift, and Kotlin source rather than
   speculation.
5. Preserve MAUI behavior, mapper extensibility, and handler lifecycle semantics.

The work was intentionally split into three independent hypotheses. Success in one does not imply
success in the others.

| Hypothesis | Question | Current verdict |
|---|---|---|
| H1: interop batching | Does reducing managed/native crossings measurably improve handler work? | **GO on Android explicit transactions. Apple undecided.** |
| H2: native-owned logic | Can native code own bounded behavior without breaking MAUI extensibility/lifecycle? | **GO as an incremental pattern.** |
| H3: SwiftUI/Compose backing | Can declarative native controls replace MAUI platform controls with useful parity? | **Hosting is feasible; production replacement is NO-GO today.** |

## Architecture conclusions

### MAUI today

`ElementHandler.SetVirtualView` creates/connects the platform view and then runs the property mapper.
The mapper invokes one delegate for every mapped property. A typical delegate performs one platform
call through JNI, Objective-C dispatch, or WinRT.

MAUI also has a `CommandMapper`, which is useful for explicit transaction boundaries and other
coarse-grained operations.

### Existing Android precedent

MAUI already implements a narrow version of the proposal on Android:

- `ViewHandler.ViewMapper` registers the synthetic `_InitializeBatchedProperties` key only on Android.
- `ViewExtensions.Initialize` calculates initial values in C#.
- One JNI call to `PlatformInterop.Set(...)` applies roughly 16 base-view values.
- Individual default mappers skip their duplicate work while the handler is connecting.
- User mapper replacement/append/prepend behavior remains available.

The original implementation was dotnet/maui#3372. Its motivation was to cross from C# into Java once
instead of several times. It reported roughly 30% faster handler attachment for several controls and
about 17 ms in its sample startup path.

### Important cross-platform correction

Only Android has a true unrelated-properties-to-one-native-call batch.

- iOS consolidates transform/layout work, but does not have Android-style initial property batching.
- Windows also consolidates transforms. There is no current `WindowsBatchPropertyMapper` symbol.

Earlier research notes that described Windows as having the same batch were corrected. The strongest
evidence for native call batching is Android, where JNI transitions are comparatively expensive.

### React Native patterns worth borrowing

React Native's legacy UI manager and Fabric architecture reinforce these principles:

1. Accumulate operations until an explicit commit boundary.
2. Apply a transaction atomically on the UI thread.
3. Send typed desired state or mutations rather than repeated string-keyed calls.
4. Diff immutable desired state and avoid reapplying unchanged values.
5. Measure real commit/mount duration; crossing count alone is not a success metric.

The Phase 2 design follows the explicit-transaction part of this model. It deliberately does not defer
arbitrary MAUI property setters to the next frame because MAUI mapper updates are synchronously
observable today.

### Flutter and Uno contrast

Flutter and Uno Skia reduce interop by owning rendering. That is not this experiment. This branch stays
native-widget-backed so native accessibility, text, IME, and platform behavior remain available.

The relevant axis is crossing frequency and ownership of bounded platform behavior, not replacing all
native widgets with a custom renderer.

### Swift and Kotlin interop constraints

- SwiftUI generic `View` values are not directly bindable through MAUI's Objective-C binding pipeline.
  The viable current shape is a concrete `@objc` wrapper around `UIHostingController<Content>`.
- Kotlin classes are JVM-bindable, but `@Composable` methods are compiler-plugin transformed and
  cannot be called directly from C#. The viable shape is a Kotlin wrapper exposing ordinary JVM
  methods around a `ComposeView`.
- Full Swift type binding remains a broader runtime/tooling problem (dotnet/runtime#95638).
- First-class Compose library tooling remains a broader Android tooling problem
  (dotnet/android-libraries#352).

## Implemented phases

## Phase 0: measurement gate

Added `src\Core\tests\DeviceBenchmarks` as an opt-in device benchmark host.

The harness measures:

- Handler connection through first platform layout.
- Monotonic wall-clock duration.
- Managed allocations on the UI thread.
- Android UI-thread CPU time.
- Stable `MAUIBENCH` metadata, raw samples, and summaries.

Scenarios include base `ContentView`/`Border` work and an Android explicit steady-state property
transaction. It uses 20 warmups and 100 measured iterations.

Nonclaims:

- It does not measure Java/native allocations.
- It does not claim app-startup performance.
- Apple timing is not validated until the project runs on macOS/Xcode.
- Android and Apple values should only be compared within the same platform/harness variant.

Key files:

- `src\Core\tests\DeviceBenchmarks\Core.DeviceBenchmarks.csproj`
- `src\Core\tests\DeviceBenchmarks\HandlerBenchmarkRunner.cs`
- `src\Core\tests\DeviceBenchmarks\HandlerBenchmarkRunner.Android.cs`
- `src\Core\tests\DeviceBenchmarks\HandlerBenchmarkRunner.iOS.cs`
- `src\Core\tests\DeviceBenchmarks\HandlerSteadyStateBenchmarkTests.Android.cs`

## Phase 1: Apple initial property batching

Added an opt-in Swift/UIKit batch through MAUI's existing Xcode framework and Objective-C binding
pipeline.

Switch:

`Microsoft.Maui.Experimental.NativeViewPropertyBatching`

Benchmark environment variable:

`MAUI_NATIVE_VIEW_PROPERTY_BATCHING`

The first batch intentionally covers only safe primitive operations. Managed behavior remains
responsible for collapse/inflate, flow-direction propagation, mapper extensions, container handling,
and reconnect semantics. Transforms and size mappings were excluded because their frame/parent
preconditions make connect-time application unsafe.

Key files:

- `src\Core\AppleNative\PlatformInterop\MauiPlatformInterop\ViewPropertyBatcher.swift`
- `src\Core\AppleNative\ApiDefinition.cs`
- `src\Core\src\Handlers\View\ViewHandler.cs`
- `src\Core\src\Handlers\View\ViewHandler.iOS.cs`
- `src\Core\src\Platform\iOS\ViewExtensions.cs`
- `src\Core\tests\DeviceTests\Handlers\View\ViewHandlerTests.iOS.cs`
- `src\Core\tests\DeviceTests\Handlers\ContentView\ContentViewTests.iOS.cs`

Source and selector parity were reviewed. The Swift framework, generated binding, device behavior, and
performance have not been compiled or run on Apple in this work.

## Phase 2: Android explicit steady-state batching

Added default-off Android batching inside existing nested `VisualElement.BatchBegin` /
`VisualElement.BatchCommit` boundaries.

Switch:

`Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled`

Benchmark environment variable:

`MAUI_NATIVE_VIEW_PROPERTY_UPDATE_BATCHING`

Eligible fields:

- `IsEnabled`
- `Opacity`
- `TranslationX` / `TranslationY`
- `Scale` / `ScaleX` / `ScaleY`
- `Rotation` / `RotationX` / `RotationY`

Excluded fields remain synchronous:

- Visibility and flow direction.
- Minimum sizes and pivots.
- Layout/frame.
- Focus, images, and async/service-backed properties.

Semantics:

- Outside an explicit batch, mapper behavior is unchanged and synchronous.
- Nested batches flush only at the outer commit.
- The handler tracks a dirty mask and reads final desired values at commit.
- One Java `PlatformInterop.updateViewProperties(...)` call applies the dirty fields.
- Enabled state targets the platform view.
- Opacity/transforms target the current container-aware platform view.
- Wrapper shadow invalidation is preserved.
- Pending state is cleared before JNI so a failure cannot leave batching stuck.
- `BatchCommitted` remains guaranteed through `finally`.
- Animation batching now uses exception-safe begin/commit handling.
- Mapper append/replace behavior, reconnects, containers, empty batches, exceptions, and NaN values
  have targeted coverage.

Key files:

- `src\Controls\src\Core\VisualElement\VisualElement.cs`
- `src\Controls\src\Core\VisualElement\VisualElement.Android.cs`
- `src\Controls\src\Core\AnimationExtensions.cs`
- `src\Core\src\RuntimeFeature.cs`
- `src\Core\src\Handlers\View\ViewHandler.cs`
- `src\Core\src\Handlers\View\ViewHandler.Android.cs`
- `src\Core\AndroidNative\maui\src\main\java\com\microsoft\maui\PlatformInterop.java`
- `src\Core\tests\DeviceTests\Handlers\View\ViewHandlerTests.Android.cs`

### Android benchmark evidence

Device and scope:

- API 36 x86_64 emulator `emulator-5580`.
- Debug build.
- 100 explicit nested transactions per measured iteration.
- Three batching-off and three batching-on executions.
- Every execution reported two tests run, two passed, zero failed.

Median of the three run-level summaries:

| Metric | Batching off | Batching on | Delta |
|---|---:|---:|---:|
| Mean duration | 30,373.053 us | 23,985.747 us | -21.03% |
| p50 duration | 25,769.500 us | 19,625.400 us | -23.84% |
| p95 duration | 57,413.800 us | 44,337.600 us | -22.78% |
| Mean UI-thread CPU | 29,913.984 us | 23,932.321 us | -20.00% |
| Managed allocation | 88,800 bytes | 88,800 bytes | 0.00% |

One batching-off run was a large emulator outlier. Treat this as a positive decision signal, not a
production performance claim. Repeat on physical Android hardware with a Release build and a
representative animation/layout workload.

## Phase 3: bounded native-owned Button behavior

Added an Android Java-owned Button event bridge.

Switch:

`Microsoft.Maui.RuntimeFeature.IsNativeButtonEventBridgeEnabled`

Java owns:

- Click/touch listener registration.
- `MotionEvent` translation to pressed/released semantics.
- Attach/detach lifecycle.

C# receives typed click, pressed, and released callbacks into the current `IButton`.

Important design limits:

- Focus remains under MAUI's existing base handler path.
- `onTouch` returns false so existing ripple/click behavior remains available.
- Java stores weak references; C# holds and clears the callback peer during disconnect.
- The unused legacy/native path does not allocate extra JNI peers eagerly.
- Mapper append behavior, reconnects, stale callbacks, containers, and fallback behavior are tested.

Key files:

- `src\Core\AndroidNative\maui\src\main\java\com\microsoft\maui\ButtonEventCallback.java`
- `src\Core\AndroidNative\maui\src\main\java\com\microsoft\maui\MauiButtonEventBridge.java`
- `src\Core\src\Handlers\Button\ButtonHandler.Android.cs`
- `src\Core\tests\DeviceTests\Handlers\Button\ButtonHandlerTests.Android.cs`

This is evidence for moving bounded lifecycle/translation logic native. It is not evidence that an
entire MAUI control contract should move into an opaque native implementation.

## Phase 4: Compose and SwiftUI hosting probes

### Compose

Added an isolated Kotlin/Compose module:

`src\Core\AndroidNative\maui-compose-probe`

Enable it with:

`MauiEnableComposeButtonExperiment=true`

The experiment is excluded from default Core builds. The wrapper is a `FrameLayout` containing the
final `ComposeView` and exposes ordinary JVM methods for:

- Text.
- Enabled state.
- Semantics description.
- Automation ID.
- Click, pressed, and released callbacks.
- Composition lifecycle and diagnostic size.

The handler reconnects to the same wrapper and explicitly disposes composition on disconnect. The
headless device-test host must call `ComponentActivity.InitializeViewTreeOwners()` because production
`SetContentView` normally installs the lifecycle/saved-state owners and the test host does not.

Build/device evidence:

- Compose Debug AAR: passed.
- Compose Release AAR: passed.
- Clean default-off Core device-test package: passed.
- Clean opt-in Core device-test package: passed.
- Android `Category=Button`: 124 run, 121 passed, 0 failed, 3 pre-existing skips.
- All three Compose-specific tests passed:
  - `MapsStateAndRoutesNativeCallback`
  - `ReconnectCreatesCompositionAndUsesCurrentVirtualView`
  - `ReportsNonZeroComposedContentSizeWhenAttached`

Production blockers:

- The opt-in module exports 18 runtime AARs totaling about 25.46 MiB before final APK
  compression/linking.
- MAUI theme/resource parity is not implemented; it uses stock light/dark Material3 schemes.
- Programmatic MAUI focus targets the wrapper, not a Compose focus node.
- Full Button property, accessibility, image/font/background/padding, and styling parity is absent.
- Release/R8 and official CI Maven-feed reproducibility are not proven.
- The current Core project aliases `IButtonHandler.PlatformView` to `MaterialButton`; the experimental
  handler intentionally derives from `ViewHandler<IButton, MauiComposeButtonView>`. A production
  replacement needs a handler/interface contract decision, not just registration.

Key files:

- `src\Core\AndroidNative\build.gradle`
- `src\Core\AndroidNative\settings.gradle`
- `src\Core\AndroidNative\maui-compose-probe\build.gradle`
- `src\Core\AndroidNative\maui-compose-probe\src\main\java\com\microsoft\maui\MauiComposeButtonView.kt`
- `src\Core\src\Core.csproj`
- `src\Core\src\Handlers\Button\ExperimentalComposeButtonHandler.Android.cs`
- `src\Core\tests\DeviceTests\Handlers\Button\ExperimentalComposeButtonHandlerTests.Android.cs`

### SwiftUI

Added a concrete `@objc` `MauiSwiftUIButtonController` around
`UIHostingController<MauiSwiftUIButtonContent>`.

The wrapper implements:

- Objective-C-compatible state/callback surface.
- Text, enabled, semantics, hint, and automation ID mapping.
- Intrinsic/constrained sizing.
- Responder-chain parent controller containment.
- Disconnect/reconnect behavior.
- Diagnostic click/pressed/released callbacks.

Production blockers:

- No Swift/Xcode compilation or Apple runtime execution has occurred.
- The zero-distance `DragGesture` pressed/released path may miss cancellation/outside-touch cleanup.
  Prefer `ButtonStyle.Configuration.isPressed` or resettable `GestureState`, then validate on device.
- Programmatic MAUI focus and full Button styling/property parity are absent.
- Text wrapping, containment, sizing, reconnect, and disposal need iOS and Mac Catalyst execution.

Key files:

- `src\Core\AppleNative\PlatformInterop\MauiPlatformInterop\MauiSwiftUIButtonController.swift`
- `src\Core\AppleNative\ApiDefinition.cs`
- `src\Core\src\Handlers\Button\ExperimentalSwiftUIButtonHandler.iOS.cs`
- `src\Core\tests\DeviceTests\Handlers\Button\ExperimentalSwiftUIButtonHandlerTests.iOS.cs`

## Known build warnings and infrastructure findings

The last opt-in Android package build completed with warnings but no errors:

- `BG8605` / `BG8606`: Java binding warnings.
- `CS8604`: nullable Android context passed by
  `ExperimentalComposeButtonHandler.CreatePlatformView`.

Other infrastructure findings:

- Debug Android APKs may use Fast Deployment and be unusable after plain installation. Use
  `EmbedAssembliesIntoApk=true` for a standalone installed test APK.
- XHarness selected invalid Android user `-2` on the API 36 emulator. Direct instrumentation with
  `--user 0` worked.
- Switching the Compose opt-in without cleaning can leave stale generated Java under `obj`. Clean
  builds are authoritative.
- Do not restore the abandoned Kotlin lifecycle-owner fallback. The correct production model is a
  supported `ComponentActivity` host; the headless test fixture should initialize its view-tree owners.

## Recommended continuation order

1. **Run the Apple gate on a Mac.**
   - Build the Swift framework and Objective-C binding.
   - Run iOS and Mac Catalyst View/Border batching tests and benchmarks with batching off/on.
   - Run SwiftUI containment, sizing, reconnect, disposal, and gesture-cancellation tests.
   - Stop the Apple batching proposal if wall time does not move meaningfully.

2. **Repeat H1 on physical Android hardware in Release.**
   - Use a representative animation/layout workload, not only the synthetic explicit transaction.
   - Record native allocation/invalidation/layout effects if practical.

3. **Close Compose productization gates before broadening H3.**
   - Resolve focus, MAUI theme/style, accessibility, and full layout/property parity.
   - Validate R8, package size, dependency policy, and `dotnet-public-maven` ingestion.
   - Prefer a separate opt-in package over adding the payload to default Core.

4. **Fix the remaining local warning and harden tests.**
   - Validate `MauiContext`/Android context with the normal descriptive failure pattern.
   - Keep eventual assertions rather than fixed delays.

5. **If H2 expands, keep it bounded.**
   - Require explicit mapper replacement/append behavior.
   - Require disconnect/reconnect, container, stale-callback, and exception tests.
   - Consider code generation only after repeated native surfaces prove worthwhile.

## Suggested validation entry points

From the repository root, build MAUI build tasks first if the checkout has not already done so:

```powershell
.\build.cmd -restore -build -configuration Release `
  -projects ".\Microsoft.Maui.BuildTasks.slnf" `
  -warnAsError false
```

Build the isolated Compose module:

```powershell
Set-Location .\src\Core\AndroidNative
.\gradlew.bat :maui-compose-probe:assembleDebug
.\gradlew.bat :maui-compose-probe:assembleRelease
```

Build an opt-in, standalone Android Core DeviceTests APK. The branch was based on the .NET 10 test
TFM; adjust the TFM if the branch is later rebased:

```powershell
dotnet build .\src\Core\tests\DeviceTests\Core.DeviceTests.csproj `
  -f net10.0-android `
  -p:MauiEnableComposeButtonExperiment=true `
  -p:EmbedAssembliesIntoApk=true
```

Run the Core Android Button category through the repository device-test workflow, ensuring the
Compose property remains enabled if the runner rebuilds:

```powershell
pwsh .github\skills\run-device-tests\scripts\Run-DeviceTests.ps1 `
  -Project Core `
  -Platform android `
  -TestFilter "Category=Button"
```

For the Phase 2 benchmark, run the device benchmark once with
`MAUI_NATIVE_VIEW_PROPERTY_UPDATE_BATCHING=0` and once with it set to `1`. Compare the
`ContentViewSteadyStateProperties` `MAUIBENCH` summaries. Do not compare a Debug emulator result to a
Release physical-device result as if they were the same population.

## Final recommendation

- Advance Android H1 behind an experiment flag and validate it on Release hardware.
- Treat Apple H1 as unproven until its benchmark gate runs.
- Use H2 for small native-owned behaviors with explicit lifecycle/extensibility contracts.
- Do not propose a wholesale SwiftUI/Compose handler backend from this spike. Continue H3 only as an
  opt-in per-control/package experiment until focus, styling, accessibility, layout, packaging, and
  toolchain gates are closed.
