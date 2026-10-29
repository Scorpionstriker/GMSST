using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Configuration;

/// <summary>
/// Cached access to the configuration table (FR-36, FR-39). Implemented in the infrastructure layer
/// with IMemoryCache; <see cref="Invalidate"/> is called after a change and after a restore.
/// </summary>
public interface ISettingsProvider : ISettingsReader
{
    /// <summary>The global value of a known setting, or its default.</summary>
    Task<int> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>The task's own value of a setting when it has one, otherwise the global value.</summary>
    Task<int> GetForTaskAsync(string key, Guid taskId, CancellationToken cancellationToken = default);

    Task<EngineSettings> GetEngineSettingsAsync(CancellationToken cancellationToken = default);

    void Invalidate();
}

/// <summary>A snapshot of the settings the engine reads every cycle.</summary>
public sealed record EngineSettings(
    int MaxConcurrency,
    int MaxPollIntervalSeconds,
    int LeaseGraceSeconds,
    int RunAllMisfireCap,
    int ShutdownGraceSeconds,
    int WorkerStaleSeconds)
{
    public static EngineSettings Defaults { get; } = new(
        SettingDefinitions.DefaultOf(SettingKeys.MaxConcurrency),
        SettingDefinitions.DefaultOf(SettingKeys.MaxPollIntervalSeconds),
        SettingDefinitions.DefaultOf(SettingKeys.LeaseGraceSeconds),
        SettingDefinitions.DefaultOf(SettingKeys.RunAllMisfireCap),
        SettingDefinitions.DefaultOf(SettingKeys.ShutdownGraceSeconds),
        SettingDefinitions.DefaultOf(SettingKeys.WorkerStaleSeconds));
}
