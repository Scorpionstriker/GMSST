using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Scheduler.Application.Common;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;
using Scheduler.Infrastructure.Caching;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Logging;

/// <summary>
/// Sends each entry to every registered provider that is active and whose minimum level allows it (FR-29).
/// Provider settings come from logging_provider, cached. A provider a module adds without a row is treated as
/// active at Info. A failing provider is reported to the .NET logger and never stops the caller.
/// </summary>
internal sealed class SchedulerLogWriter(
    IEnumerable<ILoggingProvider> providers,
    IDbContextFactory<SchedulerDbContext> contextFactory,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<SchedulerLogWriter> logger) : ISchedulerLogger
{
    private readonly IReadOnlyList<ILoggingProvider> _providers = providers.ToList();

    public async Task LogAsync(SchedulerLogLevel level, string source, string message, Guid? executionId = null,
        IReadOnlyDictionary<string, object?>? properties = null, CancellationToken cancellationToken = default)
    {
        var entry = new SchedulerLogEntry(clock.GetUtcNow().UtcDateTime, level, source, message, executionId, properties);
        var settings = await LoadSettingsAsync();

        foreach (var provider in _providers)
        {
            var (active, minimum) = settings.TryGetValue(provider.Name, out var row) ? row : (true, SchedulerLogLevel.Info);
            if (!active || level < minimum)
            {
                continue;
            }

            try
            {
                await provider.WriteAsync(entry, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Logging provider {Provider} failed to write an entry.", provider.Name);
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, (bool Active, SchedulerLogLevel Minimum)>> LoadSettingsAsync()
    {
        try
        {
            return (await cache.GetOrCreateAsync(CacheKeys.LoggingProviders, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheKeys.Lifetime;
                await using var db = await contextFactory.CreateDbContextAsync();
                var rows = await db.LoggingProviders.AsNoTracking().ToListAsync();
                return (IReadOnlyDictionary<string, (bool, SchedulerLogLevel)>)rows.ToDictionary(
                    r => r.ProviderName, r => (r.IsActive, r.MinimumLevel), StringComparer.OrdinalIgnoreCase);
            }))!;
        }
        catch (Exception ex)
        {
            // Database unreachable: keep logging to the file and console providers.
            logger.LogDebug(ex, "Logging provider settings could not be read.");
            return new Dictionary<string, (bool, SchedulerLogLevel)>
            {
                [LogProviderNames.Database] = (false, SchedulerLogLevel.Info)
            };
        }
    }
}
