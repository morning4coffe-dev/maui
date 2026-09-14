# SDK-owned SourceLink

MAUI source builds pin **.NET SDK 10.0.111** in both `global.json` selections:
`tools.dotnet` for bootstrap and `sdk.version`/`rollForward=disable` for direct
SDK resolution. Official .NET 10 release metadata lists that SDK in the
2026-08-11 security release.

[GHSA-23fw-v26w-5fgq](https://github.com/advisories/GHSA-23fw-v26w-5fgq)
identifies affected Git/SourceLink build tasks, including SDK 10.0.102 through
10.0.110. An SDK upgrade is insufficient if package imports still override its
tasks. The former CI-only `Microsoft.SourceLink.GitHub` 1.1.1 reference was
therefore removed in favor of the SDK's bundled SourceLink. This does not claim
that a `Microsoft.SourceLink.GitHub` NuGet version 10.0.111 exists, nor that
1.1.1 was specifically listed by that advisory.

`SourceLink.Build.props` is imported for all source projects. Its validation
target rejects another selected SDK (`MAUISL001`) or explicit/stale
`Microsoft.SourceLink.*` / `Microsoft.Build.Tasks.Git` package overrides
(`MAUISL002`) before restore graph generation, SCM queries and compilation.
It does not disable SourceLink, source-control queries, auditing or warnings.
Existing SDK/Arcade design-time behavior is not replaced.

Restore a fresh **owned project graph** after removing an override; never
edit restored packages, delete shared caches, inject task DLLs or force
`SuppressImplicitGitSourceLink` to conceal stale imports. Task paths/hashes and
portable-PDB SourceLink contents must be verified with the actual owned patched
SDK before a new provenance cohort is accepted.

This correction is deliberately independent of embedding, renderer handlers
and the package-integrity algorithms so it can be backported to foundation
without importing those changes. Historical validation at SDK 10.0.108 remains
historical evidence, not proof of the patched toolchain. No exploit or disclosure
was observed.
