namespace DemoApps.MaintenanceAgent;

/// <summary>The "Maintenance" section of appsettings.json.</summary>
public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    /// <summary>
    /// Folders the TempFileCleanup task may delete files in, including their subfolders. A task can never clean a folder
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
