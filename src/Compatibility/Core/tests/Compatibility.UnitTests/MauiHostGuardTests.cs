using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Embedding;
using Microsoft.Maui.Hosting;
using Xunit;

namespace Microsoft.Maui.Compatibility.Core.UnitTests;

public class MauiHostGuardTests
{
	[Fact]
	public void StandaloneHost_canBuild_withoutEmbedding()
	{
		var platformApplication = new PlatformApplicationStub();
#if PLATFORM || WINDOWS
		var previousPlatformApplication = IPlatformApplication.Current;

		try
		{
#endif
			using var app = MauiHostGuard.CreateStandaloneApp(
				platformApplication,
				() =>
				{
#if PLATFORM || WINDOWS
					Assert.Same(platformApplication, IPlatformApplication.Current);
#endif
					return MauiApp.CreateBuilder(useDefaults: false).Build();
				});

#if PLATFORM || WINDOWS
			Assert.Same(platformApplication, IPlatformApplication.Current);
		}
		finally
		{
			IPlatformApplication.Current = previousPlatformApplication;
		}
#endif
	}

	[Fact]
	public void StandaloneHost_rejects_embedding_before_initializers_run()
	{
		var initializer = new TrackingInitializer();
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.Services.AddSingleton<IMauiInitializeService>(initializer);
		builder.UseMauiEmbedding<ApplicationStub>();
#if PLATFORM || WINDOWS
		var previousPlatformApplication = IPlatformApplication.Current;
#endif
		var platformApplication = new PlatformApplicationStub();

#if PLATFORM || WINDOWS
		try
		{
#endif
			var exception = Assert.Throws<InvalidOperationException>(() =>
				MauiHostGuard.CreateStandaloneApp(platformApplication, builder.Build));

			Assert.Equal(MauiHostGuard.StandaloneEmbeddingConflictMessage, exception.Message);
#if PLATFORM || WINDOWS
			Assert.Same(previousPlatformApplication, IPlatformApplication.Current);
		}
		finally
		{
			IPlatformApplication.Current = previousPlatformApplication;
		}
#endif

		Assert.False(initializer.Initialized);
	}

	[Fact]
	public void StandaloneHost_rolls_back_current_when_creation_fails()
	{
		var platformApplication = new PlatformApplicationStub();
#if PLATFORM || WINDOWS
		var previousPlatformApplication = IPlatformApplication.Current;
		var expectedException = new InvalidOperationException("creation failed");

		try
		{
#else
		var expectedException = new InvalidOperationException("creation failed");
#endif
			var exception = Assert.Throws<InvalidOperationException>(() =>
				MauiHostGuard.CreateStandaloneApp(
					platformApplication,
					() =>
					{
#if PLATFORM || WINDOWS
						Assert.Same(platformApplication, IPlatformApplication.Current);
#endif
						throw expectedException;
					}));

			Assert.Same(expectedException, exception);
#if PLATFORM || WINDOWS
			Assert.Same(previousPlatformApplication, IPlatformApplication.Current);
		}
		finally
		{
			IPlatformApplication.Current = previousPlatformApplication;
		}
#endif
	}

	[Fact]
	public void EmbeddingEntryPoint_rejects_registration_during_standalone_creation()
	{
		using (MauiHostGuard.EnterStandaloneHostCreation())
		{
			var exception = Assert.Throws<InvalidOperationException>(() =>
				MauiApp.CreateBuilder(useDefaults: false).UseMauiEmbedding<ApplicationStub>());

			Assert.Equal(MauiHostGuard.StandaloneEmbeddingConflictMessage, exception.Message);
		}
	}

	[Fact]
	public void EmbeddingHost_canBuild_outside_standalone_creation()
	{
		using var app = MauiApp.CreateBuilder(useDefaults: false)
			.UseMauiEmbedding<ApplicationStub>()
			.Build();
	}

	sealed class TrackingInitializer : IMauiInitializeService
	{
		public bool Initialized { get; private set; }

		public void Initialize(IServiceProvider services) => Initialized = true;
	}

	sealed class PlatformApplicationStub : IPlatformApplication
	{
		public IServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();

		public IApplication Application { get; } = new ApplicationStub();
	}
}
