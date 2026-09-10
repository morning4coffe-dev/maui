using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Controls.Embedding.Uno;

/// <summary>
/// Compatibility name for MAUI's complete <see cref="CollectionViewHandler"/>.
/// </summary>
/// <remarks>
/// New code should use <see cref="CollectionViewHandler"/>. This type no longer selects the former
/// partial ItemsRepeater implementation.
/// </remarks>
[Obsolete($"Use {nameof(CollectionViewHandler)}. The Uno-specific replacement is no longer registered.")]
public class UnoCollectionViewHandler : CollectionViewHandler
{
	static readonly PropertyMapper<ReorderableItemsView, UnoCollectionViewHandler> DefaultMapper =
		new(CollectionViewHandler.Mapper);

	public new static readonly IPropertyMapper<ReorderableItemsView, UnoCollectionViewHandler> Mapper =
		DefaultMapper;

	public UnoCollectionViewHandler()
		: base(DefaultMapper)
	{
	}

	public UnoCollectionViewHandler(IPropertyMapper? mapper)
		: base(mapper as PropertyMapper ?? DefaultMapper)
	{
	}
}
