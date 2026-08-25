using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using MauiPage = Microsoft.Maui.Controls.Page;
using MauiVisualElement = Microsoft.Maui.Controls.VisualElement;

namespace Maui.Controls.Sample.Uno;

/// <summary>Provides data for <see cref="MauiHost.MauiContentRealized"/>.</summary>
public sealed class MauiContentRealizedEventArgs : EventArgs
{
	public MauiContentRealizedEventArgs(MauiVisualElement content) => Content = content;

	/// <summary>Gets the MAUI element that was realized into the Uno visual tree.</summary>
	public MauiVisualElement Content { get; }
}

/// <summary>
/// Hosts embedded .NET MAUI content inside an Uno visual tree.
/// </summary>
/// <remarks>
/// <para>
/// Because MAUI's handlers are compiled against Uno.WinUI, the realized platform view is an ordinary Uno
/// <see cref="FrameworkElement"/>. There is no interop surface: Uno measures, arranges, renders, and routes
/// input to the MAUI content exactly as it does for any other child.
/// </para>
/// <para>
/// The window scope is owned by <see cref="MauiEmbeddingSession"/>, not by this control. Unloading is
/// transient — it also happens during navigation, reparenting, virtualization, and template changes — so it
/// must never dispose a scope that sibling hosts are still using.
/// </para>
/// <para>
/// Content is supplied as an instance rather than as a <see cref="Type"/> to activate, because type-based
/// activation is not trim-safe and this sample is validated against a trimmed WebAssembly publish.
/// </para>
/// </remarks>
public sealed partial class MauiHost : ContentControl
{
	/// <summary>Identifies the <see cref="MauiContent"/> dependency property.</summary>
	public static readonly DependencyProperty MauiContentProperty = DependencyProperty.Register(
		nameof(MauiContent),
		typeof(MauiVisualElement),
		typeof(MauiHost),
		new PropertyMetadata(null, OnMauiContentChanged));

	MauiEmbeddingSession? _session;
	MauiVisualElement? _realizedContent;
	bool _isLoaded;

	public MauiHost()
	{
		// Qualified because the inherited FrameworkElement.HorizontalAlignment property otherwise shadows
		// the type name inside this class.
		HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
		VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;

		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	/// <summary>Raised after MAUI content has been realized into the Uno visual tree.</summary>
	public event EventHandler<MauiContentRealizedEventArgs>? MauiContentRealized;

	/// <summary>Gets or sets the embedding session that owns the window scope this host renders into.</summary>
	public MauiEmbeddingSession? Session
	{
		get => _session;
		set
		{
			if (ReferenceEquals(_session, value))
			{
				return;
			}

			_session = value;
			UpdateContent();
		}
	}

	/// <summary>Gets or sets the MAUI element to embed.</summary>
	public MauiVisualElement? MauiContent
	{
		get => (MauiVisualElement?)GetValue(MauiContentProperty);
		set => SetValue(MauiContentProperty, value);
	}

	static void OnMauiContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
		((MauiHost)sender).UpdateContent();

	void OnLoaded(object sender, RoutedEventArgs args)
	{
		_isLoaded = true;
		UpdateContent();

		if (_realizedContent is MauiPage page)
		{
			page.SendAppearing();
		}
	}

	void OnUnloaded(object sender, RoutedEventArgs args)
	{
		_isLoaded = false;

		if (_realizedContent is MauiPage page)
		{
			page.SendDisappearing();
		}
	}

	void UpdateContent()
	{
		if (_session is not { } session || ReferenceEquals(_realizedContent, MauiContent))
		{
			return;
		}

		if (_realizedContent is { } previous)
		{
			if (_isLoaded && previous is MauiPage previousPage)
			{
				previousPage.SendDisappearing();
			}

			Content = null;
			_realizedContent = null;

			// Releasing unparents the element and disconnects its handlers. Without it, replaced content
			// stays rooted in the embedded window for the lifetime of the Uno window.
			session.Release(previous);
		}

		if (MauiContent is not { } content)
		{
			return;
		}

		Content = session.Embed(content);
		_realizedContent = content;

		if (_isLoaded && content is MauiPage page)
		{
			page.SendAppearing();
		}

		MauiContentRealized?.Invoke(this, new MauiContentRealizedEventArgs(content));
	}
}
