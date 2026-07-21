#nullable disable

using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Controls
{
	public partial class VisualElement
	{
		IElementHandler _batchedPropertyUpdateHandler;

		partial void OnBatchBegin()
		{
			if (!RuntimeFeature.IsNativeViewPropertyUpdateBatchingEnabled)
				return;

			var handler = Handler;
			handler?.Invoke(ViewHandler.BeginNativePropertyUpdateBatchCommand);
			_batchedPropertyUpdateHandler = handler;
		}

		partial void OnBatchCommit()
		{
			var handler = _batchedPropertyUpdateHandler;
			_batchedPropertyUpdateHandler = null;
			handler?.Invoke(ViewHandler.CommitNativePropertyUpdateBatchCommand);
		}
	}
}