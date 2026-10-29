namespace Scheduler.Infrastructure.Configuration;

/// <summary>
/// The "Paths" section of appsettings.json: where log files and backups are written, and where the
/// PostgreSQL client tools are. Empty values use the defaults of the operating system.
/// </summary>
public sealed class PathsOptions
{
    public const string SectionName = "Paths";

    /// <summary>Folder for backups and manifest.json. Default: C:\GMSS\Backups on Windows, ~/Library/Application Support/GMSS/Backups on macOS.</summary>
    public string? BackupFolder { get; set; }

    /// <summary>Folder for the JSON-lines log files. Default: a Logs folder next to the default backup folder.</summary>
    public string? LogFolder { get; set; }

    /// <summary>Folder that contains pg_dump and pg_restore. Empty means they are found on the PATH.</summary>
    public string? PostgresBinFolder { get; set; }

    public string ResolveBackupFolder() => Full(BackupFolder) ?? Path.Combine(DefaultRoot(), "Backups");

    public string ResolveLogFolder() => Full(LogFolder) ?? Path.Combine(DefaultRoot(), "Logs");

    public string ResolveTool(string tool)
    {
        var name = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        return string.IsNullOrWhiteSpace(PostgresBinFolder) ? name : Path.Combine(Environment.ExpandEnvironmentVariables(PostgresBinFolder), name);
    }

    private static string? Full(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

    private static string DefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return @"C:\GMSS";
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
            ? Path.Combine(home, "Library", "Application Support", "GMSS")
            : Path.Combine(home, ".local", "share", "gmss");
    }
}
