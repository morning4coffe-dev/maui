using System;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Controls.Embedding.Uno;

/// <summary>Selects which set of handlers embedded MAUI content uses.</summary>
public enum UnoHandlerMode
{
	/// <summary>
	/// MAUI's own handlers, recompiled against Uno.WinUI. This is the default and is unchanged.
	/// </summary>
	/// <remarks>
	/// These handlers were written for the Windows App SDK. Most of them work unaltered, because Uno
	/// implements the WinUI surface they target, but a few depend on Windows-specific behaviour of the
	/// controls underneath them and do not survive every Uno target.
	/// </remarks>
	Default,

	/// <summary>
	/// Replaces the handlers that do not work across every Uno target with implementations written against
	/// portable Uno.WinUI primitives.
	/// </summary>
	/// <remarks>
	/// Only the handlers listed by <see cref="UnoHandlers.ReplacedInFullMode"/> are replaced; everything
	/// else still uses MAUI's own handler, so this is additive rather than a parallel implementation.
	/// </remarks>
	Full,
}

/// <summary>Registers the Uno-portable handler replacements.</summary>
public static class UnoHandlers
{
	/// <summary>
	/// Gets the virtual view types whose handler is replaced in <see cref="UnoHandlerMode.Full"/>, and why.
	/// </summary>
	public static IReadOnlyDictionary<Type, string> ReplacedInFullMode { get; } = new Dictionary<Type, string>
	{
		[typeof(CollectionView)] =
			"MAUI's Windows handler renders through ListViewBase with a custom control template and " +
			"ItemsStackPanel virtualization. On WebAssembly the item containers are realized and arranged " +
			"at the correct sizes and then never painted.",
		[typeof(CarouselView)] =
			"Same cause as CollectionView: the Windows handler renders through ListViewBase and paints " +
			"nothing on WebAssembly.",
	};

	/// <summary>
	/// Configures which handlers embedded MAUI content uses.
	/// </summary>
	/// <remarks>
	/// Handler registration is last-one-wins, so this must be called after <c>UseMauiEmbeddedApp</c>, which
	/// is what registers MAUI's own handlers.
	/// </remarks>
	public static MauiAppBuilder UseUnoHandlers(this MauiAppBuilder builder, UnoHandlerMode mode)
	{
		ArgumentNullException.ThrowIfNull(builder);

		if (mode is UnoHandlerMode.Default)
		{
			return builder;
		}

		builder.ConfigureMauiHandlers(handlers =>
		{
			handlers.AddHandler<CollectionView, UnoCollectionViewHandler>();
			handlers.AddHandler<CarouselView, UnoCarouselViewHandler>();
		});

		return builder;
	}
}
