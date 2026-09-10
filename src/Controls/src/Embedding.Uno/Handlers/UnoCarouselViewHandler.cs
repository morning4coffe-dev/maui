using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Controls.Embedding.Uno;

/// <summary>
/// Compatibility name for MAUI's complete <see cref="CarouselViewHandler"/>.
/// </summary>
/// <remarks>
/// New code should use <see cref="CarouselViewHandler"/>. This type no longer selects the former
/// partial ItemsRepeater implementation.
/// </remarks>
[Obsolete($"Use {nameof(CarouselViewHandler)}. The Uno-specific replacement is no longer registered.")]
public class UnoCarouselViewHandler : CarouselViewHandler
{
	static readonly PropertyMapper<CarouselView, UnoCarouselViewHandler> DefaultMapper =
		new(CarouselViewHandler.Mapper);

	public new static readonly IPropertyMapper<CarouselView, UnoCarouselViewHandler> Mapper =
		DefaultMapper;

	public UnoCarouselViewHandler()
		: base(DefaultMapper)
	{
	}

	public UnoCarouselViewHandler(IPropertyMapper? mapper)
		: base(mapper as PropertyMapper ?? DefaultMapper)
	{
	}
}
