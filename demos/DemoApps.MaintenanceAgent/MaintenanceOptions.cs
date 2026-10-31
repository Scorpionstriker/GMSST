namespace DemoApps.MaintenanceAgent;

/// <summary>The "Maintenance" section of appsettings.json.</summary>
public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    /// <summary>
    /// Folders the clean-up may delete files in, including their subfolders. A request can never clean a folder
    /// outside this list. Empty means the GMSS folder inside the operating system's temporary folder.
    /// </summary>
    public List<string> AllowedCleanupRoots { get; set; } = [];

    public IReadOnlyList<string> ResolveAllowedRoots() =>
        (AllowedCleanupRoots.Count == 0 ? [DefaultRoot] : AllowedCleanupRoots)
        .Where(r => !string.IsNullOrWhiteSpace(r))
        .Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(r))))
        .ToList();

    public static string DefaultRoot => Path.Combine(Path.GetTempPath(), "GMSS");
}

/// <summary>The "Agent" section of appsettings.json: where the API listens and the limits of the health check.</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>The environment variable that holds the shared key the scheduler sends in the X-GSST-Key header.</summary>
    public const string KeyVariable = "GSST_AGENT_KEY";

    public const string KeyHeader = "X-GSST-Key";

    /// <summary>Port on 127.0.0.1 and [::1]. The agent never listens on other addresses (Section 1, rule 4).</summary>
    public int Port { get; set; } = 5087;

    /// <summary>The health check fails when less than this many GB are free on the agent's drive.</summary>
    public int MinFreeGb { get; set; } = 2;

    /// <summary>The health check fails when the agent uses more than this many MB of memory.</summary>
    public int MaxMemoryMb { get; set; } = 512;
}

/// <summary>The "Database" section: the agent's own database maintenance_demo. The password comes from GMSS_DB_PASSWORD.</summary>
public sealed class AgentDatabaseOptions
{
    public const string SectionName = "Database";
    public const string PasswordVariable = "GMSS_DB_PASSWORD";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Name { get; set; } = "maintenance_demo";
    public string Username { get; set; } = "gmss_app";
}
