using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Embedding.Uno;
using Microsoft.UI.Xaml;
using Button = Microsoft.Maui.Controls.Button;

namespace Maui.Controls.Sample.Uno;

static partial class AccessibilityRegressionProbe
{
	public static async Task RunModalScopeAsync(MauiEmbeddingSession session, MauiHost host)
	{
		var previous = host.MauiContent;
		var openFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var openSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var closeSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var closeFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var openButton = SignalButton("Open scope modal", openFirst);
		var backgroundContent = new VerticalStackLayout
		{
			Children =
			{
				new Button { Text = "Background probe action" },
				openButton,
				SignalButton("Finish scope probe", finish)
			}
		};
		var background = new ContentPage { Content = backgroundContent };
		var firstModal = new ContentPage
		{
			Content = new VerticalStackLayout
			{
				Children =
				{
					new Button { Text = "First modal only action" },
					SignalButton("Open nested scope modal", openSecond),
					SignalButton("Close first scope modal", closeFirst)
				}
			}
		};
		var secondModal = new ContentPage
		{
			Content = new VerticalStackLayout
			{
				Children =
				{
					new Button { Text = "Second modal only action" },
					SignalButton("Close nested scope modal", closeSecond)
				}
			}
		};
		var backgroundKind = Environment.GetEnvironmentVariable("MAUI_UNO_MODAL_SCOPE_BACKGROUND");
		if (backgroundKind is "transparent" or "opaque")
		{
			var color = backgroundKind == "transparent" ? Microsoft.Maui.Graphics.Colors.Transparent : Microsoft.Maui.Graphics.Colors.White;
			firstModal.BackgroundColor = color;
			secondModal.BackgroundColor = color;
		}

		try
		{
			host.MauiContent = background;
			if (!await Tier2Probe.WaitForAsync(() => openButton.Handler?.PlatformView is FrameworkElement { IsLoaded: true }))
				throw new InvalidOperationException("Modal accessibility fixture did not attach.");
			SetPhase("READY");
			if (Environment.GetEnvironmentVariable("MAUI_UNO_MODAL_SCOPE_AUTO") == "1")
				openFirst.TrySetResult();
			await openFirst.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await background.Navigation.PushModalAsync(firstModal, false);
			backgroundContent.Children.Add(new Button { Text = "Dynamic background probe action" });
			ReportAttachment("FIRST");
			SetPhase("FIRST");

			await openSecond.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await background.Navigation.PushModalAsync(secondModal, false);
			ReportAttachment("SECOND");
			SetPhase("SECOND");
			await closeSecond.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await background.Navigation.PopModalAsync(false);
			ReportAttachment("FIRST-RESTORED");
			SetPhase("FIRST-RESTORED");
			await closeFirst.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await background.Navigation.PopModalAsync(false);
			ReportAttachment("RESTORED");
			SetPhase("RESTORED");
			await finish.Task.WaitAsync(TimeSpan.FromSeconds(30));
		}
		finally
		{
			host.MauiContent = previous;
		}

		void SetPhase(string phase)
		{
			Console.WriteLine($"MODAL-SCOPE-PROBE {phase}");
			session.PlatformWindow.Title = $"MODAL-SCOPE-PROBE {phase}";
		}

		void ReportAttachment(string phase) =>
			Console.WriteLine($"MODAL-SCOPE-ATTACHMENT {phase}: background={background.Handler?.PlatformView is FrameworkElement { IsLoaded: true }}, first={firstModal.Handler?.PlatformView is FrameworkElement { IsLoaded: true }}, second={secondModal.Handler?.PlatformView is FrameworkElement { IsLoaded: true }}");
	}

	static Button SignalButton(string text, TaskCompletionSource signal)
	{
		var button = new Button { Text = text };
		button.Clicked += (_, _) => signal.TrySetResult();
		return button;
	}
}
