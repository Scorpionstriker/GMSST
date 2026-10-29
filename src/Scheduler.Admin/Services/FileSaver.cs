namespace Scheduler.Admin.Services;

/// <summary>Saves an exported file (CSV) where the user can find it (FR-66).</summary>
public interface IFileSaver
{
    /// <summary>Saves the file and returns its full path.</summary>
    Task<string> SaveAsync(string fileName, string content, CancellationToken cancellationToken = default);
}

/// <summary>Saves exports to "Documents/GMSS Exports". An existing file is never overwritten.</summary>
public sealed class DocumentsFileSaver(string? folder = null) : IFileSaver
{
    private readonly string _folder = folder ?? Path.Combine(DocumentsFolder(), "GMSS Exports");

    private static string DocumentsFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrEmpty(documents) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : documents;
    }

    public async Task<string> SaveAsync(string fileName, string content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_folder);
        var safeName = string.Concat(Path.GetFileName(fileName).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);

        var path = Path.Combine(_folder, safeName);
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(_folder, $"{stem} ({n}){extension}");
        }

        // UTF-8 with a byte order mark, so Excel shows accented characters correctly.
        await File.WriteAllTextAsync(path, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
        return path;
    }
}
