namespace Scheduler.Admin.Services;

/// <summary>Lets the user choose a folder, for the backup folders (WP-5c).</summary>
public interface IFolderPicker
{
    /// <summary>False where no folder dialog is available; the user then types the path.</summary>
    bool IsSupported { get; }

    /// <summary>Shows the folder dialog; returns the chosen folder, or null when the user cancelled.</summary>
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}

/// <summary>Used where there is no folder dialog (macOS for now): the folder is typed in.</summary>
public sealed class TypedFolderPicker : IFolderPicker
{
    public bool IsSupported => false;

    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
