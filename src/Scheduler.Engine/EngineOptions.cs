namespace Scheduler.Engine;

/// <summary>The "Engine" section of appsettings.json.</summary>
public sealed class EngineOptions
{
    public const string SectionName = "Engine";

    /// <summary>
    /// Name of this host in worker_thread. Each process that runs an engine needs its own name, for example the Admin
    /// application and the Maintenance Agent. Default: machine name and process name.
    /// </summary>
    public string? HostName { get; set; }

    /// <summary>Registers the LogEntry, LocalHttpRequest, DatabaseBackup and LogCleanup task types (FR-07).</summary>
    public bool RegisterBuiltInHandlers { get; set; } = true;

    /// <summary>Creates the built-in Database Backup and Log Clean-up tasks at first start (FR-35, FR-68).</summary>
    public bool CreateBuiltInTasks { get; set; } = true;

    public string ResolveHostName()
    {
        var name = string.IsNullOrWhiteSpace(HostName)
            ? $"{Environment.MachineName}-{AppDomain.CurrentDomain.FriendlyName}"
            : HostName.Trim();
        return name.Length > 100 ? name[..100] : name;
    }
}
