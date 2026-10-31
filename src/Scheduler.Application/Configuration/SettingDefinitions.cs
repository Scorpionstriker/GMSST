using System.Globalization;

namespace Scheduler.Application.Configuration;

/// <summary>Names of the settings in the configuration table (Phase 4, Table 5.16).</summary>
public static class SettingKeys
{
    public const string MaxConcurrency = nameof(MaxConcurrency);
    public const string MaxPollIntervalSeconds = nameof(MaxPollIntervalSeconds);
    public const string MaxRetryLimit = nameof(MaxRetryLimit);
    public const string RetryBaseDelaySeconds = nameof(RetryBaseDelaySeconds);
    public const string RetryMaxDelaySeconds = nameof(RetryMaxDelaySeconds);
    public const string LeaseGraceSeconds = nameof(LeaseGraceSeconds);
    public const string LogRetentionDays = nameof(LogRetentionDays);
    public const string AuditRetentionDays = nameof(AuditRetentionDays);
    public const string BackupRetentionCount = nameof(BackupRetentionCount);
    public const string SessionTimeoutMinutes = nameof(SessionTimeoutMinutes);
    public const string MaxFailedLogins = nameof(MaxFailedLogins);
    public const string LockoutMinutes = nameof(LockoutMinutes);
    public const string RunAllMisfireCap = nameof(RunAllMisfireCap);
    public const string ShutdownGraceSeconds = nameof(ShutdownGraceSeconds);
    public const string WorkerStaleSeconds = nameof(WorkerStaleSeconds);
}

/// <summary>
/// A known setting: its type, allowed range, default and whether a task may override it. Every value is
/// checked against its definition before it is stored (FR-37).
/// </summary>
public sealed record SettingDefinition(
    string Key,
    string Description,
    int DefaultValue,
    int Min,
    int Max,
    string Unit,
    bool AllowTaskOverride = false,
    bool AppliesOnRestart = false)
{
    public string Validate(string? value, out int parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return $"{Key} must be a whole number.";
        }

        return parsed < Min || parsed > Max ? $"{Key} must be between {Min} and {Max}." : string.Empty;
    }
}

/// <summary>Every setting the scheduler understands, with the defaults seeded at first start (FR-38).</summary>
public static class SettingDefinitions
{
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(SettingKeys.MaxConcurrency, "Maximum number of tasks the scheduler engine runs at the same time (its worker limit).", 4, 1, 16, "tasks", AppliesOnRestart: true),
        new(SettingKeys.MaxPollIntervalSeconds, "Longest time the engine sleeps between checks for due work.", 30, 5, 300, "seconds"),
        new(SettingKeys.MaxRetryLimit, "Retries after a failed or timed-out run, unless the task sets its own.", 3, 0, 10, "retries", AllowTaskOverride: true),
        new(SettingKeys.RetryBaseDelaySeconds, "Delay before the first retry; it doubles for each further retry.", 30, 1, 3600, "seconds", AllowTaskOverride: true),
        new(SettingKeys.RetryMaxDelaySeconds, "Longest delay between retries.", 900, 1, 86400, "seconds", AllowTaskOverride: true),
        new(SettingKeys.LeaseGraceSeconds, "Extra time added to a task's time-out before its claim is treated as abandoned.", 60, 10, 3600, "seconds"),
        new(SettingKeys.LogRetentionDays, "Executions, errors, log entries and log files older than this are removed by the Log Clean-up task.", 90, 1, 3650, "days"),
        new(SettingKeys.AuditRetentionDays, "Sign-in attempts and user management audit records older than this are removed by the Log Clean-up task.", 365, 30, 3650, "days"),
        new(SettingKeys.BackupRetentionCount, "Number of newest backups kept; older backup files are deleted.", 14, 1, 365, "backups"),
        new(SettingKeys.SessionTimeoutMinutes, "Minutes of inactivity before a signed-in user is signed out.", 20, 1, 480, "minutes"),
        new(SettingKeys.MaxFailedLogins, "Failed sign-in attempts in a row before an account is locked.", 5, 1, 20, "attempts"),
        new(SettingKeys.LockoutMinutes, "How long a locked account stays locked.", 15, 1, 1440, "minutes"),
        new(SettingKeys.RunAllMisfireCap, "Most missed runs replayed by a schedule whose misfire policy is Run all.", 10, 1, 1000, "runs"),
        new(SettingKeys.ShutdownGraceSeconds, "Time running tasks are given to finish when the engine stops.", 30, 0, 600, "seconds"),
        new(SettingKeys.WorkerStaleSeconds, "A worker without a heartbeat for this long is marked Stopped.", 120, 30, 3600, "seconds")
    ];

    private static readonly Dictionary<string, SettingDefinition> ByKey = All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static SettingDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

    public static int DefaultOf(string key) => ByKey.TryGetValue(key, out var definition) ? definition.DefaultValue : 0;
}
