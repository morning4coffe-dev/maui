using System.Collections.Generic;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.Maui.Platform
{
	public partial class MauiButtonAutomationPeer : ButtonAutomationPeer
	{
		public MauiButtonAutomationPeer(Button owner) : base(owner)
		{
		}

#if UNO
#pragma warning disable CS8764 // Uno 6.7 declares this override as non-nullable; WinUI and MAUI's shipped API remain nullable.
#endif
		protected override IList<AutomationPeer>? GetChildrenCore()
		{
#if UNO
			return [];
#else
			return null;
#endif
		}
#if UNO
#pragma warning restore CS8764
#endif

		protected override AutomationPeer? GetLabeledByCore()
		{
			foreach (var item in base.GetChildrenCore())
			{
				if (item is TextBlockAutomationPeer tba)
					return tba;
			}

			return null;
		}
	}
}
