using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common.Persistence;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.IntegrationTests;

/// <summary>WP-7 (F-13): a logging provider added by a module gets its own row at start-up.</summary>
public sealed class LoggingProviderTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [SkippableFact]
    public async Task ModuleProvider_IsRegisteredOnce_ActiveAtInfo()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("custom-provider", configure: s => s.AddSingleton<ILoggingProvider, MemoryProvider>());
        await services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        await services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

        await using var db = database.CreateContext();
        var row = await db.LoggingProviders.SingleAsync(p => p.ProviderName == MemoryProvider.ProviderName);
        Assert.Equal(LoggingProviderType.Custom, row.ProviderType);
        Assert.Equal(SchedulerLogLevel.Info, row.MinimumLevel);
        Assert.True(row.IsActive);
        Assert.Equal(4, await db.LoggingProviders.CountAsync());
    }

    private sealed class MemoryProvider : ILoggingProvider
    {
        public const string ProviderName = "MemoryLoggingProvider";
        public string Name => ProviderName;
        public Task WriteAsync(SchedulerLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
