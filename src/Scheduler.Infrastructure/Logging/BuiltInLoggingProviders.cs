using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scheduler.Domain.Entities;
using Scheduler.Extensibility.Contracts;
using Scheduler.Infrastructure.Configuration;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Logging;

/// <summary>Writes log entries to the execution_log table (FR-28).</summary>
internal sealed class DatabaseLoggingProvider(IDbContextFactory<SchedulerDbContext> contextFactory) : ILoggingProvider
{
    private Guid? _providerId;

    public string Name => LogProviderNames.Database;

    public async Task WriteAsync(SchedulerLogEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        _providerId ??= await db.LoggingProviders.Where(p => p.ProviderName == Name).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (_providerId is not { } providerId)
        {
            return;
        }

        db.ExecutionLogs.Add(new ExecutionLogEntry(entry.ExecutionId, providerId, entry.Level, entry.Component,
            LogJson.Serialize(entry, includeEnvelope: false), entry.TimestampUtc));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Writes one JSON object per line to a daily file, gmssYYYYMMDD.log, in the log folder (FR-28).</summary>
internal sealed class FileLoggingProvider(IOptions<PathsOptions> paths) : ILoggingProvider
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public string Name => LogProviderNames.File;

    public async Task WriteAsync(SchedulerLogEntry entry, CancellationToken cancellationToken = default)
    {
        var folder = paths.Value.ResolveLogFolder();
        var file = Path.Combine(folder, $"gmss{entry.TimestampUtc:yyyyMMdd}.log");
        var line = LogJson.Serialize(entry, includeEnvelope: true) + Environment.NewLine;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(folder);
            await File.AppendAllTextAsync(file, line, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }
}

/// <summary>Writes a readable line to standard output; useful for the Maintenance Agent console (FR-28).</summary>
internal sealed class ConsoleLoggingProvider : ILoggingProvider
{
    public string Name => LogProviderNames.Console;

    public Task WriteAsync(SchedulerLogEntry entry, CancellationToken cancellationToken = default)
    {
        Console.Out.WriteLine($"{entry.TimestampUtc:HH:mm:ss} [{entry.Level}] {entry.Component}: {entry.Message}");
        return Task.CompletedTask;
    }
}
