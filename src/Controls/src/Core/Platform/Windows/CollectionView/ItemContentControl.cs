#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using NativeAutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;
using WSize = Windows.Foundation.Size;
using WThickness = Microsoft.UI.Xaml.Thickness;

namespace Microsoft.Maui.Controls.Platform
{
	public partial class ItemContentControl : ContentControl
	{
		VisualElement _visualElement;
		IViewHandler _handler;
		DataTemplate _currentTemplate;
		bool _isMeasureInvalidationPending;
		readonly HashSet<Element> _semanticElements = new();
		VisualElement _subscribedContent;

		public ItemContentControl()
		{
			DefaultStyleKey = typeof(ItemContentControl);
			IsTabStop = false;
			Loaded += OnLoaded;
			Unloaded += OnUnloaded;
		}

		public static readonly DependencyProperty MauiContextProperty = DependencyProperty.Register(
			nameof(MauiContext), typeof(IMauiContext), typeof(ItemContentControl),
			new PropertyMetadata(default(IMauiContext), MauiContextChanged));

		static void MauiContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (e.NewValue == null)
			{
				return;
			}

			var itemContentControl = (ItemContentControl)d;
			itemContentControl.Realize();
		}

		public IMauiContext MauiContext
		{
			get => (IMauiContext)GetValue(MauiContextProperty);
			set => SetValue(MauiContextProperty, value);
		}

		public static readonly DependencyProperty FormsDataTemplateProperty = DependencyProperty.Register(
			nameof(FormsDataTemplate), typeof(DataTemplate), typeof(ItemContentControl),
			new PropertyMetadata(default(DataTemplate), FormsDataTemplateChanged));

		static void FormsDataTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (e.NewValue == null)
			{
				return;
			}

			var itemContentControl = (ItemContentControl)d;
			itemContentControl.Realize();
		}

		public DataTemplate FormsDataTemplate
		{
			get => (DataTemplate)GetValue(FormsDataTemplateProperty);
			set => SetValue(FormsDataTemplateProperty, value);
		}

		public static readonly DependencyProperty FormsDataContextProperty = DependencyProperty.Register(
			nameof(FormsDataContext), typeof(object), typeof(ItemContentControl),
			new PropertyMetadata(default(object), FormsDataContextChanged));

		static void FormsDataContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var formsContentControl = (ItemContentControl)d;
			formsContentControl.Realize();
		}

		public object FormsDataContext
		{
			get => GetValue(FormsDataContextProperty);
			set => SetValue(FormsDataContextProperty, value);
		}

		public static readonly DependencyProperty FormsContainerProperty = DependencyProperty.Register(
			nameof(FormsContainer), typeof(BindableObject), typeof(ItemContentControl),
			new PropertyMetadata(default(BindableObject), FormsContainerChanged));

		static void FormsContainerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var formsContentControl = (ItemContentControl)d;
			formsContentControl.Realize();
		}

		public BindableObject FormsContainer
		{
			get => (BindableObject)GetValue(FormsContainerProperty);
			set => SetValue(FormsContainerProperty, value);
		}

		public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
			nameof(ItemHeight), typeof(double), typeof(ItemContentControl),
			new PropertyMetadata(default(double), OnItemDimensionChanged));

		public double ItemHeight
		{
			get => (double)GetValue(ItemHeightProperty);
			set => SetValue(ItemHeightProperty, value);
		}

		public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
			nameof(ItemWidth), typeof(double), typeof(ItemContentControl),
			new PropertyMetadata(default(double), OnItemDimensionChanged));

		public double ItemWidth
		{
			get => (double)GetValue(ItemWidthProperty);
			set => SetValue(ItemWidthProperty, value);
		}

		static void OnItemDimensionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (Equals(e.OldValue, e.NewValue))
			{
				return;
			}

			((ItemContentControl)d).InvalidateItemDimensionMeasure();
		}

		public static readonly DependencyProperty ItemSpacingProperty = DependencyProperty.Register(
			nameof(ItemSpacing), typeof(Thickness), typeof(ItemContentControl),
			new PropertyMetadata(default(Thickness)));

		public Thickness ItemSpacing
		{
			get => (Thickness)GetValue(ItemSpacingProperty);
			set => SetValue(ItemSpacingProperty, value);
		}

		protected override void OnContentChanged(object oldContent, object newContent)
		{
			base.OnContentChanged(oldContent, newContent);

			UnsubscribeSemanticTree();

			if (newContent != null && _visualElement != null)
			{
				SubscribeSemanticTree(_visualElement);
				UpdateSemanticProperties(_visualElement);
			}
		}

		internal void Realize()
		{
			var dataContext = FormsDataContext;
			var dataTemplate = FormsDataTemplate;
			var container = FormsContainer;
			var mauiContext = MauiContext;

			var itemsView = container as ItemsView;

			if (itemsView != null && _handler?.VirtualView is Element e)
			{
				itemsView.RemoveLogicalChild(e);
			}

			if (dataContext is null || dataTemplate is null || container is null || mauiContext is null)
			{
				return;
			}

			// If the template is a DataTemplateSelector, we need to get the actual DataTemplate to use
			// for our recycle check below
			dataTemplate = dataTemplate.SelectDataTemplate(dataContext, container);

			if (Content is null || _currentTemplate != dataTemplate)
			{
				// If the content has never been realized (i.e., this is a new instance), 
				// or if we need to switch DataTemplates (because this instance is being recycled)
				// then we'll need to create the content from the template 
				var content = dataTemplate.CreateContent();
				_visualElement = content as VisualElement;

				if (_visualElement is null)
				{
					throw new InvalidOperationException($"{dataTemplate} could not be created from {content}");
				}

				_visualElement.BindingContext = dataContext;
				itemsView?.AddLogicalChild(_visualElement);
				_handler = _visualElement.ToHandler(mauiContext);

				// We need to set IsPlatformStateConsistent explicitly; otherwise, it won't be set until the renderer's Loaded 
				// event. If the CollectionView is in a Layout, the Layout won't measure or layout the CollectionView until
				// every visible descendant has `IsPlatformStateConsistent == true`. And the problem that Layout is trying
				// to avoid by skipping layout for controls with not-yet-loaded children does not apply to CollectionView
				// items. If we don't set this, the CollectionView just won't get layout at all, and will be invisible until
				// the window is resized. 
				SetNativeStateConsistent(_visualElement);

				// Keep track of the template in case this instance gets reused later
				_currentTemplate = dataTemplate;
			}
			else
			{
				// We are reusing this ItemContentControl and we can reuse the Element
				_visualElement.BindingContext = dataContext;

				// Re-add to the logical tree after RemoveLogicalChild was called at the top of Realize(),
				// so styles, resources, and VSM states continue to resolve correctly on recycled cells.
				itemsView?.AddLogicalChild(_visualElement);
			}

			if (_handler.VirtualView is ICrossPlatformLayout)
			{
				Content = _handler.ToPlatform();
			}
			else
			{
				Content = new ContentLayoutPanel(_handler.VirtualView);
			}
			UpdateSemanticProperties(_visualElement);

			if (itemsView is SelectableItemsView selectableItemsView && selectableItemsView.SelectionMode is not SelectionMode.None)
			{
				bool isSelected = false;
				if (selectableItemsView.SelectionMode == SelectionMode.Single)
					isSelected = object.Equals(selectableItemsView.SelectedItem, FormsDataContext);
				else
					isSelected = selectableItemsView.SelectedItems.Contains(FormsDataContext);

				if (isSelected)
					UpdateIsSelected(isSelected);
			}
		}

		void SetNativeStateConsistent(VisualElement visualElement)
		{
			visualElement.IsPlatformStateConsistent = true;

			foreach (var child in ((IElementController)visualElement).LogicalChildren)
			{
				if (!(child is VisualElement ve))
				{
					continue;
				}

				SetNativeStateConsistent(ve);
			}
		}

		internal void UpdateIsSelected(bool isSelected)
		{
			var formsElement = _handler?.VirtualView as VisualElement;

			if (formsElement == null)
				return;

			formsElement.IsItemSelected = isSelected;
		}

		void OnViewMeasureInvalidated(object sender, EventArgs e)
		{
			InvalidateMeasure();
		}

		void InvalidateItemDimensionMeasure()
		{
			if (_isMeasureInvalidationPending)
			{
				return;
			}

			var dispatcherQueue = DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();

			if (dispatcherQueue is null)
			{
				InvalidateMeasure();
				return;
			}

			_isMeasureInvalidationPending = true;

			if (!dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
				{
					_isMeasureInvalidationPending = false;
					InvalidateMeasure();
				}))
			{
				_isMeasureInvalidationPending = false;
				InvalidateMeasure();
			}
		}

		void OnViewPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if ((e.IsOneOf(
				SemanticProperties.HeadingLevelProperty,
				SemanticProperties.HintProperty,
				SemanticProperties.DescriptionProperty,
				AutomationProperties.IsInAccessibleTreeProperty,
				Label.TextProperty) ||
				e.IsOneOf(
					AutomationProperties.ExcludedWithChildrenProperty,
					VisualElement.IsVisibleProperty)) &&
				_visualElement is not null)
			{
				UpdateSemanticProperties(_visualElement);
			}
		}

		void SubscribeSemanticTree(VisualElement root)
		{
			UnsubscribeSemanticTree();
			_subscribedContent = root;
			root.MeasureInvalidated += OnViewMeasureInvalidated;
			root.DescendantAdded += OnSemanticDescendantAdded;
			root.DescendantRemoved += OnSemanticDescendantRemoved;
			Subscribe(root);

			void Subscribe(Element element)
			{
				SubscribeSemanticElement(element);

				foreach (var child in ((IElementController)element).LogicalChildren)
				{
					Subscribe(child);
				}
			}
		}

		void SubscribeSemanticElement(Element element)
		{
			if (!_semanticElements.Add(element))
				return;

			element.PropertyChanged += OnViewPropertyChanged;
			if (element is View view && view.GestureRecognizers is INotifyCollectionChanged gestures)
				gestures.CollectionChanged += OnSemanticGesturesChanged;
		}

		void UnsubscribeSemanticElement(Element element)
		{
			element.PropertyChanged -= OnViewPropertyChanged;
			if (element is View view && view.GestureRecognizers is INotifyCollectionChanged gestures)
				gestures.CollectionChanged -= OnSemanticGesturesChanged;
		}

		void OnSemanticDescendantAdded(object sender, ElementEventArgs e)
		{
			SubscribeSemanticElement(e.Element);
			UpdateSemanticProperties(_visualElement);
		}

		void OnSemanticDescendantRemoved(object sender, ElementEventArgs e)
		{
			if (_semanticElements.Remove(e.Element))
				UnsubscribeSemanticElement(e.Element);
			UpdateSemanticProperties(_visualElement);
		}

		void OnSemanticGesturesChanged(object sender, NotifyCollectionChangedEventArgs e) =>
			UpdateSemanticProperties(_visualElement);

		void UnsubscribeSemanticTree()
		{
			if (_subscribedContent is not null)
			{
				_subscribedContent.MeasureInvalidated -= OnViewMeasureInvalidated;
				_subscribedContent.DescendantAdded -= OnSemanticDescendantAdded;
				_subscribedContent.DescendantRemoved -= OnSemanticDescendantRemoved;
				_subscribedContent = null;
			}
			foreach (var element in _semanticElements)
				UnsubscribeSemanticElement(element);
			_semanticElements.Clear();
		}

		void OnLoaded(object sender, RoutedEventArgs e)
		{
			if (_visualElement is not null)
			{
				if (Content is not null && _subscribedContent is null)
					SubscribeSemanticTree(_visualElement);
				UpdateSemanticProperties(_visualElement);
			}
		}

		void OnUnloaded(object sender, RoutedEventArgs e) => UnsubscribeSemanticTree();

		void UpdateSemanticProperties(IView view)
		{
			// If you don't set the automation properties on the root element
			// of a list item it just reads out the class type to narrator
			// https://docs.microsoft.com/en-us/accessibility-tools-docs/items/uwpxaml/listitem_name
			// Because this is the root element of the ListViewItem we need to propagate
			// the semantic properties from the root xplat element to this platform element
			if (view == null)
				return;

			this.UpdateSemantics(view);

			var semantics = view.Semantics;
			var itemName = semantics?.Description;
			if (string.IsNullOrWhiteSpace(itemName) && view is VisualElement visualElement)
			{
				itemName = GetTemplateText(visualElement);
			}
			if (string.IsNullOrWhiteSpace(itemName) &&
				FormsContainer is ItemsView { ItemTemplate: null } &&
				FormsDataContext is not null)
			{
				// The default MAUI item template paints the data object's string representation.
				// Use the same value for its container name so pixels and accessibility stay aligned.
				itemName = FormsDataContext.ToString();
			}
			NativeAutomationProperties.SetName(this, itemName);

			UI.Xaml.Automation.Peers.AccessibilityView defaultAccessibilityView =
				UI.Xaml.Automation.Peers.AccessibilityView.Content;

			if (!String.IsNullOrWhiteSpace(semantics?.Description) || !String.IsNullOrWhiteSpace(semantics?.Hint))
			{
				defaultAccessibilityView = UI.Xaml.Automation.Peers.AccessibilityView.Raw;
			}

			this.SetAutomationPropertiesAccessibilityView(_visualElement, defaultAccessibilityView);

			var parent = VisualTreeHelper.GetParent(this);
			while (parent is not null && parent is not SelectorItem)
			{
				parent = VisualTreeHelper.GetParent(parent);
			}

			if (parent is SelectorItem itemContainer)
			{
				NativeAutomationProperties.SetName(itemContainer, itemName);
				NativeAutomationProperties.SetHelpText(itemContainer, semantics?.Hint);
			}
		}

		static string GetTemplateText(VisualElement root)
		{
			StringBuilder text = null;
			Append(root, true);
			return text?.ToString();

			void Append(Element element, bool isRoot)
			{
				if (element is VisualElement { IsVisible: false } ||
					AutomationProperties.GetExcludedWithChildren(element) == true)
				{
					return;
				}

				if (!isRoot && IsIndependentControl(element))
				{
					return;
				}

				if (element is Label { Text: { } labelText } label &&
					AutomationProperties.GetIsInAccessibleTree(label) != false &&
					label.GestureRecognizers.Count == 0 &&
					!string.IsNullOrWhiteSpace(labelText))
				{
					if (SemanticProperties.GetDescription(label) is string description &&
						!string.IsNullOrWhiteSpace(description))
						labelText = description;
					text ??= new StringBuilder();
					if (text.Length > 0)
					{
						text.Append(", ");
					}
					text.Append(labelText);
					return;
				}

				foreach (var child in ((IElementController)element).LogicalChildren)
				{
					Append(child, false);
				}
			}
		}

		static bool IsIndependentControl(Element element) =>
			element is Button or ImageButton or InputView or Picker or DatePicker or TimePicker or
				Slider or Stepper or Switch or CheckBox or RadioButton or SelectableItemsView;

		/// <inheritdoc/>
		protected override WSize ArrangeOverride(WSize finalSize)
		{
			return base.ArrangeOverride(finalSize);
		}

		/// <inheritdoc/>
		protected override WSize MeasureOverride(WSize availableSize)
		{
			if (_handler is null)
			{
				// Make sure we supply a real number for sizes otherwise virtualization won't function
				if (double.IsFinite(availableSize.Width) && !double.IsFinite(availableSize.Height))
					return new WSize(availableSize.Width, 32);
				else if (!double.IsFinite(availableSize.Width) && double.IsFinite(availableSize.Height))
					return new WSize(88, availableSize.Height);

				return base.MeasureOverride(availableSize);
			}

			var width = ItemWidth == default ? availableSize.Width : ItemWidth;
			var height = ItemHeight == default ? availableSize.Height : ItemHeight;

			// I realize if ItemWidth and ItemHeight are set that this call seems pointless, but it's not.
			// From what I can tell, calling `base.MeasureOverride` causes the `ContentControl` to realize its content
			// and build its visual tree. So, in order to just play nice with the WinUI `ContentControl` we always
			// call base.MeasureOverride, so that we don't short circuit whatever bookkeeping it needs to do.

			var measureSize = base.MeasureOverride(new WSize(width, height));

#if UNO
			var virtualViewMeasure = _handler.VirtualView.Measure(width, height);
			measureSize = new WSize(
				Max(measureSize.Width, virtualViewMeasure.Width),
				Max(measureSize.Height, virtualViewMeasure.Height));
#endif

			return new WSize(Max(measureSize.Width, width), Max(measureSize.Height, height));
		}

		double Max(double requested, double available)
		{
			return Math.Max(requested, ClampInfinity(available));
		}

		double ClampInfinity(double value)
		{
			return double.IsInfinity(value) ? 0 : value;
		}

		internal VisualElement GetVisualElement() => _visualElement;

		partial class ContentLayoutPanel : Panel
		{
			IView _view;
			public ContentLayoutPanel(IView view)
			{
				_view = view;

				var platformView = view.ToPlatform();

#pragma warning disable RS0030 // Do not use banned APIs; Panel.Children is banned for performance reasons. Here we cannot avoid accessing it.
				// Just in case this view is already parented to a wrapper that's been cycled out
				if (platformView.Parent is ContentLayoutPanel clp)
					clp.Children.Remove(platformView);

				Children.Add(platformView);
#pragma warning restore RS0030 // Do not use banned APIs
			}

			protected override WSize ArrangeOverride(WSize finalSize) => _view.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height)).ToPlatform();

			protected override WSize MeasureOverride(WSize availableSize) => _view.Measure(availableSize.Width, availableSize.Height).ToPlatform();
		}
	}
}
