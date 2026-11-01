using Scheduler.Application.Configuration;

namespace Scheduler.Admin.Services;

/// <summary>Saves an exported file (CSV) where the user can find it (FR-66).</summary>
public interface IFileSaver
{
    string DefaultFolder { get; }
    string? LastFolder { get; }

    /// <summary>Saves the file and returns its full path.</summary>
    Task<string> SaveAsync(string fileName, string content, string? folder = null, CancellationToken cancellationToken = default);

    Task RememberLastFolderAsync(string folder, CancellationToken cancellationToken = default);
}

/// <summary>Saves exports to the configured folder or "Documents/GMSS Exports". Existing files are never overwritten.</summary>
public sealed class DocumentsFileSaver(IMachineSettingsStore settings) : IFileSaver
{
    public string DefaultFolder => ResolveFolder(settings.Get().ExportFolder) ?? DefaultExportFolder;

    public string? LastFolder => ResolveFolder(settings.Get().LastExportFolder);

    public static string DefaultExportFolder => Path.Combine(DocumentsFolder(), "GMSS Exports");

    private static string DocumentsFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrEmpty(documents) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : documents;
    }

    public async Task<string> SaveAsync(string fileName, string content, string? folder = null, CancellationToken cancellationToken = default)
    {
        var targetFolder = ResolveFolder(folder) ?? DefaultFolder;
        Directory.CreateDirectory(targetFolder);
        var safeName = string.Concat(Path.GetFileName(fileName).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);

        var path = Path.Combine(targetFolder, safeName);
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(targetFolder, $"{stem} ({n}){extension}");
        }

        // UTF-8 with a byte order mark, so Excel shows accented characters correctly.
        await File.WriteAllTextAsync(path, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
        return path;
    }

    public async Task RememberLastFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFolder(folder) ?? throw new ArgumentException("Enter a full folder path.", nameof(folder));
        await settings.SaveAsync(settings.Get() with { LastExportFolder = fullPath }, cancellationToken);
    }

    private static string? ResolveFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(folder.Trim());
        if (!Path.IsPathFullyQualified(expanded))
        {
            throw new ArgumentException("Enter a full folder path.", nameof(folder));
        }

        return Path.GetFullPath(expanded);
    }
}
