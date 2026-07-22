using System;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using UIKit;

namespace Microsoft.Maui.Handlers
{
	internal sealed class ExperimentalConfigurationButtonHandler : ButtonHandler
	{
		const string ConfigurationSnapshotKey = "ExperimentalUIButtonConfigurationSnapshot";

		static readonly IPropertyMapper<IButton, IButtonHandler> ConfigurationMapper =
			CreateConfigurationMapper();
		static readonly CommandMapper<IButton, IButtonHandler> ConfigurationCommandMapper =
			CreateConfigurationCommandMapper();

		ImageSourcePartLoader? _configurationImageSourceLoader;
		UIImage? _configurationImage;
		bool _configurationBatchActive;
		bool _configurationBatchDirty;

		public ExperimentalConfigurationButtonHandler()
			: base(ConfigurationMapper, ConfigurationCommandMapper)
		{
		}

		internal long ConfigurationApplyCount { get; private set; }

		internal long ConfigurationBatchFlushCount { get; private set; }

		public override ImageSourcePartLoader ImageSourceLoader =>
			_configurationImageSourceLoader ??=
				new ImageSourcePartLoader(new ConfigurationImageSourcePartSetter(this));

		public override void SetVirtualView(IView view)
		{
			var currentVirtualView = ((IElementHandler)this).VirtualView;
			var reconnecting =
				currentVirtualView is not null &&
				!ReferenceEquals(currentVirtualView, view);

			if (reconnecting)
			{
				_configurationImageSourceLoader?.Reset();
				_configurationBatchActive = false;
				_configurationBatchDirty = false;
				base.DisconnectHandler(PlatformView);
			}

			if (!ReferenceEquals(currentVirtualView, view))
				_configurationImage = null;

			base.SetVirtualView(view);

			if (reconnecting)
				base.ConnectHandler(PlatformView);
		}

		protected override void DisconnectHandler(UIButton platformView)
		{
			_configurationImageSourceLoader?.Reset();
			_configurationBatchActive = false;
			_configurationBatchDirty = false;
			_configurationImage = null;

			base.DisconnectHandler(platformView);
		}

		internal void ResetConfigurationDiagnostics()
		{
			ConfigurationApplyCount = 0;
			ConfigurationBatchFlushCount = 0;
		}

		static ConfigurationPropertyMapper CreateConfigurationMapper()
		{
			var mapper = new ConfigurationPropertyMapper(ButtonHandler.Mapper)
			{
				[ConfigurationSnapshotKey] = MapConfigurationSnapshot,
				[nameof(IText.Text)] = MapConfigurationProperty,
				[nameof(ITextStyle.TextColor)] = MapConfigurationProperty,
				[nameof(ITextStyle.Font)] = MapConfigurationProperty,
				[nameof(ITextStyle.CharacterSpacing)] = MapConfigurationProperty,
				[nameof(IButton.Background)] = MapConfigurationProperty,
				[nameof(IButton.Padding)] = MapConfigurationProperty,
				[nameof(IButtonStroke.StrokeColor)] = MapConfigurationProperty,
				[nameof(IButtonStroke.StrokeThickness)] = MapConfigurationProperty,
				[nameof(IButtonStroke.CornerRadius)] = MapConfigurationProperty,
				[nameof(IView.FlowDirection)] = MapFlowDirection,
				[nameof(IImage.Source)] = MapImageSource,
			};

			return mapper;
		}

		static CommandMapper<IButton, IButtonHandler> CreateConfigurationCommandMapper()
		{
			var mapper = new CommandMapper<IButton, IButtonHandler>(ButtonHandler.CommandMapper);

			mapper.ModifyMapping(
				ViewHandler.BeginNativePropertyUpdateBatchCommand,
				static (handler, view, args, next) =>
				{
					next?.Invoke(handler, view, args);

					if (handler is ExperimentalConfigurationButtonHandler configurationHandler)
						configurationHandler.BeginConfigurationBatch();
				});

			mapper.ModifyMapping(
				ViewHandler.CommitNativePropertyUpdateBatchCommand,
				static (handler, view, args, next) =>
				{
					next?.Invoke(handler, view, args);

					if (handler is ExperimentalConfigurationButtonHandler configurationHandler)
						configurationHandler.CommitConfigurationBatch(view);
				});

			return mapper;
		}

		static void MapConfigurationSnapshot(IButtonHandler handler, IButton button)
		{
			if (handler is ExperimentalConfigurationButtonHandler configurationHandler)
				configurationHandler.ApplyConfiguration(button);
		}

		static void MapConfigurationProperty(IButtonHandler handler, IButton button)
		{
			if (handler.IsMappingProperties() ||
				handler is not ExperimentalConfigurationButtonHandler configurationHandler)
			{
				return;
			}

			configurationHandler.ApplyConfigurationOrQueue(button);
		}

		static void MapImageSource(IButtonHandler handler, IButton button)
		{
			if (button is not IImage image)
				return;

			if (image.Source is not null)
			{
				ButtonHandler.MapImageSource(handler, image);
				return;
			}

			if (handler is not ExperimentalConfigurationButtonHandler configurationHandler)
				return;

			configurationHandler._configurationImageSourceLoader?.Reset();
			configurationHandler._configurationImage = null;

			if (!handler.IsMappingProperties())
				configurationHandler.ApplyConfigurationOrQueue(button);
		}

		static void MapFlowDirection(IButtonHandler handler, IButton button)
		{
			ViewHandler.MapFlowDirection(handler, button);
			MapConfigurationProperty(handler, button);
		}

		void ApplyConfigurationOrQueue(IButton button)
		{
			if (_configurationBatchActive &&
				RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled)
			{
				_configurationBatchDirty = true;
				return;
			}

			ApplyConfiguration(button);
		}

		void ApplyConfiguration(IButton button)
		{
			if (button is not IText text)
			{
				throw new InvalidOperationException(
					"The configuration-backed button experiment requires an IText button.");
			}

			var padding = button.Padding;
			if (padding.IsNaN)
				padding = DefaultPadding;

			var fontManager = this.GetRequiredService<IFontManager>();
			var font = fontManager.GetFont(text.Font, UIFont.ButtonFontSize);
			var textColor = text.TextColor?.ToPlatform();
			UIColor? backgroundColor = null;
			if (button.Background is SolidPaint solidPaint)
			{
				backgroundColor = solidPaint.Color?.ToPlatform();
			}
			else if (!button.Background.IsNullOrEmpty())
			{
				throw new NotSupportedException(
					"The configuration-backed button experiment currently supports only solid backgrounds.");
			}

			var strokeColor = button.StrokeColor?.ToPlatform();

			MauiUIButtonConfigurationBatcher.Apply(
				PlatformView,
				text.Text ?? string.Empty,
				font,
				text.CharacterSpacing,
				textColor,
				backgroundColor,
				_configurationImage,
				padding.Top,
				padding.Left,
				padding.Bottom,
				padding.Right,
				IsRightToLeft(button),
				strokeColor,
				button.StrokeThickness,
				button.CornerRadius);

			ConfigurationApplyCount++;
		}

		static bool IsRightToLeft(IButton button)
		{
			// The configuration snapshot runs before the regular FlowDirection mapper.
			if (button.FlowDirection == FlowDirection.RightToLeft)
				return true;

			if (button.FlowDirection != FlowDirection.MatchParent ||
				button.Parent?.Handler?.PlatformView is not UIView parentView)
			{
				return false;
			}

			return parentView.SemanticContentAttribute ==
				UISemanticContentAttribute.ForceRightToLeft;
		}

		void BeginConfigurationBatch()
		{
			if (!RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled ||
				_configurationBatchActive)
			{
				return;
			}

			_configurationBatchActive = true;
			_configurationBatchDirty = false;
		}

		void CommitConfigurationBatch(IButton button)
		{
			if (!_configurationBatchActive)
				return;

			_configurationBatchActive = false;

			if (!_configurationBatchDirty)
				return;

			_configurationBatchDirty = false;
			ApplyConfiguration(button);
			ConfigurationBatchFlushCount++;
		}

		sealed class ConfigurationPropertyMapper : PropertyMapper<IButton, IButtonHandler>
		{
			public ConfigurationPropertyMapper(params IPropertyMapper[] chained)
				: base(chained)
			{
			}

			public override IEnumerable<string> GetKeys()
			{
				yield return ConfigurationSnapshotKey;

				foreach (var key in base.GetKeys())
				{
					if (key != ConfigurationSnapshotKey)
						yield return key;
				}
			}
		}

		sealed class ConfigurationImageSourcePartSetter : ImageSourcePartSetter<IButtonHandler>
		{
			readonly ExperimentalConfigurationButtonHandler _handler;

			public ConfigurationImageSourcePartSetter(
				ExperimentalConfigurationButtonHandler handler)
				: base(handler)
			{
				_handler = handler;
			}

			public override void SetImageSource(UIImage? platformImage)
			{
				_handler._configurationImage =
					platformImage?.ImageWithRenderingMode(
						UIImageRenderingMode.AlwaysOriginal);

				if (_handler.VirtualView is IButton button)
					_handler.ApplyConfigurationOrQueue(button);
			}
		}
	}
}
