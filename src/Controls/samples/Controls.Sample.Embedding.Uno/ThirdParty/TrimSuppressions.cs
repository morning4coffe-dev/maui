using System.Diagnostics.CodeAnalysis;

// Scoped to the assembly that actually needs it.
//
// The CommunityToolkit.Maui source compiled here is not trim-annotated: its converters call
// Activator.CreateInstance, its behaviours construct string-path bindings internally, and
// ICommunityToolkitValueConverter annotates its targetType parameter differently from the IValueConverter
// it overrides. None of that is fixable without editing third-party source.
//
// This used to be a NoWarn on the WebAssembly head, which suppressed the same diagnostics for the sample's
// own code as well — and IL2026 had already caught two real string-path bindings there. A module-scoped
// suppression keeps the third-party noise out while leaving the sample's own code fully analysed.
//
// The suppression asserts nothing about correctness. What settles that is running the trimmed build in a
// browser and checking the controls actually work; see the sample README.

[module: UnconditionalSuppressMessage(
	"Trimming",
	"IL2026:RequiresUnreferencedCode",
	Scope = "module",
	Justification = "Third-party source that is not trim-annotated; verified by running the trimmed build.")]
[module: UnconditionalSuppressMessage(
	"Trimming",
	"IL2062:UnrecognizedReflectionPattern",
	Scope = "module",
	Justification = "Third-party source that is not trim-annotated; verified by running the trimmed build.")]
[module: UnconditionalSuppressMessage(
	"Trimming",
	"IL2067:UnrecognizedReflectionPattern",
	Scope = "module",
	Justification = "Third-party source that is not trim-annotated; verified by running the trimmed build.")]
[module: UnconditionalSuppressMessage(
	"Trimming",
	"IL2089:DynamicallyAccessedMembersMismatch",
	Scope = "module",
	Justification = "Third-party source that is not trim-annotated; verified by running the trimmed build.")]
[module: UnconditionalSuppressMessage(
	"Trimming",
	"IL2091:DynamicallyAccessedMembersMismatch",
	Scope = "module",
	Justification = "Third-party source that is not trim-annotated; verified by running the trimmed build.")]
[module: UnconditionalSuppressMessage(
	"Trimming",
	"IL2092:DynamicallyAccessedMembersMismatch",
	Scope = "module",
	Justification = "Third-party source that is not trim-annotated; verified by running the trimmed build.")]
