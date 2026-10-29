using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Scheduler.Application.Configuration;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Caching;

/// <summary>
/// Reads the configuration table through IMemoryCache (FR-39). The whole table is small, so it is loaded at
/// once and kept for <see cref="CacheKeys.Lifetime"/>, or until <see cref="Invalidate"/> is called after a change.
/// </summary>
internal sealed class CachedSettingsProvider(IDbContextFactory<SchedulerDbContext> contextFactory, IMemoryCache cache) : ISettingsProvider
{
    public async Task<int> GetAsync(string key, CancellationToken cancellationToken = default) =>
        await GetIntAsync(key, SettingDefinitions.DefaultOf(key), cancellationToken);

    public async Task<int> GetIntAsync(string key, int defaultValue, CancellationToken cancellationToken = default)
    {
        var values = await LoadAsync(cancellationToken);
        return Parse(key, values.GetValueOrDefault((null, key)), defaultValue);
    }

    public async Task<int> GetForTaskAsync(string key, Guid taskId, CancellationToken cancellationToken = default)
    {
        var values = await LoadAsync(cancellationToken);
        var fallback = Parse(key, values.GetValueOrDefault((null, key)), SettingDefinitions.DefaultOf(key));
        return SettingDefinitions.Find(key) is { AllowTaskOverride: true }
            ? Parse(key, values.GetValueOrDefault((taskId, key)), fallback)
            : fallback;
    }

    public async Task<EngineSettings> GetEngineSettingsAsync(CancellationToken cancellationToken = default) =>
        new(
            await GetAsync(SettingKeys.MaxConcurrency, cancellationToken),
            await GetAsync(SettingKeys.MaxPollIntervalSeconds, cancellationToken),
            await GetAsync(SettingKeys.LeaseGraceSeconds, cancellationToken),
            await GetAsync(SettingKeys.RunAllMisfireCap, cancellationToken),
            await GetAsync(SettingKeys.ShutdownGraceSeconds, cancellationToken),
            await GetAsync(SettingKeys.WorkerStaleSeconds, cancellationToken));

    public void Invalidate()
    {
        cache.Remove(CacheKeys.Settings);
        cache.Remove(CacheKeys.LoggingProviders);
    }

    /// <summary>A stored value outside its allowed range (for example edited directly in the database) falls back to the default.</summary>
    private static int Parse(string key, string? stored, int fallback)
    {
        if (stored is null)
        {
            return fallback;
        }

        var definition = SettingDefinitions.Find(key);
        if (definition is null)
        {
            return int.TryParse(stored, out var value) ? value : fallback;
        }

        return definition.Validate(stored, out var parsed).Length == 0 ? parsed : fallback;
    }

    private async Task<Dictionary<(Guid?, string), string>> LoadAsync(CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync(CacheKeys.Settings, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.Lifetime;
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await db.Configuration.AsNoTracking().Select(c => new { c.TaskId, c.Key, c.Value }).ToListAsync(cancellationToken);
            return rows.ToDictionary(r => ((Guid?)r.TaskId, r.Key), r => r.Value);
        }))!;
}
