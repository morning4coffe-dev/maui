# Runtime package integrity

This build-only guard validates managed PE/CLI metadata without loading code.
It is not a repair of the historical all-zero Graphics publication and does
not assign its unreproduced trigger to Roslyn, the SDK, ReFS or Windows.

`Uno.Maui.Runtime.csproj` selects the six implementation images and their normal
published references. Each reference must be a reference assembly and match
its corresponding **producer refint bytes**, not the implementation MVID.
SHA-256 is computed from the same immutable snapshot inspected by `PEReader`.
PE sections and the certificate directory must fit within the file.

The task runs before `_GetPackageFiles`, not merely before `Pack` (which is too
late for the `GenerateNuspec` prerequisites). It retains read-only file leases
until MSBuild build-lifetime disposal, including failed/cancelled builds.
After `GenerateNuspec`, each expected managed archive entry must match its
retained input snapshot exactly, with no duplicate/missing entry. The archive
is then held read-only until build completion. Validation never recopies,
rebuilds, repairs, retries or converts failure to a warning.

The RI-1/RI-2 follow-up selects **SDK-resolved `@(_OutputPackItems)`**, including
the requested symbol output. It never concatenates `PackageOutputPath` and a
filename: `dotnet pack --output <directory-without-trailing-separator>` must
validate the actual archive, not an adjacent decoy. Missing resolved outputs
fail even when a normal package was successfully emitted.

When symbols are requested, the runtime projects six PDB inputs alongside its
six implementations. Portable PDB metadata must be readable and its content ID
must match the implementation's portable CodeView GUID/stamp. Legacy symbols
bind the same DLL/PDB bytes; `.snupkg` contains the validated PDBs, not DLLs.
Every expected entry is required, and unexpected managed/PDB entries fail.
All archives are read/decompressed and leased through build completion.
This is not a full IL, PDB-semantic or adversarial-storage verifier.

Windows sharing locks prevent pathname replacement or writing through normal
file APIs while leased. POSIX native writers need not respect those locks:
post-pack comparison is necessary, and release tooling must retain its own
validated package snapshot/lease through later consumption and finalization.
A filesystem/path check followed by an unprotected reopen is not that boundary.
No check is a claim of adversarial storage or post-release immutability.

The wrapper's `eng/ReferenceIntegrity.ps1` compiles the same metadata reader
in PowerShell. `Invoke-WithManagedImages` acquires all inputs before invoking a
consumer and disposes all acquisitions on failure/interruption. Consumers
must read the returned snapshots via `OpenRead()` rather than reopen `Path`.
The returned stream is not writable and cannot expose its backing array.
`New-ManagedImageSnapshot` validates archive-entry bytes using the same reader
and clones the caller's buffer. Dispose that snapshot after consuming its
read-only stream. The release owner must still bind the enclosing archive
and any separately shipped symbol packages through finalization.
The wrapper now records symbol archives separately in `SymbolPackages`, retains
their leases and hashes, and checks them against normal-package implementations.
Its consumer cache guard is separate: validating this task's or the wrapper's
private extraction is not evidence about files used by a consumer.

Focused regressions in the wrapper:

* `tests/ReferenceIntegrity.Tests.ps1`: malformed/truncated/zero images,
  wrong identity, same-MVID wrong bytes, immutable snapshots and interruption.
* `tests/ReferenceIntegrity.Build.Tests.ps1`: tiny compiler-produced fixtures
  and actual SDK NuGet Pack, original existence-only negative control,
  corrupt reference/implementation/archive and interruption.
* `tests/Invoke-ReferencePublicationProbe.ps1`: bounded actual Csc →
  CopyRefAssembly → Csc graph, fresh-output/warm-recompile and
  normal/diagnostic/concurrent controls on separately owned volumes.

These are build/artifact tests, not application or physical-input acceptance.
The subprocess Pack tests prove unlocking after process exit only. The separate
event-driven cancellation fixture opens inputs exclusively **before** disposing
its still-live BuildManager/process. Neither injected target errors nor process
teardown alone establish build-lifetime cancellation cleanup.
The probe records task order, sampled hashes/file IDs and distinct SDK/observer
runtimes. It does not prove kernel write ordering, compiler process identity,
block cloning or cold filesystem caches. A full MAUI rebuild and filtered
kernel tracing are separate prerequisites; preserve original failed trees.
