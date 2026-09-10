using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Embedding.Uno;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using NativeAutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;
using CollectionView = Microsoft.Maui.Controls.CollectionView;

namespace Maui.Controls.Sample.Uno;

static partial class AccessibilityRegressionProbe
{
	public static async Task<Tier2ProbeResult> RunItemNamesAsync(MauiHost host)
	{
		var previous = host.MauiContent;
		var originalWidth = host.Width;
		var originalHeight = host.Height;
		var report = new StringBuilder();
		var failures = 0;
		Label? secondary = null;
		Label? excluded = null;
		VerticalStackLayout? template = null;
		var collection = new CollectionView
		{
			HeightRequest = 240,
			WidthRequest = 320,
			ItemsSource = new[] { "item" },
			ItemTemplate = new DataTemplate(() =>
			{
				secondary = new Label { Text = "B", IsVisible = false };
				excluded = new Label { Text = "Private" };
				template = new VerticalStackLayout
				{
					Children = { new Label { Text = "A" }, secondary }
				};
				return template;
			})
		};
		try
		{
			host.Width = 320;
			host.Height = 240;
			host.MauiContent = collection;
			if (!await Tier2Probe.WaitForAsync(() => collection.Handler?.PlatformView is ListViewBase { IsLoaded: true } list &&
				list.ContainerFromIndex(0) is SelectorItem))
				throw new InvalidOperationException("The named-item fixture did not attach and realize.");
			await CheckName("initial visible text", "A");
			if (secondary is null || excluded is null || template is null)
				throw new InvalidOperationException("The item template did not realize.");

			secondary.IsVisible = true;
			await CheckName("show existing label", "A, B");
			secondary.IsVisible = false;
			await CheckName("hide existing label", "A");
			var added = new Label { Text = "C" };
			template.Children.Add(added);
			await CheckName("add label after realization", "A, C");
			added.Text = "D";
			await CheckName("edit dynamically added label", "A, D");
			SemanticProperties.SetDescription(added, "Spoken D");
			await CheckName("child semantic description", "A, Spoken D");
			template.Children.Remove(added);
			added.Text = "Detached";
			await CheckName("remove and mutate detached label", "A");

			AutomationProperties.SetIsInAccessibleTree(excluded, false);
			template.Children.Add(excluded);
			await CheckName("exclude visible label from item name", "A");
			AutomationProperties.SetIsInAccessibleTree(excluded, true);
			await CheckName("include existing label", "A, Private");
			template.Children.Remove(excluded);
			await CheckName("remove included label", "A");

			var nested = new VerticalStackLayout { Children = { new Label { Text = "Nested" } } };
			AutomationProperties.SetExcludedWithChildren(nested, true);
			template.Children.Add(nested);
			await CheckName("exclude nested subtree from item name", "A");
			AutomationProperties.SetExcludedWithChildren(nested, false);
			await CheckName("include nested subtree", "A, Nested");
			template.Children.Remove(nested);
			await CheckName("remove nested subtree", "A");

			collection.ItemTemplate = new DataTemplate(() =>
			{
				var privateLabel = new Label { Text = "Private" };
				AutomationProperties.SetIsInAccessibleTree(privateLabel, false);
				return new VerticalStackLayout
				{
					Children = { new Label { Text = "Public" }, privateLabel }
				};
			});
			await CheckName("initial excluded label on template replacement", "Public");
			collection.ItemTemplate = new DataTemplate(() =>
			{
				var privateBranch = new VerticalStackLayout { Children = { new Label { Text = "Private" } } };
				AutomationProperties.SetExcludedWithChildren(privateBranch, true);
				return new VerticalStackLayout
				{
					Children = { new Label { Text = "Public" }, privateBranch }
				};
			});
			await CheckName("initial excluded subtree on template replacement", "Public");
		}
		finally
		{
			host.MauiContent = previous;
			host.Width = originalWidth;
			host.Height = originalHeight;
		}
		return new Tier2ProbeResult(failures == 0, report.ToString());

		async Task CheckName(string scenario, string expected)
		{
			var timer = Stopwatch.StartNew();
			string? actual = null;
			do
			{
				if (collection.Handler?.PlatformView is ListViewBase list &&
					list.ContainerFromIndex(0) is SelectorItem item)
				{
					actual = NativeAutomationProperties.GetName(item);
					if (actual == expected)
						break;
				}
				await Task.Delay(16);
			} while (timer.Elapsed < TimeSpan.FromSeconds(2));

			var passed = actual == expected;
			if (!passed)
				failures++;
			report.AppendLine($"{(passed ? "PASS" : "FAIL")} {scenario}: expected '{expected}', actual '{actual}'");
		}
	}
}
