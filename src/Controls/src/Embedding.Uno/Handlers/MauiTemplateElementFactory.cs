using System;
using System.Collections.Generic;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PlatformView = Microsoft.UI.Xaml.FrameworkElement;
using GetArgs = Microsoft.UI.Xaml.Controls.ElementFactoryGetArgs;
using RecycleArgs = Microsoft.UI.Xaml.Controls.ElementFactoryRecycleArgs;

namespace Microsoft.Maui.Controls.Embedding.Uno;

/// <summary>
/// Realizes a MAUI <see cref="DataTemplate"/> into an Uno element for each item an
/// <c>ItemsRepeater</c> asks for.
/// </summary>
/// <remarks>
/// <para>
/// The two template systems do not meet anywhere, so this is where they are joined: the MAUI template
/// produces a MAUI <see cref="View"/>, and <c>ToPlatform</c> turns that into the Uno element the repeater
/// wants. Everything below the join is an ordinary Uno visual tree.
/// </para>
/// <para>
/// This derives from <c>ElementFactory</c> rather than implementing <c>IElementFactory</c> directly.
/// <c>ItemsRepeater.ItemTemplate</c> is typed as <see cref="object"/> and accepts only a
/// <see cref="DataTemplate"/> or something it can treat as its internal element-factory shim; a bare
/// <c>IElementFactory</c> is rejected at assignment with an <see cref="ArgumentException"/>.
/// </para>
/// <para>
/// Items are not recycled into new data. A recycled MAUI view would have to be re-bound and have its
/// handler re-attached, and MAUI's own Windows handler does not recycle either; correctness first, and the
/// item count in an embedded island is small.
/// </para>
/// </remarks>
sealed class MauiTemplateElementFactory : ElementFactory
{
	readonly Dictionary<PlatformView, View> _realized = new();
	readonly Element _owner;
	readonly IMauiContext _mauiContext;
	readonly DataTemplate _template;
	readonly Action<object?>? _onItemInvoked;

	public MauiTemplateElementFactory(
		Element owner,
		IMauiContext mauiContext,
		DataTemplate template,
		Action<object?>? onItemInvoked)
	{
		_owner = owner;
		_mauiContext = mauiContext;
		_template = template;
		_onItemInvoked = onItemInvoked;
	}

	protected override UIElement GetElementCore(GetArgs args)
	{
		var item = args.Data;

		// A selector picks per item; a plain template is used as-is.
		var template = _template is DataTemplateSelector selector
			? selector.SelectTemplate(item, _owner)
			: _template;

		var view = (View)template.CreateContent();

		// Parenting before binding is what lets the item resolve dynamic resources and inherited state
		// through the embedded window rather than only seeing its own bindable properties.
		view.Parent = _owner;
		view.BindingContext = item;

		var platformView = (PlatformView)view.ToPlatform(_mauiContext);

		_realized[platformView] = view;

		if (_onItemInvoked is not null)
		{
			platformView.Tapped += OnItemTapped;
		}

		return platformView;
	}

	protected override void RecycleElementCore(RecycleArgs args)
	{
		if (args.Element is not PlatformView platformView)
		{
			return;
		}

		platformView.Tapped -= OnItemTapped;

		if (_realized.Remove(platformView, out var view))
		{
			// Leaving the handler attached keeps the whole item subtree alive for as long as the island.
			((IView)view).DisconnectHandlers();
			view.Parent = null;
		}
	}

	/// <summary>Releases every realized item, for when the source or template is replaced wholesale.</summary>
	public void Clear()
	{
		foreach (var pair in _realized)
		{
			pair.Key.Tapped -= OnItemTapped;
			((IView)pair.Value).DisconnectHandlers();
			pair.Value.Parent = null;
		}

		_realized.Clear();
	}

	void OnItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
	{
		if (sender is PlatformView platformView && _realized.TryGetValue(platformView, out var view))
		{
			_onItemInvoked?.Invoke(view.BindingContext);
		}
	}
}
