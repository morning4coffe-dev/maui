#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
#if ANDROID
using Microsoft.Maui.TestUtils.DeviceTests.Runners.HeadlessRunner;
#endif

namespace Microsoft.Maui.DeviceTests
{
	public sealed class DevicePerformanceResult
	{
		public const int CurrentSchemaVersion = 1;

		public int SchemaVersion { get; init; } = CurrentSchemaVersion;
		public string Scenario { get; init; } = string.Empty;
		public string Platform { get; init; } = DevicePerformanceEnvironment.Platform;
		public string Variant { get; init; } = DevicePerformanceEnvironment.GetValue("MAUI_PERF_VARIANT") ?? "unknown";
		public string CommitSha { get; init; } = DevicePerformanceEnvironment.GetValue("MAUI_PERF_COMMIT_SHA") ?? "unknown";
		public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
		public int WarmupCount { get; init; }
		public double[] MeasurementsMilliseconds { get; init; } = [];
		public DevicePerformanceStatistics Statistics { get; init; } = new();
		public Dictionary<string, double> Counters { get; init; } =
			new Dictionary<string, double>(StringComparer.Ordinal);
	}

	public sealed class DevicePerformanceStatistics
	{
		public double MinimumMilliseconds { get; init; }
		public double MaximumMilliseconds { get; init; }
		public double MedianMilliseconds { get; init; }
		public double P95Milliseconds { get; init; }
		public double MeanMilliseconds { get; init; }
	}

	public static class DevicePerformanceMeasurement
	{
		public static async Task<DevicePerformanceResult> MeasureAsync(
			string scenario,
			int warmupCount,
			int iterationCount,
			Func<int, Task> operation,
			IReadOnlyDictionary<string, double>? counters = null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(scenario);
			ArgumentNullException.ThrowIfNull(operation);

			if (warmupCount < 0)
				throw new ArgumentOutOfRangeException(nameof(warmupCount));

			if (iterationCount <= 0)
				throw new ArgumentOutOfRangeException(nameof(iterationCount));

			for (int i = 0; i < warmupCount; i++)
				await operation(i).ConfigureAwait(false);

			var measurements = new double[iterationCount];
			for (int i = 0; i < iterationCount; i++)
			{
				long start = Stopwatch.GetTimestamp();
				await operation(i).ConfigureAwait(false);
				measurements[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
			}

			return new DevicePerformanceResult
			{
				Scenario = scenario,
				WarmupCount = warmupCount,
				MeasurementsMilliseconds = measurements,
				Statistics = CalculateStatistics(measurements),
				Counters = counters?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
					?? new Dictionary<string, double>(StringComparer.Ordinal)
			};
		}

		public static DevicePerformanceStatistics CalculateStatistics(IReadOnlyList<double> measurements)
		{
			ArgumentNullException.ThrowIfNull(measurements);

			if (measurements.Count == 0)
				throw new ArgumentException("At least one measurement is required.", nameof(measurements));

			double[] sorted = measurements.OrderBy(value => value).ToArray();
			int middle = sorted.Length / 2;
			double median = sorted.Length % 2 == 0
				? (sorted[middle - 1] + sorted[middle]) / 2
				: sorted[middle];
			int p95Index = Math.Max(0, (int)Math.Ceiling(sorted.Length * 0.95) - 1);

			return new DevicePerformanceStatistics
			{
				MinimumMilliseconds = sorted[0],
				MaximumMilliseconds = sorted[^1],
				MedianMilliseconds = median,
				P95Milliseconds = sorted[p95Index],
				MeanMilliseconds = sorted.Average()
			};
		}
	}

	public static class DevicePerformanceReporter
	{
		public const string ResultPrefix = "MAUI_PERF_RESULT:";

		public static void Write(DevicePerformanceResult result)
		{
			ArgumentNullException.ThrowIfNull(result);

			if (result.SchemaVersion != DevicePerformanceResult.CurrentSchemaVersion)
				throw new ArgumentException($"Unsupported schema version: {result.SchemaVersion}", nameof(result));

			Console.WriteLine(
				$"{ResultPrefix}{JsonSerializer.Serialize(result, DevicePerformanceJsonContext.Default.DevicePerformanceResult)}");
		}
	}

	[JsonSourceGenerationOptions(
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
			WriteIndented = false)]
	[JsonSerializable(typeof(DevicePerformanceResult))]
	[JsonSerializable(typeof(DevicePerformanceStatistics))]
	[JsonSerializable(typeof(Dictionary<string, double>))]
	partial class DevicePerformanceJsonContext : JsonSerializerContext
	{
	}

	static class DevicePerformanceEnvironment
	{
		public static string Platform
		{
			get
			{
#if ANDROID
				return "android";
#elif IOS
				return "ios";
#elif MACCATALYST
				return "maccatalyst";
#elif WINDOWS
				return "windows";
#else
				return "unknown";
#endif
			}
		}

		public static string? GetValue(string name)
		{
#if ANDROID
			string? instrumentationValue = MauiTestInstrumentation.Current?.Arguments?.GetString(name);
			if (!string.IsNullOrWhiteSpace(instrumentationValue))
				return instrumentationValue;
#endif

			return Environment.GetEnvironmentVariable(name);
		}
	}
}
