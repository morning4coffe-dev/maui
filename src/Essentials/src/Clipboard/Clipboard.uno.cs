#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;

using WindowsClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Microsoft.Maui.ApplicationModel.DataTransfer
{
	partial class ClipboardImplementation : IClipboard
	{
		public Task SetTextAsync(string? text)
		{
			var dataPackage = new DataPackage();
			dataPackage.SetText(text ?? string.Empty);
			WindowsClipboard.SetContent(dataPackage);
			return Task.CompletedTask;
		}

		public bool HasText =>
			OperatingSystem.IsBrowser()
				? throw new FeatureNotSupportedException("Clipboard.HasText is not supported by the Uno WebAssembly clipboard projection.")
				: WindowsClipboard.GetContent().Contains(StandardDataFormats.Text);

		public async Task<string?> GetTextAsync()
		{
			var clipboardContent = WindowsClipboard.GetContent();
			return clipboardContent.Contains(StandardDataFormats.Text)
				? await clipboardContent.GetTextAsync()
				: null;
		}

		void StartClipboardListeners() =>
			WindowsClipboard.ContentChanged += ClipboardChangedEventListener;

		void StopClipboardListeners() =>
			WindowsClipboard.ContentChanged -= ClipboardChangedEventListener;

		void ClipboardChangedEventListener(object? sender, object args) =>
			OnClipboardContentChanged();
	}
}
