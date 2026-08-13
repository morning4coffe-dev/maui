#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using NSubstitute;
using Xunit;

namespace Microsoft.Maui.UnitTests.Extensions
{
	[Category(TestCategory.Core, TestCategory.Extensions)]
	public class TaskExtensionsTests
	{
#if !DEBUG
		[Fact]
		public async Task FireAndForget_LogsFaultedTaskWithHandlerLogger()
		{
			var loggerProvider = new CapturingLoggerProvider();
			var builder = MauiApp.CreateBuilder();
			builder.Logging.Services.AddSingleton<ILoggerProvider>(loggerProvider);
			var mauiApp = builder.Build();

			var handler = Substitute.For<IViewHandler>();
			var context = Substitute.For<IMauiContext>();
			context.Services.Returns(mauiApp.Services);
			handler.MauiContext.Returns(context);

			loggerProvider.CaptureEnabled = true;

			Task.FromException(new InvalidOperationException("boom")).FireAndForget(handler);

			var completed = await Task.WhenAny(loggerProvider.Logged.Task, Task.Delay(TimeSpan.FromSeconds(5)));

			Assert.Same(loggerProvider.Logged.Task, completed);
			var log = await loggerProvider.Logged.Task;
			Assert.Equal(LogLevel.Error, log.Level);
			Assert.Equal("Microsoft.Maui.IViewHandler", log.CategoryName);
			Assert.IsType<InvalidOperationException>(log.Exception);
			Assert.Contains("Unexpected exception", log.Message, StringComparison.Ordinal);
		}
#endif

		sealed class CapturingLoggerProvider : ILoggerProvider
		{
			public bool CaptureEnabled { get; set; }

			public TaskCompletionSource<CapturedLog> Logged { get; } =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public ILogger CreateLogger(string categoryName) =>
				new CapturingLogger(categoryName, this);

			public void Dispose()
			{
			}

			sealed class CapturingLogger : ILogger
			{
				readonly string _categoryName;
				readonly CapturingLoggerProvider _provider;

				public CapturingLogger(string categoryName, CapturingLoggerProvider provider)
				{
					_categoryName = categoryName;
					_provider = provider;
				}

				public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

				public bool IsEnabled(LogLevel logLevel) => true;

				public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
				{
					if (!_provider.CaptureEnabled)
					{
						return;
					}

					_provider.Logged.TrySetResult(new CapturedLog(
						_categoryName,
						logLevel,
						formatter(state, exception),
						exception));
				}
			}

			sealed class NullScope : IDisposable
			{
				public static readonly NullScope Instance = new();

				public void Dispose()
				{
				}
			}
		}

		sealed record CapturedLog(string CategoryName, LogLevel Level, string Message, Exception? Exception);
	}
}
