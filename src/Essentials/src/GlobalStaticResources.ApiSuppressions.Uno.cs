#if UNO
using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
	"ApiDesign",
	"RS0016:Add public types and members to the declared API",
	Justification = "GlobalStaticResources is generated infrastructure from Uno's XAML compiler.",
	Scope = "type",
	Target = "~T:Microsoft.Maui.Essentials.GlobalStaticResources")]
#endif
