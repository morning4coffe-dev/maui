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
		var previousPlatformApplication = IPlatformApplication.Current;
		var platformApplication = new PlatformApplicationStub();

		try
		{
			using var app = MauiHostGuard.CreateStandaloneApp(
				platformApplication,
				() =>
				{
					Assert.Same(platformApplication, IPlatformApplication.Current);
					return MauiApp.CreateBuilder(useDefaults: false).Build();
				});

			Assert.Same(platformApplication, IPlatformApplication.Current);
		}
		finally
		{
			IPlatformApplication.Current = previousPlatformApplication;
		}
	}

	[Fact]
	public void StandaloneHost_rejects_embedding_before_initializers_run()
	{
		var initializer = new TrackingInitializer();
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.Services.AddSingleton<IMauiInitializeService>(initializer);
		builder.UseMauiEmbedding<ApplicationStub>();
		var previousPlatformApplication = IPlatformApplication.Current;
		var platformApplication = new PlatformApplicationStub();

		try
		{
			var exception = Assert.Throws<InvalidOperationException>(() =>
				MauiHostGuard.CreateStandaloneApp(platformApplication, builder.Build));

			Assert.Equal(MauiHostGuard.StandaloneEmbeddingConflictMessage, exception.Message);
			Assert.Same(previousPlatformApplication, IPlatformApplication.Current);
		}
		finally
		{
			IPlatformApplication.Current = previousPlatformApplication;
		}

		Assert.False(initializer.Initialized);
	}

	[Fact]
	public void StandaloneHost_rolls_back_current_when_creation_fails()
	{
		var previousPlatformApplication = IPlatformApplication.Current;
		var platformApplication = new PlatformApplicationStub();
		var expectedException = new InvalidOperationException("creation failed");

		try
		{
			var exception = Assert.Throws<InvalidOperationException>(() =>
				MauiHostGuard.CreateStandaloneApp(
					platformApplication,
					() =>
					{
						Assert.Same(platformApplication, IPlatformApplication.Current);
						throw expectedException;
					}));

			Assert.Same(expectedException, exception);
			Assert.Same(previousPlatformApplication, IPlatformApplication.Current);
		}
		finally
		{
			IPlatformApplication.Current = previousPlatformApplication;
		}
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
