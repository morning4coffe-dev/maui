# Native-backed MAUI handlers experiment report

- **Report date:** 2026-07-22
- **Repository:** `dotnet/maui`
- **Experiment branch:** `temp/native-backed-handlers-handoff-20260720`
- **Base commit:** `0395a53b66f85d7b6fd6732f9b2f8a50eb7d70cb`
- **Latest experiment commit:** `d2b94b8d06775859aad997dc69e84f233357204c`

## Executive summary

The experiment tested whether MAUI handlers benefit from moving more work into native Java, Kotlin,
Swift, UIKit, SwiftUI, or Compose and from crossing the managed/native boundary less frequently.

The answer is platform- and workload-specific:

| Area | Android result | Apple result |
|---|---|---|
| Initial unrelated property batching | Existing MAUI precedent remains valid | **NO-GO** for the tested Swift/UIKit primitive batch |
| Explicit recurring property batching | **GO for further Release hardware validation** | **Conditional GO** for compound transforms only |
| Allocation reduction | No change in the Android transaction benchmark | **GO:** iOS transform allocations reduced about 90.6% |
| Bounded native-owned behavior | **GO as an incremental pattern** | Feasible, but not separately productized in this branch |
| Compose/SwiftUI control replacement | Hosting works; production replacement is **NO-GO** | Hosting works; production replacement is **NO-GO** |
| `UIButton.Configuration` replacement | Not applicable | Current full-snapshot design is **NO-GO** |

The most credible production extractions are:

1. Keep the allocation-free iOS transform update.
2. Continue Android explicit property batching behind a feature switch and validate it on physical
   Release hardware.
3. Continue Apple transform batching only for compound explicit transactions, behind the existing
   default-off switch.
4. Use native-owned logic only for bounded lifecycle or event-translation responsibilities.

The experiment does **not** support replacing MAUI's native handlers wholesale with Compose or
SwiftUI, and it does **not** support broad deferred mapper commits.

## Goals and decision criteria

The work evaluated three independent hypotheses:

| Hypothesis | Question | Decision criterion |
|---|---|---|
| H1: interop batching | Does reducing JNI or Objective-C crossings improve measured work? | Wall time, UI-thread CPU, allocation, and preserved mapper semantics |
| H2: native-owned behavior | Can native code own bounded behavior safely? | Correct reconnect/disconnect, weak ownership, event ordering, and extensibility |
| H3: declarative native backing | Can Compose or SwiftUI replace the current platform control? | Full layout, focus, styling, accessibility, lifecycle, packaging, and test parity |

The experiments preserved MAUI's existing synchronous property-update contract unless an explicit
`BatchBegin` / `BatchCommit` boundary was present. This is important because mapper and native state
are synchronously observable today.

## Measurement methodology

The branch added an opt-in device benchmark host under:

`src/Core/tests/DeviceBenchmarks`

It measures:

- Handler connection through first native layout.
- Mapper-only handler connection CPU.
- Explicit steady-state property transactions.
- Monotonic wall-clock duration.
- Managed allocations on the current UI thread.
- Android UI-thread CPU time.
- Deterministic diagnostics such as native batch flushes and configuration assignments.

Default benchmark shape:

- 20 warmup iterations.
- 100 measured iterations.
- 100 property transactions or handler connections per measured sample where specified.
- Alternating feature-off and feature-on runs.
- Median of three run-level summaries for final comparisons.

Important limitations:

- Java and native allocations were not measured.
- App startup was not measured.
- Absolute values must not be compared across platforms.
- Android H1 evidence used a Debug x86_64 emulator.
- Apple transform and Button configuration evidence used Release simulator/Catalyst builds.

## Android results

### 1. Existing Android initial batching precedent

MAUI already batches initial Android base-view properties:

- A synthetic mapper key runs while the handler connects.
- C# computes a group of initial values.
- One JNI call applies roughly 16 Java view properties.
- Default property mappers skip duplicate work during the initial mapping pass.
- User mapper replacement and extension remain available.

The original implementation, dotnet/maui#3372, reported approximately 30% faster handler attachment
for several controls and about 17 ms in its sample startup path.

This existing design remains the strongest evidence that coarse JNI calls can be useful when:

- The fields are cheap primitives.
- The transaction boundary is explicit.
- Native target selection is known.
- Managed side effects and mapper semantics remain outside the batch.

### 2. Android explicit steady-state batching

The experiment extended batching to recurring explicit transactions.

Feature switch:

`Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled`

Benchmark environment variable:

`MAUI_NATIVE_VIEW_PROPERTY_UPDATE_BATCHING`

Eligible properties:

- `IsEnabled`
- `Opacity`
- `TranslationX` / `TranslationY`
- `Scale` / `ScaleX` / `ScaleY`
- `Rotation` / `RotationX` / `RotationY`

Excluded properties:

- Visibility and flow direction.
- Minimum sizes and pivots.
- Layout and frame.
- Focus.
- Images and other asynchronous or service-backed properties.

Implementation behavior:

- Updates remain synchronous outside an explicit batch.
- Nested batches flush only at the outer commit.
- The handler tracks a dirty mask.
- Final desired values are read at commit.
- One Java `PlatformInterop.updateViewProperties(...)` call applies the selected values.
- Container-aware opacity and transform targeting are preserved.
- Wrapper shadow invalidation is preserved.
- Pending state is cleared before JNI execution.
- Mapper append/replace, reconnect, empty batch, exception, and NaN behavior are covered.

#### Android benchmark

Environment:

- API 36 x86_64 emulator: `emulator-5580`
- Debug build
- 100 explicit nested transactions per measured iteration
- Three batching-off and three batching-on executions
- Two benchmark tests passed in every execution

Median run-level results:

| Metric | Batching off | Batching on | Delta |
|---|---:|---:|---:|
| Mean duration | 30,373.053 us | 23,985.747 us | -21.03% |
| p50 duration | 25,769.500 us | 19,625.400 us | -23.84% |
| p95 duration | 57,413.800 us | 44,337.600 us | -22.78% |
| Mean UI-thread CPU | 29,913.984 us | 23,932.321 us | -20.00% |
| Managed allocation | 88,800 bytes | 88,800 bytes | 0.00% |

#### Android batching verdict

**GO for further validation, not yet GO for default enablement.**

The timing and UI-thread CPU signal is material and points in the same direction. However:

- One batching-off run was a large emulator outlier.
- The build was Debug.
- The device was an x86_64 emulator.
- Java/native allocation and invalidation effects were not measured.
- A representative real animation/layout workload was not measured.

Required next gate:

- Physical Android hardware.
- Release build.
- Representative compound animation/layout workload.
- Frame timing and native allocation evidence.

### 3. Android native-owned Button event bridge

The experiment added a Java-owned bridge for Button interaction.

Feature switch:

`Microsoft.Maui.RuntimeFeature.IsNativeButtonEventBridgeEnabled`

Java owns:

- Click and touch listener registration.
- `MotionEvent` translation into pressed/released semantics.
- Attach/detach lifecycle.

C# receives:

- Clicked.
- Pressed.
- Released.

Safety properties:

- Focus remains in the existing MAUI handler path.
- `onTouch` returns `false`, preserving ripple and click behavior.
- Java uses weak references.
- C# clears the callback peer during disconnect.
- The disabled path does not allocate unused JNI peers eagerly.
- Reconnect, stale callbacks, containers, mapper append behavior, and fallback behavior are covered.

#### Android native behavior verdict

**GO as a bounded design pattern.**

This supports moving narrowly defined lifecycle or event translation into native code. It does not
support moving an entire MAUI control contract into an opaque native implementation.

### 4. Compose hosting probe

An isolated Kotlin/Compose module was added:

`src/Core/AndroidNative/maui-compose-probe`

Opt-in property:

`MauiEnableComposeButtonExperiment=true`

The wrapper is a `FrameLayout` containing a `ComposeView` and exposes ordinary JVM methods for:

- Text.
- Enabled state.
- Semantics description.
- Automation ID.
- Click, pressed, and released callbacks.
- Composition lifecycle.
- Diagnostic size.

Build and device evidence:

- Compose Debug AAR: passed.
- Compose Release AAR: passed.
- Clean default-off Core device-test package: passed.
- Clean opt-in Core device-test package: passed.
- Android Button category: 124 run, 121 passed, 0 failed, 3 pre-existing skips.
- All three Compose-specific tests passed.

Production blockers:

- 18 runtime AARs totaling about 25.46 MiB before final APK compression/linking.
- Stock Material3 theme instead of MAUI theme/resource parity.
- Programmatic focus targets the wrapper, not the Compose focus node.
- Missing full Button property, accessibility, image, font, background, padding, and styling parity.
- Release/R8 behavior is not proven.
- Official Maven-feed ingestion is not proven.
- The existing `IButtonHandler.PlatformView` contract expects `MaterialButton`; the Compose handler
  uses a different platform type.

#### Compose verdict

**Hosting is feasible; default MAUI Button replacement is NO-GO.**

The only plausible future product shape is an opt-in package after focus, accessibility, theme,
package-size, dependency, and handler-contract gates are resolved.

### Android overall verdict

| Area | Verdict |
|---|---|
| Explicit property batching | **Promising GO**, pending Release physical-device evidence |
| Native-owned Button event logic | **GO as a bounded pattern** |
| Compose-backed default Button | **NO-GO** |

## Apple results

Apple evidence covers both iOS and Mac Catalyst.

Environment:

- Apple Silicon Mac.
- Xcode 26.2.
- Repository-local .NET SDK 10.0.108.
- Apple workload 26.0.11017.
- iOS 26.x simulator runtime.
- Release iOS and Mac Catalyst behavior gates for the final Button configuration probe.

### 1. Apple connect-time primitive batching

The experiment added an opt-in Swift/UIKit primitive batch through the existing Xcode framework and
Objective-C binding pipeline.

Feature switch:

`Microsoft.Maui.Experimental.NativeViewPropertyBatching`

Benchmark environment variable:

`MAUI_NATIVE_VIEW_PROPERTY_BATCHING`

The batch covered only safe primitive operations. Managed code retained responsibility for:

- Collapse/inflate.
- Flow-direction propagation.
- Mapper extensions.
- Container handling.
- Reconnect semantics.
- Transforms and size mappings.

Behavior gates passed on iOS and Mac Catalyst after fixing Swift availability and generated-binding
issues.

Median off/on benchmark deltas:

| Platform/build | Scenario | Mean | p50 | p95 | Managed allocation |
|---|---|---:|---:|---:|---:|
| iOS simulator, Release | ContentView | +9.35% | +0.58% | +0.28% | 0.00% |
| iOS simulator, Release | Border | +0.50% | +1.56% | +0.24% | 0.00% |
| Mac Catalyst, Debug | ContentView | -2.16% | +1.48% | -3.23% | -0.11% |
| Mac Catalyst, Debug | Border | +2.02% | +0.36% | +1.85% | 0.00% |

#### Apple initial batching verdict

**NO-GO.**

Reducing these Objective-C dispatches did not improve median wall time and did not reduce managed
allocation. UIKit layout and run-loop work dominated the small crossing reduction.

### 2. iOS transform allocation optimization

The synchronous iOS transform path previously created a captured dispatch closure even when already
on the UI thread.

Removing that unnecessary closure reduced managed allocation:

| Transaction shape, 100 transactions | Before | After | Reduction |
|---|---:|---:|---:|
| One transform | 25,600 bytes | 2,400 bytes | 90.6% |
| Translation X + Y | 51,200 bytes | about 4,801 bytes | about 90.6% |
| Four transform properties | 102,400 bytes | about 9,603 bytes | about 90.6% |
| Ten transform properties | 256,000 bytes | about 24,007 bytes | about 90.6% |

#### Transform allocation verdict

**GO.**

This optimization is useful independently of native batching and preserves the existing synchronous
behavior.

### 3. Apple explicit transform batching

The experiment taught the iOS/Mac Catalyst handler to honor the existing explicit
`BatchBegin` / `BatchCommit` boundary for aggregate transforms.

Behavior:

- Default-off under
  `Microsoft.Maui.RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled`.
- Built-in aggregate transform updates are queued.
- Custom appended mapper delegates still run for each property.
- Replaced mappers bypass batching.
- Nested batches flush at the outer commit.
- Disabled behavior remains synchronous.
- Disconnect clears pending work.

iOS Release median results, 100 transactions per sample:

| Transaction shape | p50 off | p50 on | Delta | Per-transaction change |
|---|---:|---:|---:|---:|
| One transform property | 769.6 us | 800.2 us | +3.98% | +0.31 us |
| Translation X + Y | 1,711.7 us | 915.0 us | -46.54% | -7.97 us |
| Translation + scale + rotation | 3,528.7 us | 964.8 us | -72.66% | -25.64 us |
| All ten transform properties | 9,310.8 us | 1,200.0 us | -87.11% | -81.11 us |

#### Apple transform batching verdict

**Conditional GO.**

- Single-property animation pays a small overhead.
- Compound explicit transactions improve substantially.
- Physical-device frame and hitch evidence is still required.
- The feature should remain default-off until that gate is complete.

### 4. SwiftUI hosting probe

The experiment added a concrete Objective-C-compatible
`MauiSwiftUIButtonController` around `UIHostingController`.

Implemented:

- Text and enabled state.
- Semantics description and hint.
- Automation ID.
- Intrinsic and constrained sizing.
- Parent-controller containment.
- Reconnect and disconnect.
- Click, pressed, released, and cancellation diagnostics.
- Same-window controller reparenting.

Important findings:

- Generic `Button(action:label:)` caused a native Release simulator SIGSEGV.
- SwiftUI's string-title Button initializer avoided the crash.
- A zero-distance SwiftUI drag gesture conflicted with click behavior.
- A simultaneous UIKit `UILongPressGestureRecognizer` provided stable press tracking.
- Released-before-Clicked ordering and cancellation behavior were hardened.

Production blockers:

- Programmatic MAUI focus is not implemented.
- Full Button style/property parity is absent.
- End-to-end Appium/XCUITest accessibility discovery is not proven.
- Real press-drag-scroll cancellation is not automated.
- Hosting-controller containment and packaging are more complex than a direct `UIButton`.

#### SwiftUI verdict

**Hosting is feasible; interactive default Button replacement is NO-GO.**

SwiftUI may remain useful for isolated opt-in content or noninteractive native effects, but not as a
drop-in replacement for MAUI's core Button handler today.

### 5. `UIButton.Configuration` snapshot probe

The final Apple experiment retained a real `UIButton` and `IButtonHandler` while applying a complete
configuration snapshot through one generated Objective-C call into Swift.

Mapped state:

- Text.
- Font.
- Character spacing.
- Text color.
- Image.
- Padding with RTL compensation.
- Solid background.
- Stroke color and thickness.
- Corner radius.
- Mac idiom bordered configuration.

Behavior:

- Internal and not registered by default.
- Initial configuration snapshot runs before ordinary property mapper keys.
- Ordinary updates remain synchronous.
- Explicit batches coalesce repeated configuration rebuilds.
- Image loading, clear, cancellation, reconnect, event rebinding, mapper ordering, and RTL are
  covered.
- Non-solid backgrounds are intentionally unsupported.

Final Release behavior gates:

| Platform | Result |
|---|---:|
| iOS | 113 run, 112 passed, 0 failed, 1 skipped |
| Mac Catalyst | 113 run, 107 passed, 0 failed, 6 skipped |

#### Connect and first-layout results

Median p50 from three batching-off runs:

| Platform | Scope | Legacy | Configuration | Delta | Managed allocation delta |
|---|---|---:|---:|---:|---:|
| iOS | Handler connect CPU | 73,396.5 us | 68,664.2 us | -6.45% | -19.36% |
| Mac Catalyst | Handler connect CPU | 88,467.2 us | 76,138.6 us | -13.94% | -16.92% |
| iOS | Connect through first layout | 16,156.7 us | 16,033.6 us | -0.76% | -25.47% |
| Mac Catalyst | Connect through first layout | 2,236.3 us | 2,382.5 us | +6.54% | -28.44% |

The connect CPU reduction did not become a meaningful first-layout reduction.

#### Steady-state configuration results

Median p50 in microseconds per 100 transactions:

| Platform | Properties | Legacy off | Config off | Delta off | Legacy on | Config on | Delta on |
|---|---|---:|---:|---:|---:|---:|---:|
| iOS | One | 2,716.6 | 7,587.4 | +179.30% | 2,745.5 | 8,257.3 | +200.76% |
| iOS | Four | 9,154.6 | 30,531.0 | +233.50% | 9,099.4 | 10,091.0 | +10.90% |
| iOS | Nine | 22,970.6 | 65,309.6 | +184.32% | 24,279.3 | 14,146.6 | -41.73% |
| Mac Catalyst | One | 3,661.9 | 9,260.7 | +152.89% | 4,087.1 | 10,372.5 | +153.79% |
| Mac Catalyst | Four | 12,674.7 | 35,299.6 | +178.50% | 12,538.0 | 12,867.5 | +2.63% |
| Mac Catalyst | Nine | 34,993.5 | 82,378.4 | +135.41% | 32,193.7 | 16,268.9 | -49.47% |

Assignment diagnostics:

- Batching off: 10,000, 40,000, and 90,000 configuration assignments.
- Batching on: 10,000 assignments for every matrix.

Managed allocation with batching enabled:

| Platform/shape | Legacy | Configuration | Result |
|---|---:|---:|---|
| One property | 0 bytes | 153,600 bytes | Configuration is substantially worse |
| Four properties | about 13,600 bytes | 160,800 bytes | Configuration is substantially worse |
| iOS, nine properties | about 233,848 bytes | 168,000 bytes | Configuration is 28.2% lower |
| Catalyst, nine properties | about 217,616 bytes | 168,000 bytes | Configuration is 22.8% lower |

#### `UIButton.Configuration` verdict

**NO-GO for the complete snapshot handler.**

The snapshot improves mapper-only connection CPU and managed allocation, but:

- First-layout time is effectively unchanged.
- Common one-property updates are 2.5x to 3x slower.
- Four-property batching is only near parity and allocates much more.
- The only decisive win is the synthetic nine-property transaction.
- The initial full snapshot weakens property replacement semantics.
- Non-solid backgrounds and legacy per-state appearance require additional compatibility design.

If `UIButton.Configuration` is pursued for modernization or deprecated-API correctness, the next
valid design is a granular dirty-mask mapper that updates only affected configuration fields and
remeasures native allocations.

### Apple overall verdict

| Area | Verdict |
|---|---|
| Connect-time primitive batching | **NO-GO** |
| Allocation-free transform update | **GO** |
| Compound explicit transform batching | **Conditional GO** |
| SwiftUI-backed default Button | **NO-GO** |
| Complete `UIButton.Configuration` snapshot | **NO-GO** |

## Cross-platform conclusions

### 1. Interop cost differs materially

The Android JNI transaction produced a meaningful CPU and wall-time signal. The comparable Apple
Objective-C primitive batch did not.

The result does not mean Objective-C crossings are free. It means the tested UIKit property calls
were too small relative to layout/run-loop work for crossing reduction to move the measured endpoint.

### 2. Explicit recurring transactions are the strongest batching boundary

Both platforms show the same shape:

- One-property batches add overhead.
- Compound transactions benefit from applying aggregate state once.
- Larger transactions produce larger gains.

This validates `BatchBegin` / `BatchCommit` as the correct experimentation boundary. It does not
validate arbitrary next-frame deferred updates.

### 3. Aggregate native setters must be cheaper than rebuilding state

Android's Java dirty-mask setter applies primitive fields directly.

The full Apple Button configuration path rebuilt attributed text, background configuration, insets,
stroke, and image state on every ordinary update. That cost overwhelmed the saved dispatches until a
large transaction was coalesced.

### 4. Native ownership should remain bounded

The Android event bridge is successful because it owns one narrow responsibility with explicit
lifecycle and weak-reference rules.

Compose and SwiftUI wrappers become problematic when they must own the full MAUI control contract:

- Focus.
- Styling.
- Accessibility.
- Layout.
- Images.
- Theme integration.
- Handler type compatibility.
- Packaging and dependency policy.

### 5. Declarative native frameworks do not remove MAUI contracts

Compose and SwiftUI can render content, but MAUI still needs a stable bridge for:

- Property mapper extensibility.
- Synchronous native observability.
- Handler reconnect/disconnect.
- Platform focus.
- Accessibility automation.
- Native control expectations.

The wrapper cost and compatibility surface remain even when the visual implementation is declarative.

## Decision matrix

| Candidate | Performance signal | Behavior confidence | Product recommendation |
|---|---|---|---|
| Android explicit primitive batching | Positive, about 20-24% | Good targeted coverage | Validate on physical Release hardware |
| Android native Button event bridge | Not primarily performance-tested | Strong lifecycle/event coverage | Reuse as bounded native-ownership pattern |
| Compose Button wrapper | Not a default-handler performance win | Basic probe passes | Keep opt-in only; do not ship in Core |
| Apple connect-time primitive batch | No median improvement | Behavior passes | Stop |
| iOS transform allocation removal | About 90.6% allocation reduction | Behavior passes | Extract/keep |
| Apple compound transform batch | 46-87% faster for compound shapes | Strong semantic coverage | Keep default-off pending physical frame tests |
| SwiftUI Button wrapper | No production performance case | Hosting behavior passes | Do not replace core Button |
| `UIButton.Configuration` full snapshot | Wins only at nine-property batch | Strong experiment coverage, compatibility gaps | Stop current shape |

## Infrastructure lessons

### Android

- Debug Fast Deployment APKs may not work after ordinary installation.
- Use `EmbedAssembliesIntoApk=true` for a standalone test package.
- XHarness selected invalid Android user `-2` on the API 36 emulator; direct instrumentation with
  `--user 0` worked.
- Switching the Compose opt-in without cleaning can leave stale generated Java.
- Official builds require Maven dependencies to be available through the MAUI Azure Artifacts feed.

### Apple

- Use the repository-local `.dotnet` and pinned Apple workloads.
- Swift changes require rebuilding the Xcode native reference and relinking the app.
- Stale Xcode stamps can produce runtime unrecognized-selector failures.
- Switching Apple TFMs/configurations can leave incompatible AOT output.
- XHarness can report success when an app aborts before producing XML.
- Treat the result XML and application log as authoritative.
- If `dotnet clean` fails because assets target another TFM, remove only the resolved
  target-specific app `bin`/`obj` directories before rebuilding.
- Mac Catalyst unified logs redact ordinary console payloads; benchmark output must also be written
  through xUnit XML.

## Final recommendations

### Advance

1. **iOS allocation-free transform update**
   - Preserve as an independent production-quality optimization.

2. **Android explicit property batching**
   - Keep behind the existing feature switch.
   - Repeat on physical Release hardware.

3. **Apple compound transform batching**
   - Keep default-off.
   - Validate frame hitches, UI-thread CPU, Core Animation work, and GC on physical devices.

4. **Bounded native-owned behavior**
   - Use the Android Button event bridge as the design template.

### Stop

1. Apple connect-time primitive batching.
2. Complete `UIButton.Configuration` snapshots for every update.
3. Wholesale SwiftUI or Compose replacement of MAUI core handlers.
4. Broad deferred mapper commits without explicit transaction boundaries.

### Investigate next

1. Profile high-frequency Apple native-to-managed scroll, gesture-move, and text/IME callbacks before
   designing another bridge.
2. If Button modernization is required for UIKit API correctness, prototype granular configuration
   dirty masks rather than complete snapshots.
3. Measure Android Release behavior on physical hardware.
4. Measure Apple compound animations with one, ten, and one hundred simultaneous views.

## Commit inventory

| Commit | Purpose |
|---|---|
| `d5363b378c` | Original native-backed handler experiments |
| `ffa8d2c1a4` | Apple build, behavior, lifecycle, and benchmark gate |
| `665f5f1032` | Remove iOS transform update allocations |
| `037628b15f` | Coalesce Apple transform updates |
| `d2b94b8d06` | Evaluate `UIButton.Configuration` snapshots |

## Bottom line

The experiment supports a selective strategy, not a framework rewrite:

- Batch primitive state where crossing cost is proven and an explicit transaction exists.
- Remove managed allocations from hot paths independently of batching.
- Move only bounded lifecycle/event logic into native code.
- Keep MAUI's existing platform controls when focus, accessibility, styling, and mapper compatibility
  matter.
- Require measured platform-specific evidence before generalizing an optimization across Android and
  Apple.
