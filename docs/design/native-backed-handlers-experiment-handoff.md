# Native-backed MAUI handlers experiment handoff

- **Handoff date:** 2026-07-20
- **Status:** Planned probes executed on Android and Apple; Android batching remains a GO; Apple initial batching is a NO-GO
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
| H1: interop batching | Does reducing managed/native crossings measurably improve handler work? | **GO on Android explicit transactions. NO-GO on Apple connect-time primitive batching.** |
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

### Apple gate evidence

The gate was completed on an Apple Silicon Mac with Xcode 26.2, the iOS 26.3.1 simulator runtime,
and the repository-pinned .NET 10.0.108 Apple workloads.

Compilation found and fixed four branch defects before device execution:

- The SwiftUI view used accessibility modifiers unavailable at its declared iOS 13 minimum.
- The binding used the generated protocol interface before that interface existed in the API
  definition compilation.
- The binding explicitly declared an `init` constructor that the generator already supplied.
- Apple-only handler and test files were missing `System` imports.

The Swift framework and generated binding then compiled for both iOS Simulator and Mac Catalyst.
Targeted behavior runs passed:

| Platform | Category/scope | Result |
|---|---|---:|
| iOS 26.3 simulator | View | 68 passed, 0 failed, 1 skipped |
| iOS 26.3 simulator | ContentView + FlowDirection | 62 passed, 0 failed, 1 skipped |
| iOS 26.3 simulator | Button, including five SwiftUI-specific tests | 102 passed, 0 failed, 1 skipped |
| Mac Catalyst | View | 63 passed, 0 failed, 6 skipped |
| Mac Catalyst | ContentView + FlowDirection | 57 passed, 0 failed, 6 skipped |
| Mac Catalyst | Button, including five SwiftUI-specific tests | 97 passed, 0 failed, 6 skipped |

The benchmark was run in alternating off/on order with three executions per mode, 20 warmups, and
100 measured iterations per scenario. The table reports the median run-level summary.

| Platform/build | Scenario | Mean | p50 | p95 | Managed allocation |
|---|---|---:|---:|---:|---:|
| iOS simulator, Release | ContentView Apple-batched properties | +9.35% | +0.58% | +0.28% | 0.00% |
| iOS simulator, Release | Border Apple-batched properties | +0.50% | +1.56% | +0.24% | 0.00% |
| Mac Catalyst, Debug | ContentView Apple-batched properties | -2.16% | +1.48% | -3.23% | -0.11% |
| Mac Catalyst, Debug | Border Apple-batched properties | +2.02% | +0.36% | +1.85% | 0.00% |

The distributions are noisy, but the central result is consistent: reducing these few Objective-C
property calls did not improve p50 wall time on either Apple platform. There was no managed-allocation
benefit. **The Apple H1 gate is therefore NO-GO; do not productize this connect-time primitive batch.**

### Apple follow-up: explicit transform transactions

The connect-time NO-GO does not apply to recurring transform work. MAUI animations already wrap each
tick in `VisualElement.BatchBegin` / `BatchCommit`, but the iOS handler previously ignored that
boundary. Each translation, scale, rotation, or anchor mapper independently rebuilt and assigned the
complete `CATransform3D`.

Two follow-up changes were evaluated:

1. The synchronous main-thread transformation path no longer creates the captured dispatch closure
   that is only needed for off-main updates.
2. A default-off Apple implementation of
   `Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled` queues the built-in
   aggregate transformation update and flushes it once at the outer `BatchCommit`.

The allocation refactor is independently useful. Before it, 100 single-transform transactions
allocated 25,600 managed bytes; afterward they allocated 2,400 bytes, a 90.6% reduction. Batching no
longer changes allocation because the per-transformation closure allocation is gone.

The transform batching variant was measured on the iOS 26.0 arm64 simulator in Release, alternating
three batching-off and three batching-on executions. Each sample contains 100 explicit transactions;
the table reports the median run-level summary.

| Transaction shape | p50 off | p50 on | Delta | Per-transaction change |
|---|---:|---:|---:|---:|
| One transform property | 769.6 us | 800.2 us | +3.98% | +0.31 us |
| `TranslationX` + `TranslationY` (`TranslateTo` shape) | 1,711.7 us | 915.0 us | -46.54% | -7.97 us |
| Translation + scale + rotation | 3,528.7 us | 964.8 us | -72.66% | -25.64 us |
| All ten transform properties | 9,310.8 us | 1,200.0 us | -87.11% | -81.11 us |

The result is deterministic in call shape: one transformation application per transaction when
enabled, rather than one application per changed transform property. Custom appended mapper delegates
still run for every property, replaced mappers bypass the batch, nested batches flush only at the
outer commit, and behavior remains synchronous when the feature is disabled.

**Verdict:** the allocation-free transformation path is a GO independently. Explicit Apple transform
batching is a conditional GO behind the experiment switch for compound transform transactions. It is
not ready to enable by default: common single-property animations pay a small command/batch overhead,
and physical-device frame/hitch validation with multiple simultaneously animated views is still
required.

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

- A generic `Button(action:label:)` construction caused a native SIGSEGV in the Release iOS simulator
  build. Using SwiftUI's string-title initializer avoided the crash and passed the Button suite.
- The zero-distance SwiftUI `DragGesture` was replaced with a simultaneous UIKit
  `UILongPressGestureRecognizer`. It now handles ended, cancelled, and failed states through one
  guarded press-state path without cancelling the SwiftUI Button's click gesture.
- Recognizer state transitions, cancellation, Released-before-Clicked ordering, and disconnect while
  pressed are covered through the same state handler used by the native recognizer. A real
  XCUITest/Appium press-drag-scroll cancellation sequence has not been automated.
- Programmatic MAUI focus and full Button styling/property parity are absent.
- Automation ID state crosses the binding, but end-to-end XCUITest/Appium discovery of the SwiftUI
  accessibility element is not proven.

Containment, intrinsic sizing, reconnect, disconnect, callback staleness, cancellation state, and
same-window controller reparenting now pass on iOS and Mac Catalyst.

Key files:

- `src\Core\AppleNative\PlatformInterop\MauiPlatformInterop\MauiSwiftUIButtonController.swift`
- `src\Core\AppleNative\ApiDefinition.cs`
- `src\Core\src\Handlers\Button\ExperimentalSwiftUIButtonHandler.iOS.cs`
- `src\Core\tests\DeviceTests\Handlers\Button\ExperimentalSwiftUIButtonHandlerTests.iOS.cs`

## Phase 5: `UIButton.Configuration` snapshot probe

Added a nonregistered `ExperimentalConfigurationButtonHandler` that keeps the existing `UIButton`,
`IButtonHandler`, event proxy, container, focus, accessibility, and responder behavior.

The experiment uses one generated Objective-C binding call into
`MauiUIButtonConfigurationBatcher`. Swift creates a complete `UIButton.Configuration` snapshot and
assigns it once. It maps:

- Text, font, character spacing, and text color through an attributed title.
- Image.
- Physical MAUI padding through directional UIKit insets, including RTL compensation.
- Solid background color.
- Stroke color/thickness and fixed corner radius.
- Bordered Mac idiom defaults and plain iOS defaults.

The handler inserts a synthetic mapper key before ordinary property keys so the complete initial
state is available to mapper overrides. Ordinary updates remain synchronous. When
`Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled` is enabled, explicit
`VisualElement.BatchBegin` / `BatchCommit` scopes defer repeated configuration rebuilds and apply the
final state once at commit.

Important limits:

- The handler is internal and is not registered by default.
- Initial image loading is a separate operation. A synchronously resolved file image produces one
  initial snapshot plus one image-completion snapshot.
- Only solid backgrounds are supported. A non-solid background throws rather than silently changing
  behavior.
- The initial full snapshot runs before property-specific mapper overrides. An override can replace
  the final native state, but a no-op replacement cannot prevent that initial snapshot from mapping
  the property. A production migration needs granular mapper-aware updates rather than this
  performance-ceiling shortcut.
- Legacy per-state `UIButton` title/background appearance APIs, configuration update handlers, and
  arbitrary custom mapper code need a compatibility contract before production use.

Release behavior evidence:

- iOS: 113 run, 112 passed, 0 failed, 1 skipped.
- Mac Catalyst: 113 run, 107 passed, 0 failed, 6 skipped.
- Tests cover coherent state, native defaults and intrinsic size, synchronous updates, image load and
  clear/cancellation, explicit batching on/off, mapper ordering, reconnect/event rebinding, and RTL
  padding.

### `UIButton.Configuration` benchmark evidence

The Release benchmarks used three alternating batching-off and batching-on executions per platform.
Each steady-state sample contains 100 explicit transactions. Connect CPU samples contain 100
handler connections and intentionally exclude images so asynchronous image work is not hidden in the
connect comparison.

Median p50 connect/first-layout results from the batching-off runs:

| Platform | Scope | Legacy | Configuration | Delta | Managed allocation delta |
|---|---|---:|---:|---:|---:|
| iOS | Handler connect CPU | 73,396.5 us | 68,664.2 us | -6.45% | -19.36% |
| Mac Catalyst | Handler connect CPU | 88,467.2 us | 76,138.6 us | -13.94% | -16.92% |
| iOS | Connect to first layout | 16,156.7 us | 16,033.6 us | -0.76% | -25.47% |
| Mac Catalyst | Connect to first layout | 2,236.3 us | 2,382.5 us | +6.54% | -28.44% |

The Catalyst first-layout samples were much noisier than connect CPU. The useful signal is that the
connect CPU reduction did not become a meaningful first-layout reduction on either platform.

Median p50 steady-state results, in microseconds per 100 transactions:

| Platform | Properties changed | Legacy off | Configuration off | Delta off | Legacy on | Configuration on | Delta on |
|---|---|---:|---:|---:|---:|---:|---:|
| iOS | One | 2,716.6 | 7,587.4 | +179.30% | 2,745.5 | 8,257.3 | +200.76% |
| iOS | Four | 9,154.6 | 30,531.0 | +233.50% | 9,099.4 | 10,091.0 | +10.90% |
| iOS | Nine | 22,970.6 | 65,309.6 | +184.32% | 24,279.3 | 14,146.6 | -41.73% |
| Mac Catalyst | One | 3,661.9 | 9,260.7 | +152.89% | 4,087.1 | 10,372.5 | +153.79% |
| Mac Catalyst | Four | 12,674.7 | 35,299.6 | +178.50% | 12,538.0 | 12,867.5 | +2.63% |
| Mac Catalyst | Nine | 34,993.5 | 82,378.4 | +135.41% | 32,193.7 | 16,268.9 | -49.47% |

The diagnostics confirmed the intended call shape:

- Batching off: 10,000, 40,000, and 90,000 configuration assignments for the one-, four-, and
  nine-property matrices.
- Batching on: 10,000 assignments for every matrix, one per explicit transaction.

Managed allocation remains a major constraint. With batching enabled, 100 one-property transactions
allocate 153,600 bytes for the configuration path versus zero for the legacy path. Four-property
transactions allocate 160,800 bytes versus about 13,600 bytes. Only the nine-property transaction
crosses over: iOS configuration allocation is 168,000 bytes versus about 233,848 bytes for legacy
(-28.2%), and Mac Catalyst is 168,000 bytes versus about 217,616 bytes (-22.8%).

**Verdict:** NO-GO for the current full-snapshot handler as a production Button replacement.
Configuration snapshots reduce connect CPU and managed allocation, but the gain disappears in
first-layout timing. Common isolated updates are 2.5x to 3x slower and allocate substantially more.
Explicit batching reaches near parity at four changed properties and wins decisively only for the
synthetic nine-property transaction. That niche crossover does not justify the mapper compatibility,
background, appearance, image, and allocation costs of an all-or-nothing migration.

If `UIButton.Configuration` is pursued for API correctness or modernization, the next valid shape is
a granular dirty-mask implementation that updates only the affected configuration fields, preserves
property replacement semantics, and measures native allocations. Do not advance this complete
snapshot implementation.

## Phase 6: Apple high-frequency callback profiling

Profiled ScrollView, Entry, Editor, pan, pinch, and pointer-move callback bursts on iOS and Mac
Catalyst.

The Release benchmark uses:

- 20 warmup iterations.
- 100 measured iterations.
- 100 callbacks per measured sample.
- Three executions per platform.
- Real UIKit delegate/event entry points for ScrollView, Entry, and Editor.
- Managed dispatch lower bounds for pan, pinch, and pointer movement.

No physical iPhone was connected. iOS measurements use the iOS 26.0 arm64 simulator. Mac Catalyst
runs execute on the physical Apple Silicon Mac.

Median current-code results:

| Callback path | iOS us/callback | iOS bytes/callback | Catalyst us/callback | Catalyst bytes/callback |
|---|---:|---:|---:|---:|
| ScrollView vertical delegate | 15.881 | 80 | 11.899 | 80 |
| ScrollView diagonal delegate | 16.118 | 160 | 12.154 | 160 |
| Entry `EditingChanged`, stable text | 57.034 | 480 | 25.518 | 176 |
| Entry changed-text native/managed round trip | 305.150 | 2,217 | 164.995 | 1,080 |
| Editor changed callback, stable text | 21.626 | 96 | 16.263 | 96 |
| Entry one-range max-length validation | 26.108 | 720 | 18.587 | 280 |
| Entry three-range max-length validation | 31.249 | 816 | 23.981 | 376 |
| Pan managed dispatch | 0.010 | 40 | 0.014 | 40 |
| Pinch managed dispatch | 0.013 | 48 | 0.018 | 48 |
| Pointer-move managed dispatch | 0.049 | 224 | 0.043 | 224 |

The ScrollView diagonal benchmark confirmed that one native callback currently raises two managed
`Scrolled` events because the handler assigns horizontal and vertical offsets separately. Fixing that
atomically would require a cross-assembly scroll-position contract; reflection, deferred dispatch, or
implicit coalescing would be worse than the measured cost.

Broad callback coalescing is unsafe:

- Scroll offsets and `Scrolled` handlers are synchronously observable and can drive scroll-linked UI.
- Entry/Editor text changes must preserve `TextChanged`, cursor, selection, MaxLength, and IME
  composition ordering.
- Pan, pinch, and pointer handlers intentionally expose every running-state event to user code.
- Gesture managed dispatch is already tiny; only its per-event object allocation is visible.

### iOS 26 multi-range Entry validation follow-up

The iOS 26 `ShouldChangeCharactersInRanges` path rebuilt the complete candidate string for every
range only to compare its final length to `MaxLength`. Replaced it with equivalent checked length
arithmetic, retaining the existing descending range order and paste truncation behavior.

Release before/after medians:

| Platform | Shape | p50 delta | p95 delta | Managed allocation delta |
|---|---|---:|---:|---:|
| iOS simulator | One range | +19.80% | -6.84% | -34.31% |
| iOS simulator | Three ranges | +23.17% | +4.17% | -55.26% |
| Mac Catalyst | One range | -25.12% | -25.39% | -57.32% |
| Mac Catalyst | Three ranges | -26.52% | -27.05% | -72.83% |

The iOS simulator timing was noisy after a simulator reset: optimized one-range p50 varied from
1,953.3 to 2,866.0 us per 100 callbacks, while allocation was deterministic. Catalyst provided a
stable timing signal and confirmed the expected improvement.

Behavior evidence:

- iOS Entry Debug: 234 run, 233 passed, 0 failed, 1 ignored.
- Mac Catalyst Entry Debug: 234 run, 228 passed, 0 failed, 6 ignored.
- Release callback benchmarks passed in all six final executions.

**Verdict:** NO-GO for general Apple event coalescing. GO for the allocation-free multi-range
MaxLength calculation. The next event optimization should target a demonstrated source-level
allocation or duplicate notification without changing callback frequency.

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
- The selected Xcode installation had iOS SDK files but had not registered an iOS platform
  destination. `xcodebuild -downloadPlatform iOS -architectureVariant arm64` installed and registered
  the matching simulator component.
- Apple builds must use the repository-local `.dotnet` after the bootstrap installs the pinned
  iOS/Mac Catalyst workload packs.
- The Xcode project build is stamp-based. After Swift changes, rebuild the Core native reference and
  relink the app before trusting runtime selector results.
- Switching Apple TFMs/configurations can leave incompatible AOT outputs in the device-test app.
  XHarness may report success even when the app aborts before creating `test-results.xml`. Treat the
  XML/application log as authoritative and run a target-specific clean rebuild after an
  `out of date` AOT-module error. If `dotnet clean` fails because `project.assets.json` currently
  targets another Apple TFM, it can leave stale AOT files behind; restore the intended TFM first or
  remove only that app's resolved target-specific `bin` and `obj` directories before rebuilding.
- Mac Catalyst GUI processes redact ordinary `Console.WriteLine` payloads in unified logs. The
  benchmark now also writes through xUnit output so XHarness persists `MAUIBENCH` lines in its XML.

## Recommended continuation order

1. **Accept the Apple H1 NO-GO decision.**
   - Do not advance the connect-time Swift/UIKit primitive batch toward production.
   - Keep the evidence scenarios while this experiment branch is useful, or remove the default-off
     batch before extracting production work.
   - Continue SwiftUI only as an independent H3 hosting probe, not as evidence for Apple batching.

2. **Do not advance the complete `UIButton.Configuration` snapshot handler.**
   - Treat the current implementation as a measured performance ceiling, not a production candidate.
   - If Button modernization continues, prototype granular dirty-mask updates with mapper replacement,
     UIAppearance/per-state, non-solid background, and native-allocation coverage.

3. **Validate the positive Apple transform follow-up on physical hardware.**
   - Use real `TranslateTo` and compound animations with one, ten, and one hundred simultaneous views.
   - Record UI-thread CPU, frame hitches, Core Animation work, and GC activity.
   - Keep the feature default-off unless a representative frame workload moves meaningfully.

4. **Do not add general Apple callback coalescing.**
   - Keep per-event scroll, gesture, text, cursor, selection, and IME semantics.
   - Prefer local allocation removal such as the multi-range MaxLength calculation.
   - Revisit pointer-event allocation only if a representative pointer-heavy app shows GC pressure.

5. **Repeat H1 on physical Android hardware in Release.**
   - Use a representative animation/layout workload, not only the synthetic explicit transaction.
   - Record native allocation/invalidation/layout effects if practical.

6. **Close Compose productization gates before broadening H3.**
   - Resolve focus, MAUI theme/style, accessibility, and full layout/property parity.
   - Validate R8, package size, dependency policy, and `dotnet-public-maven` ingestion.
   - Prefer a separate opt-in package over adding the payload to default Core.

7. **Fix the remaining local warning and harden tests.**
   - Validate `MauiContext`/Android context with the normal descriptive failure pattern.
   - Keep eventual assertions rather than fixed delays.

8. **If H2 expands, keep it bounded.**
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

For the Phase 5 Button probe, compare the `ButtonLegacy*` and `ButtonConfiguration*` summaries from
at least three alternating off/on runs. Use the xUnit XML on Mac Catalyst because unified logging
redacts ordinary benchmark payloads.

## Final recommendation

- Advance Android H1 behind an experiment flag and validate it on Release hardware.
- Stop Apple connect-time primitive batching: the completed iOS/Mac Catalyst gate showed no
  meaningful wall-time improvement.
- Keep the allocation-free iOS transformation refactor; it removes the dominant managed allocation
  from every transform update.
- Continue explicit Apple transform batching only as a default-off compound-animation experiment
  until physical-device frame evidence is available.
- Do not advance the complete `UIButton.Configuration` snapshot handler. A future modernization
  attempt must use granular mapper-aware updates and remeasure ordinary property changes.
- Keep the allocation-free iOS 26 multi-range Entry MaxLength calculation.
- Do not add broad Apple scroll, gesture, or text callback coalescing.
- Use H2 for small native-owned behaviors with explicit lifecycle/extensibility contracts.
- Do not propose a wholesale SwiftUI/Compose handler backend from this spike. Continue H3 only as an
  opt-in per-control/package experiment until focus, styling, accessibility, layout, packaging, and
  toolchain gates are closed.
