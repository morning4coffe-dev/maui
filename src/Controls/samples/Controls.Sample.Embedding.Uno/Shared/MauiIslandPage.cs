using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Maui.Controls.Sample.Uno;

/// <summary>
/// A window-level MAUI island: a real <see cref="ContentPage"/> promoted to the embedded window's
/// <c>Window.Page</c>, which is what enables window-scoped MAUI services.
/// </summary>
public sealed class MauiIslandPage : ContentPage
{
	readonly Label _result;

	public MauiIslandPage(string title)
	{
		Title = title;

		var heading = new Label { Text = title };
		heading.SetDynamicResource(VisualElement.StyleProperty, "EmbeddedHeadline");

		_result = new Label { Text = "Tier 2 result: (nothing yet)" };

		var alertButton = new Button { Text = "DisplayAlert" };
		alertButton.Clicked += OnAlertClicked;

		var confirmButton = new Button { Text = "Confirm (2 buttons)" };
		confirmButton.Clicked += OnConfirmClicked;

		var actionSheetButton = new Button { Text = "DisplayActionSheet" };
		actionSheetButton.Clicked += OnActionSheetClicked;

		var promptButton = new Button { Text = "DisplayPromptAsync" };
		promptButton.Clicked += OnPromptClicked;

		var modalButton = new Button { Text = "PushModalAsync" };
		modalButton.Clicked += OnModalClicked;

		var pushButton = new Button { Text = "PushAsync (stack navigation)" };
		pushButton.Clicked += OnPushClicked;

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Padding = new Thickness(16),
				Children =
				{
					heading,
					new Label { Text = "This island is the embedded window's Page, so window-scoped MAUI services apply." },
					alertButton,
					confirmButton,
					actionSheetButton,
					promptButton,
					modalButton,
					pushButton,
					_result,
				},
			},
		};
	}

	void OnAlertClicked(object? sender, EventArgs args) =>
		RunAsync(async () =>
		{
			await DisplayAlertAsync("Alert from embedded MAUI", "This dialog is hosted by the Uno window.", "OK");
			return "DisplayAlert dismissed";
		});

	void OnConfirmClicked(object? sender, EventArgs args) =>
		RunAsync(async () =>
		{
			var accepted = await DisplayAlertAsync("Confirm", "Did the embedded alert work?", "Yes", "No");
			return $"Confirm returned {accepted}";
		});

	void OnActionSheetClicked(object? sender, EventArgs args) =>
		RunAsync(async () =>
		{
			var choice = await DisplayActionSheetAsync("Pick one", "Cancel", null, "First", "Second", "Third");
			return $"Action sheet returned {choice ?? "(null)"}";
		});

	void OnPromptClicked(object? sender, EventArgs args) =>
		RunAsync(async () =>
		{
			var value = await DisplayPromptAsync("Prompt", "Type something", initialValue: "hello");
			return $"Prompt returned {value ?? "(cancelled)"}";
		});

	void OnModalClicked(object? sender, EventArgs args) =>
		RunAsync(async () =>
		{
			var closeButton = new Button { Text = "Close modal" };
			var modal = new ContentPage
			{
				Title = "Modal page",
				BackgroundColor = Color.FromArgb("#EFE7FF"),
				Content = new VerticalStackLayout
				{
					Spacing = 12,
					Padding = new Thickness(24),
					Children =
					{
						new Label { Text = "This is a modal page pushed from embedded MAUI." },
						closeButton,
					},
				},
			};

			var closed = new TaskCompletionSource();
			closeButton.Clicked += (_, _) => closed.TrySetResult();

			await Navigation.PushModalAsync(modal);
			await closed.Task;
			await Navigation.PopModalAsync();

			return "Modal pushed and popped";
		});

	void OnPushClicked(object? sender, EventArgs args) =>
		RunAsync(async () =>
		{
			if (Navigation.NavigationStack.Count == 0)
			{
				return "PushAsync needs a NavigationPage host";
			}

			var backButton = new Button { Text = "Go back" };
			var pushed = new ContentPage
			{
				Title = "Pushed page",
				Content = new VerticalStackLayout
				{
					Spacing = 12,
					Padding = new Thickness(24),
					Children =
					{
						new Label { Text = "This page was pushed onto the embedded navigation stack." },
						backButton,
					},
				},
			};

			var popped = new TaskCompletionSource();
			backButton.Clicked += (_, _) => popped.TrySetResult();

			await Navigation.PushAsync(pushed);
			await popped.Task;
			await Navigation.PopAsync();

			return "Pushed and popped a page";
		});

	// Button.Clicked is a synchronous event; awaiting inside it without this wrapper would be async void
	// with no exception handling, which would tear the app down on failure.
	void RunAsync(Func<Task<string>> operation) => _ = RunAsyncCore(operation);

	async Task RunAsyncCore(Func<Task<string>> operation)
	{
		try
		{
			_result.Text = $"Tier 2 result: {await operation()}";
		}
		catch (Exception ex)
		{
			_result.Text = $"Tier 2 result: FAILED - {ex.GetType().Name}: {ex.Message}";
		}
	}
}
