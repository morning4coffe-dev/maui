using System;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Controls.Embedding.Uno;

/// <summary>Selects the supported handler configuration for embedded MAUI content.</summary>
public enum UnoHandlerMode
{
	/// <summary>
	/// MAUI's handlers recompiled against Uno.WinUI.
	/// </summary>
	Default,

	/// <summary>
	/// Uses the complete MAUI handler set. This is retained as a compatibility alias for
	/// <see cref="Default"/>.
	/// </summary>
	/// <remarks>
	/// Earlier builds replaced CollectionView and CarouselView with partial ItemsRepeater handlers in
	/// this mode. The default ListViewBase path now paints correctly and preserves the full MAUI contract,
	/// so both values intentionally select the same handlers.
	/// </remarks>
	Full,
}

/// <summary>Configures the supported MAUI handler set for Uno embedding.</summary>
public static class UnoHandlers
{
	/// <summary>
	/// Gets the virtual view types whose handlers are replaced in <see cref="UnoHandlerMode.Full"/>.
	/// </summary>
	/// <remarks>
	/// The collection is empty because Full now uses the same complete MAUI handlers as Default.
	/// </remarks>
	public static IReadOnlyDictionary<Type, string> ReplacedInFullMode { get; } =
		new Dictionary<Type, string>();

	/// <summary>
	/// Configures the embedded MAUI handler mode.
	/// </summary>
	public static MauiAppBuilder UseUnoHandlers(this MauiAppBuilder builder, UnoHandlerMode mode)
	{
		ArgumentNullException.ThrowIfNull(builder);

		if (mode is not UnoHandlerMode.Default and not UnoHandlerMode.Full)
		{
			throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Uno handler mode.");
		}

		return builder;
	}
}
