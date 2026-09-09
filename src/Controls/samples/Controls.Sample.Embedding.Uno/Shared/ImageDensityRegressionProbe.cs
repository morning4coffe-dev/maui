using System.Globalization;
using System.Text;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Embedding.Uno;
using Microsoft.UI.Xaml.Media.Imaging;
using NativeImage = Microsoft.UI.Xaml.Controls.Image;
using NativeImageSource = Microsoft.UI.Xaml.Media.ImageSource;

namespace Maui.Controls.Sample.Uno;

internal static class ImageDensityRegressionProbe
{
	internal static bool IsEnabled => Environment.GetEnvironmentVariable("MAUI_UNO_DENSITY_PROBE") == "1";

	internal static async Task<Tier2ProbeResult> RunAsync(MauiHost host)
	{
		var report = new StringBuilder();
		var original = host.MauiContent;
		var source = new DensitySource();
		var image = new Microsoft.Maui.Controls.Image { Source = source, WidthRequest = 32, HeightRequest = 32 };
		try
		{
			host.MauiContent = image;
			await RequireAsync(() => image.Handler?.PlatformView is NativeImage { IsLoaded: true }, "density image attached");
			var native = (NativeImage)image.Handler!.PlatformView!;
			await RequireAsync(() => source.Requests.Count == 1, "initial image load started");
			await SetDensityAsync(native, 1.5);
			source.Requests[0].Complete();
			await RequireAsync(() => source.Requests.Count == 2, "first density retry started");
			Check(source.Requests[1].Scale == 1.5f, "first retry uses density 1.5");
			await SetDensityAsync(native, 2);
			source.Requests[1].Complete();
			await RequireAsync(() => source.Requests.Count == 3, "density change during retry starts a latest-density load");
			Check(source.Requests[2].Scale == 2, "latest retry uses density 2");
			source.Requests[2].Complete();
			await RequireAsync(() => ReferenceEquals(native.Source, source.Requests[2].Result.Value) && !image.IsLoading,
				"final image is the density 2 result");

			var stale = new DensitySource();
			image.Source = stale;
			await RequireAsync(() => stale.Requests.Count == 1, "superseded source load started");
			var replacement = new DensitySource();
			image.Source = replacement;
			await RequireAsync(() => replacement.Requests.Count == 1, "replacement source load started");
			stale.Requests[0].Complete();
			await RequireAsync(() => stale.Requests[0].Result.IsDisposed, "superseded result is disposed");
			Check(!ReferenceEquals(native.Source, stale.Requests[0].Result.Value), "superseded result is never assigned");
			replacement.Requests[0].Complete();
			await RequireAsync(() => ReferenceEquals(native.Source, replacement.Requests[0].Result.Value), "replacement result is assigned");

			var failed = new DensitySource();
			image.Source = failed;
			await RequireAsync(() => failed.Requests.Count == 1, "superseded failing load started");
			var latest = new DensitySource();
			image.Source = latest;
			await RequireAsync(() => latest.Requests.Count == 1, "latest source load started");
			latest.Requests[0].Complete();
			await RequireAsync(() => ReferenceEquals(native.Source, latest.Requests[0].Result.Value), "latest result is assigned");
			failed.Requests[0].Fail();
			await Task.Yield();
			Check(ReferenceEquals(native.Source, latest.Requests[0].Result.Value), "late failure cannot clear the latest image");

			var disconnected = new DensitySource();
			image.Source = disconnected;
			await RequireAsync(() => disconnected.Requests.Count == 1, "disconnect load started");
			host.MauiContent = null;
			disconnected.Requests[0].Complete();
			await RequireAsync(() => disconnected.Requests[0].Result.IsDisposed, "result completed after disconnect is disposed");
			Check(!ReferenceEquals(native.Source, disconnected.Requests[0].Result.Value), "disconnected result is never assigned");
			return new Tier2ProbeResult(true, report.ToString());
		}
		catch (Exception error)
		{
			report.AppendLine($"FAIL density regression: {error}");
			return new Tier2ProbeResult(false, report.ToString());
		}
		finally
		{
			host.MauiContent = original;
		}

		void Check(bool passed, string name)
		{
			if (!passed)
				throw new InvalidOperationException(name);
			report.AppendLine($"PASS {name}");
		}

		async Task RequireAsync(Func<bool> condition, string name)
			=> Check(await Tier2Probe.WaitForAsync(condition), name);

		async Task SetDensityAsync(NativeImage native, double density)
		{
			// The browser driver changes real device metrics in response to this marker.
			// The production loader still reads the real XamlRoot rather than a test override.
			Console.WriteLine($"DENSITY-REQUEST {density.ToString(CultureInfo.InvariantCulture)}");
			await RequireAsync(() => native.XamlRoot?.RasterizationScale == density, $"XamlRoot density changed to {density}");
		}
	}

	internal sealed class DensitySource : Microsoft.Maui.Controls.ImageSource
	{
		internal List<Request> Requests { get; } = new();
	}

	internal sealed class DensityService : IImageSourceService<DensitySource>
	{
		public Task<IImageSourceServiceResult<NativeImageSource>?> GetImageSourceAsync(
			IImageSource imageSource, float scale = 1, CancellationToken cancellationToken = default)
		{
			var request = new Request(scale);
			((DensitySource)imageSource).Requests.Add(request);
			// Deliberately complete after cancellation too: third-party services may do so.
			return request.Completion.Task;
		}
	}

	internal sealed class Request
	{
		internal Request(float scale)
		{
			Scale = scale;
			Result = new ImageSourceServiceResult(new WriteableBitmap((int)(16 * scale), (int)(16 * scale)), true);
		}

		internal float Scale { get; }
		internal ImageSourceServiceResult Result { get; }
		internal TaskCompletionSource<IImageSourceServiceResult<NativeImageSource>?> Completion { get; } = new();
		internal void Complete() => Completion.SetResult(Result);
		internal void Fail()
		{
			Result.Dispose();
			Completion.SetException(new InvalidOperationException("Expected superseded image failure."));
		}
	}
}
