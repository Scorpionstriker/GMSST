namespace Scheduler.Infrastructure.Configuration;

/// <summary>
/// The "Paths" section of the configuration: where log files and backups are written, and where the PostgreSQL
/// client tools are. Empty values use the defaults of the operating system. The values usually come from the user
/// settings file of this computer (WP-5a), so they can differ between computers sharing a backup.
/// </summary>
public sealed class PathsOptions
{
    public const string SectionName = "Paths";

    /// <summary>Folder for backup sets. Default: C:\GMSS\Backups on Windows, ~/Library/Application Support/GMSS/Backups on macOS.</summary>
    public string? BackupFolder { get; set; }

    /// <summary>Optional second folder, for example on an external drive, that every verified backup set is copied to (WP-5d).</summary>
    public string? BackupCopyFolder { get; set; }

    /// <summary>Folder for the JSON-lines log files. Default: a Logs folder next to the default backup folder.</summary>
    public string? LogFolder { get; set; }

    /// <summary>Folder that contains pg_dump and pg_restore. Empty: the PATH, then the usual install folders (WP-5b).</summary>
    public string? PostgresBinFolder { get; set; }

    public string ResolveBackupFolder() => Full(BackupFolder) ?? Path.Combine(DefaultRoot(), "Backups");

    public string? ResolveBackupCopyFolder() => Full(BackupCopyFolder);

    public string ResolveLogFolder() => Full(LogFolder) ?? Path.Combine(DefaultRoot(), "Logs");

    private static string? Full(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));

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
