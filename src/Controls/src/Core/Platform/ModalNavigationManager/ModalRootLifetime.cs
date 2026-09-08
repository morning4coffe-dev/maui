using System.Threading;

namespace Microsoft.Maui.Controls.Platform;

internal sealed class ModalRootLifetime
{
	CancellationTokenSource _source = new CancellationTokenSource();

	public CancellationToken Token => _source.Token;

	public void Cancel() => _source.Cancel();

	public void Begin()
	{
		if (_source.IsCancellationRequested)
		{
			_source.Dispose();
			_source = new CancellationTokenSource();
		}
	}
}
