using System.Globalization;

namespace Scheduler.Infrastructure.Configuration;

/// <summary>
/// Finds the folder with pg_dump and pg_restore (WP-5b), in this order: the PostgresBinFolder setting, the PATH,
/// then the usual install folders, newest version first: C:\Program Files\PostgreSQL\&lt;n&gt;\bin and
/// C:\PostgreSQL &lt;n&gt;\bin on Windows, /Library/PostgreSQL/&lt;n&gt;/bin and /opt/homebrew/opt/postgresql@&lt;n&gt;/bin on macOS,
/// and /usr/lib/postgresql/&lt;n&gt;/bin on Linux.
/// </summary>
public static class PostgresTools
{
    /// <summary>The folder found, or null.</summary>
    /// <param name="setting">The PostgresBinFolder setting; when set, only this folder is used.</param>
    /// <param name="fileExists">Checks a file; replaced in tests.</param>
    /// <param name="pathVariable">The PATH; replaced in tests.</param>
    /// <param name="candidates">The usual folders; replaced in tests.</param>
    public static string? FindFolder(string? setting, Func<string, bool>? fileExists = null, string? pathVariable = null,
        IEnumerable<string>? candidates = null)
    {
        fileExists ??= File.Exists;
        var tool = ToolFile("pg_dump");
        if (!string.IsNullOrWhiteSpace(setting))
        {
            var folder = Environment.ExpandEnvironmentVariables(setting.Trim());
            return fileExists(Path.Combine(folder, tool)) ? folder : null;
        }

        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (fileExists(Path.Combine(folder.Trim('"'), tool)))
            {
                return folder.Trim('"');
            }
        }

        return (candidates ?? UsualFolders()).FirstOrDefault(folder => fileExists(Path.Combine(folder, tool)));
    }

    public static string ToolFile(string tool) => OperatingSystem.IsWindows() ? tool + ".exe" : tool;

    /// <summary>The usual install folders of this operating system, newest version first.</summary>
    public static IEnumerable<string> UsualFolders()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return Versions(Path.Combine(programFiles, "PostgreSQL"), name => name, v => Path.Combine(programFiles, "PostgreSQL", v, "bin"))
                .Concat(VersionedSiblings(@"C:\", "PostgreSQL ", v => Path.Combine(@"C:\", "PostgreSQL " + v, "bin")));
        }

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            return Versions("/Library/PostgreSQL", name => name, v => $"/Library/PostgreSQL/{v}/bin")
                .Concat(VersionedSiblings("/opt/homebrew/opt", "postgresql@", v => $"/opt/homebrew/opt/postgresql@{v}/bin"))
                .Concat(VersionedSiblings("/usr/local/opt", "postgresql@", v => $"/usr/local/opt/postgresql@{v}/bin"));
        }

        return Versions("/usr/lib/postgresql", name => name, v => $"/usr/lib/postgresql/{v}/bin");
    }

    /// <summary>Sub-folders of <paramref name="root"/> whose name is a version number, newest first.</summary>
    private static IEnumerable<string> Versions(string root, Func<string, string> version, Func<string, string> bin) =>
        SafeDirectories(root)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Select(name => (Name: name, Number: ParseVersion(version(name))))
            .Where(v => v.Number is not null)
            .OrderByDescending(v => v.Number)
            .Select(v => bin(v.Name));

    /// <summary>Folders in <paramref name="parent"/> named prefix + version, newest first (for example "PostgreSQL 18").</summary>
    private static IEnumerable<string> VersionedSiblings(string parent, string prefix, Func<string, string> bin) =>
        SafeDirectories(parent)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => (Name: name[prefix.Length..], Number: ParseVersion(name[prefix.Length..])))
            .Where(v => v.Number is not null)
            .OrderByDescending(v => v.Number)
            .Select(v => bin(v.Name));

    private static decimal? ParseVersion(string text) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
