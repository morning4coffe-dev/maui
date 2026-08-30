using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Maui.Controls.Sample.Uno;

/// <summary>
/// Reports which of the gallery's controls genuinely made it onto the platform.
/// </summary>
/// <remarks>
/// <para>
/// Construction succeeding proves nothing: a MAUI element with no working handler still builds, still sits
/// in the logical tree, and still reports its requested size. Each control is therefore checked for a
/// handler, a realized Uno <see cref="FrameworkElement"/>, and a non-zero arranged size.
/// </para>
/// <para>
/// Size alone is not enough either, so controls registered with <c>expectsItems</c> must additionally have
/// realized visible text somewhere beneath their platform view, or they are reported as EMPTY.
/// </para>
/// <para>
/// <b>This census cannot tell whether anything was painted.</b> That is not a detail: on WebAssembly the
/// items of <c>CollectionView</c>, <c>CarouselView</c> and <c>RefreshView</c> are realized and arranged
/// with correct sizes, and still draw nothing at all. Everything here is therefore reported as
/// <c>REALIZED</c> rather than rendered, and painting is only ever confirmed from a screenshot.
/// </para>
/// </remarks>
public static class ControlCensus
{
	public static async Task<string> RunAsync(AdvancedMauiContent gallery)
	{
		ArgumentNullException.ThrowIfNull(gallery);

		// Templated and virtualizing controls realize their items over several frames.
		await Task.Delay(2500);

		var report = new StringBuilder();
		var ok = 0;
		var total = 0;

		foreach (var (name, element, expectsItems) in gallery.ShowcasedControls)
		{
			total++;

			var platformView = element.Handler?.PlatformView as FrameworkElement;
			var width = platformView?.ActualWidth ?? 0;
			var height = platformView?.ActualHeight ?? 0;
			var sized = platformView is not null && width > 0 && height > 0;

			var descendants = 0;
			var texts = 0;
			var arranged = 0;

			if (platformView is not null)
			{
				Inspect(platformView, 0, ref descendants, ref texts, ref arranged);
			}

			var passed = sized && (!expectsItems || texts > 0);
			var verdict = !sized ? "MISSING " : passed ? "REALIZED" : "EMPTY   ";

			if (passed)
			{
				ok++;
			}

			report.AppendLine(string.Format(
				CultureInfo.InvariantCulture,
				"{0} {1,-28} platform={2,-22} size={3:0}x{4:0} children={5,-4} texts={6,-3} arranged={7}",
				verdict,
				name,
				platformView?.GetType().Name ?? "none",
				width,
				height,
				descendants,
				texts,
				arranged));
		}

		report.Insert(0, $"CENSUS-RESULT {ok}/{total} realized, handlers={MauiProgram.HandlerMode} (painting is not measurable here){Environment.NewLine}");

		return report.ToString();
	}

	/// <summary>
	/// Counts realized descendants, how many show text, and how many were actually given a size.
	/// </summary>
	/// <remarks>
	/// The arranged count is what separates the two failure modes: subtrees that exist but were never laid
	/// out, from subtrees that were laid out correctly and simply never painted.
	/// </remarks>
	static void Inspect(DependencyObject node, int depth, ref int descendants, ref int texts, ref int arranged)
	{
		if (depth > 30)
		{
			return;
		}

		var count = VisualTreeHelper.GetChildrenCount(node);

		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(node, i);
			descendants++;

			if (child is TextBlock block && !string.IsNullOrEmpty(block.Text))
			{
				texts++;
			}

			if (child is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 })
			{
				arranged++;
			}

			Inspect(child, depth + 1, ref descendants, ref texts, ref arranged);
		}
	}

	/// <summary>Writes the census where both a desktop run and a headless browser run can collect it.</summary>
	/// <remarks>
	/// A desktop GUI host does not surface <see cref="Console"/> output, and the browser has no writable
	/// file system, so the census goes to both.
	/// </remarks>
	public static void Publish(string report)
	{
		foreach (var line in report.Split('\n'))
		{
			var trimmed = line.TrimEnd('\r');

			if (!string.IsNullOrEmpty(trimmed))
			{
				Console.WriteLine(trimmed);
			}
		}

		try
		{
			System.IO.File.WriteAllText(LogPath, report);
		}
		catch (Exception)
		{
			// Diagnostics only.
		}
	}

	public static string LogPath =>
		System.IO.Path.Combine(System.IO.Path.GetTempPath(), "control-census.log");
}
