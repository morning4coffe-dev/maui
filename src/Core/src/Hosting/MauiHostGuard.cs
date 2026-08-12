using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Maui.Hosting;

internal static class MauiHostGuard
{
	internal const string StandaloneEmbeddingConflictMessage =
		"Standalone MauiWinUIApplication hosting cannot be combined with MAUI or Uno embedding. Choose either the standalone MauiWinUIApplication root or an embedding host.";

	static readonly AsyncLocal<bool> standaloneHostCreation = new();

	internal static IDisposable EnterStandaloneHostCreation()
	{
		var previousValue = standaloneHostCreation.Value;
		standaloneHostCreation.Value = true;
		return new StandaloneHostCreationScope(previousValue);
	}

	internal static MauiApp CreateStandaloneApp(IPlatformApplication platformApplication, Func<MauiApp> createMauiApp)
	{
		if (platformApplication is null)
			throw new ArgumentNullException(nameof(platformApplication));
		if (createMauiApp is null)
			throw new ArgumentNullException(nameof(createMauiApp));

		var previousPlatformApplication = IPlatformApplication.Current;
		using (EnterStandaloneHostCreation())
		{
			IPlatformApplication.Current = platformApplication;
			MauiApp? mauiApp = null;

			try
			{
				mauiApp = createMauiApp();
				ThrowIfEmbeddingConfigured(mauiApp.Services);
				return mauiApp;
			}
			catch
			{
				try
				{
					mauiApp?.Dispose();
				}
				finally
				{
					IPlatformApplication.Current = previousPlatformApplication;
				}

				throw;
			}
		}
	}

	internal static void MarkEmbedding(MauiAppBuilder builder)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));

		ThrowIfEmbeddingIsActive();
		builder.Services.TryAddSingleton<MauiEmbeddingHostMarker>();
	}

	internal static void ThrowIfEmbeddingConfigured(IServiceCollection services)
	{
		if (!standaloneHostCreation.Value)
			return;

		foreach (var descriptor in services)
		{
			if (descriptor.ServiceType == typeof(MauiEmbeddingHostMarker))
			{
				throw new InvalidOperationException(StandaloneEmbeddingConflictMessage);
			}
		}
	}

	internal static void ThrowIfEmbeddingConfigured(IServiceProvider services)
	{
		if (standaloneHostCreation.Value &&
			services.GetService<MauiEmbeddingHostMarker>() is not null)
		{
			throw new InvalidOperationException(StandaloneEmbeddingConflictMessage);
		}
	}

	static void ThrowIfEmbeddingIsActive()
	{
		if (standaloneHostCreation.Value)
			throw new InvalidOperationException(StandaloneEmbeddingConflictMessage);
	}

	sealed class StandaloneHostCreationScope(bool previousValue) : IDisposable
	{
		public void Dispose() => standaloneHostCreation.Value = previousValue;
	}
}

internal sealed class MauiEmbeddingHostMarker
{
}
