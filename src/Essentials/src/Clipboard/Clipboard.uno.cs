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
			if (OperatingSystem.IsBrowser())
				return Task.FromException(CreateUnsupportedException());

			var dataPackage = new DataPackage();
			dataPackage.SetText(text ?? string.Empty);
			WindowsClipboard.SetContent(dataPackage);
			return Task.CompletedTask;
		}

		public bool HasText =>
			OperatingSystem.IsBrowser()
				? throw CreateUnsupportedException()
				: WindowsClipboard.GetContent()?.Contains(StandardDataFormats.Text) == true;

		public async Task<string?> GetTextAsync()
		{
			if (OperatingSystem.IsBrowser())
				throw CreateUnsupportedException();

			var clipboardContent = WindowsClipboard.GetContent();
			return clipboardContent?.Contains(StandardDataFormats.Text) == true
				? await clipboardContent.GetTextAsync()
				: null;
		}

		void StartClipboardListeners()
		{
			EnsureListenerSupport();
			WindowsClipboard.ContentChanged += ClipboardChangedEventListener;
		}

		void StopClipboardListeners()
		{
			EnsureListenerSupport();
			WindowsClipboard.ContentChanged -= ClipboardChangedEventListener;
		}

		void ClipboardChangedEventListener(object? sender, object args) =>
			OnClipboardContentChanged();

		static void EnsureListenerSupport()
		{
			if (OperatingSystem.IsBrowser())
				throw CreateUnsupportedException();
		}

		static FeatureNotSupportedException CreateUnsupportedException() =>
			new("Clipboard is not supported by the Uno WebAssembly host.");
	}
}
