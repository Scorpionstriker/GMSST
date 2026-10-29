using Scheduler.Application.Common;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Backup;

/// <summary>Why a backup was taken (Table 8.3).</summary>
public enum BackupKind
{
    Manual = 1,
    Scheduled = 2,
    PreRestore = 3,
    PreMigration = 4
}

/// <summary>One database's file in a backup set: a pg_dump custom-format file checked with pg_restore --list (FR-67, FR-70).</summary>
public sealed record BackupFileInfo(string Database, string FileName, long SizeBytes, bool Verified, string Sha256, string? Error = null);

/// <summary>
/// A backup set (WP-5c): a folder named after the kind, date and time, holding one file per database and a
/// manifest.json with the time, kind, sizes, verification results and SHA-256 checksums.
/// </summary>
public sealed record BackupSetInfo(string SetName, string Folder, DateTime CreatedAtUtc, BackupKind Kind, IReadOnlyList<BackupFileInfo> Files)
{
    /// <summary>Problems that did not stop the backup, for example a database that does not exist or a failed copy.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool Verified => Files.Count > 0 && Files.All(f => f.Verified);

    public long SizeBytes => Files.Sum(f => f.SizeBytes);

    public IReadOnlyList<string> Databases => Files.Select(f => f.Database).ToList();

    /// <summary>The first file that could not be verified, for the warning message.</summary>
    public BackupFileInfo? FirstUnverified => Files.FirstOrDefault(f => !f.Verified);
}

/// <summary>Where pg_dump and pg_restore were found (WP-5b): the folder, or the error to show.</summary>
public sealed record PostgresToolsInfo(string? Folder, string? Error)
{
    public const string NotFoundMessage = "pg_dump was not found. Set the PostgreSQL tools folder on the Configuration page.";

    public bool Found => Error is null;
}

/// <summary>
/// pg_dump/pg_restore backup sets with a SHA-256 manifest kept outside the database (FR-67 to FR-72, WP-5).
/// Implemented in the infrastructure layer.
/// </summary>
public interface IBackupService
{
    /// <summary>The default backup folder (Paths:BackupFolder, or the operating system's default).</summary>
    string BackupFolder { get; }

    /// <summary>The optional copy folder (Paths:BackupCopyFolder); null when copies are off.</summary>
    string? BackupCopyFolder { get; }

    /// <summary>The folder of the File logging provider, shown on the Configuration page.</summary>
    string LogFolder { get; }

    /// <summary>The scheduler's own database (normally gmss).</summary>
    string SchedulerDatabase { get; }

    /// <summary>The databases a backup can include: the scheduler's, inventory_demo and maintenance_demo (Backup:Databases).</summary>
    IReadOnlyList<string> KnownDatabases { get; }

    PostgresToolsInfo FindTools();

    /// <summary>
    /// Creates, verifies and records a backup set of <paramref name="databases"/> (all known databases when null) in
    /// <paramref name="folder"/> (the default folder when null), then copies it to the copy folder. Databases that do
    /// not exist are skipped with a warning when no list was given. Throws <see cref="BackupException"/> on failure.
    /// </summary>
    Task<BackupSetInfo> CreateBackupAsync(BackupKind kind, IReadOnlyCollection<string>? databases = null, string? folder = null,
        CancellationToken cancellationToken = default);

    /// <summary>The backup sets in <paramref name="folder"/> (the default folder when null), newest first.</summary>
    Task<IReadOnlyList<BackupSetInfo>> ListAsync(string? folder = null, CancellationToken cancellationToken = default);

    /// <summary>One backup set by name, or null when it does not exist in the folder.</summary>
    Task<BackupSetInfo?> FindAsync(string setName, string? folder = null, CancellationToken cancellationToken = default);

    /// <summary>Null when the files of <paramref name="databases"/> exist and match their checksums, otherwise the problem.</summary>
    Task<string?> CheckChecksumsAsync(BackupSetInfo set, IReadOnlyCollection<string> databases, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores one database from the set with --clean --if-exists --single-transaction --no-owner --exit-on-error, so a
    /// failed restore leaves that database unchanged. Throws <see cref="BackupException"/>.
    /// </summary>
    Task RestoreAsync(BackupSetInfo set, string database, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps the newest <paramref name="keepCount"/> normal sets and safety sets younger than seven days, in the backup
    /// folder and the copy folder. Returns how many sets were removed.
    /// </summary>
    Task<int> ApplyRetentionAsync(int keepCount, CancellationToken cancellationToken = default);
}

/// <summary>A backup or restore failed. The message is safe to show; tool output is in <see cref="Details"/>.</summary>
public sealed class BackupException(string message, string? details = null, Exception? inner = null) : Exception(message, inner)
{
    public string? Details { get; } = details;
}

/// <summary>"Back up now" (FR-67): the chosen databases (all when null) into the chosen folder (the default when null).</summary>
public sealed record CreateBackupCommand(IReadOnlyList<string>? Databases = null, string? Folder = null) : IRequireAdministrator;

/// <summary>The backup sets in a folder with their verification results (FR-70).</summary>
public sealed record GetBackupsQuery(string? Folder = null) : IRequireAdministrator;

public sealed record BackupOverview(string Folder, string DefaultFolder, string? CopyFolder, int RetentionCount, IReadOnlyList<string> KnownDatabases,
    PostgresToolsInfo Tools, IReadOnlyList<BackupSetInfo> Backups);

/// <summary>Restores the chosen databases of a set after the administrator typed RESTORE (FR-72, Algorithm 6.14, WP-5g).</summary>
public sealed record RestoreBackupCommand(string SetName, IReadOnlyList<string> Databases, string Confirmation, string? Folder = null)
    : IRequireAdministrator
{
    public const string ConfirmationWord = "RESTORE";
}

/// <summary>What a restore did. <see cref="SignedOut"/> is true when the scheduler database was restored and the user was signed out.</summary>
public sealed record RestoreResult(IReadOnlyList<string> Restored, string SafetySet, bool EngineResumed, bool SignedOut);

internal static class BackupFolders
{
    /// <summary>A chosen folder must be a full path; null or empty means the default folder.</summary>
    public static bool IsValid(string? folder) =>
        string.IsNullOrWhiteSpace(folder) || (folder.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(folder.Trim()));

    public static string? Normalise(string? folder) => string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder.Trim());

    public static async Task LogFailureAsync(ISchedulerLogger log, BackupException ex, CancellationToken cancellationToken) =>
        await log.LogAsync(SchedulerLogLevel.Error, LogSources.Backup, ex.Message,
            properties: new Dictionary<string, object?> { ["details"] = ex.Details }, cancellationToken: cancellationToken);

    public static async Task LogWarningsAsync(ISchedulerLogger log, BackupSetInfo set, CancellationToken cancellationToken)
    {
        foreach (var warning in set.Warnings)
        {
            await log.LogAsync(SchedulerLogLevel.Warning, LogSources.Backup, warning,
                properties: new Dictionary<string, object?> { ["set"] = set.SetName }, cancellationToken: cancellationToken);
        }
    }
}

internal sealed class CreateBackupValidator(IBackupService backups) : IValidator<CreateBackupCommand>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(CreateBackupCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ValidationErrors()
            .AddIf(command.Databases is { Count: 0 }, "Databases", "Select at least one database to back up.")
            .AddIf(command.Databases?.Any(d => !backups.KnownDatabases.Contains(d, StringComparer.Ordinal)) == true, "Databases",
                "Select only the databases listed.")
            .AddIf(!BackupFolders.IsValid(command.Folder), "Folder", "Enter a full folder path, for example D:\\Backups.")
            .Errors);
}

internal sealed class CreateBackupHandler(IBackupService backups, ISettingsProvider settings, ISchedulerLogger log)
    : ICommandHandler<CreateBackupCommand, BackupSetInfo>
{
    public async Task<Result<BackupSetInfo>> HandleAsync(CreateBackupCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var set = await backups.CreateBackupAsync(BackupKind.Manual, command.Databases, BackupFolders.Normalise(command.Folder), cancellationToken);
            await BackupFolders.LogWarningsAsync(log, set, cancellationToken);
            await backups.ApplyRetentionAsync(await settings.GetAsync(SettingKeys.BackupRetentionCount, cancellationToken), cancellationToken);
            return Result.Success(set);
        }
        catch (BackupException ex)
        {
            await BackupFolders.LogFailureAsync(log, ex, cancellationToken);
            return Error.Failure("Backup.Failed", ex.Message);
        }
    }
}

internal sealed class GetBackupsHandler(IBackupService backups, ISettingsProvider settings) : IQueryHandler<GetBackupsQuery, BackupOverview>
{
    public async Task<Result<BackupOverview>> HandleAsync(GetBackupsQuery query, CancellationToken cancellationToken = default)
    {
        if (!BackupFolders.IsValid(query.Folder))
        {
            return Error.Validation("Folder", "Enter a full folder path, for example D:\\Backups.");
        }

        var folder = BackupFolders.Normalise(query.Folder) ?? backups.BackupFolder;
        return Result.Success(new BackupOverview(folder, backups.BackupFolder, backups.BackupCopyFolder,
            await settings.GetAsync(SettingKeys.BackupRetentionCount, cancellationToken), backups.KnownDatabases, backups.FindTools(),
            await backups.ListAsync(folder, cancellationToken)));
    }
}

internal sealed class RestoreBackupValidator : IValidator<RestoreBackupCommand>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(RestoreBackupCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ValidationErrors()
            .AddIf(string.IsNullOrWhiteSpace(command.SetName) || command.SetName != Path.GetFileName(command.SetName), "SetName",
                "Select a backup from the list.")
            .AddIf(command.Databases.Count == 0, "Databases", "Select at least one database to restore.")
            .AddIf(!BackupFolders.IsValid(command.Folder), "Folder", "Enter a full folder path, for example D:\\Backups.")
            .AddIf(!string.Equals(command.Confirmation, RestoreBackupCommand.ConfirmationWord, StringComparison.Ordinal), "Confirmation",
                "Type RESTORE in capital letters to confirm.")
            .Errors);
}

/// <summary>
/// WP-5g: checks the checksums, asks the modules' restore guards (the Maintenance Agent must be stopped before
/// maintenance_demo is restored), remembers whether the engine was running and pauses it, takes a safety set of the
/// chosen databases and restores them one by one. After the scheduler database is restored, the engine re-registers
/// its workers, the settings cache is cleared and the user is signed out. The engine resumes only if it was running.
/// </summary>
internal sealed class RestoreBackupHandler(
    IBackupService backups,
    IEngineControl engine,
    ISettingsProvider settings,
    ICurrentUser currentUser,
    IEnumerable<IRestoreGuard> guards,
    IEnumerable<IAuthenticationProvider> authentication,
    ISchedulerLogger log) : ICommandHandler<RestoreBackupCommand, RestoreResult>
{
    public async Task<Result<RestoreResult>> HandleAsync(RestoreBackupCommand command, CancellationToken cancellationToken = default)
    {
        var set = await backups.FindAsync(command.SetName, BackupFolders.Normalise(command.Folder), cancellationToken);
        if (set is null)
        {
            return Error.NotFound("Backup");
        }

        var databases = command.Databases.Distinct(StringComparer.Ordinal).ToList();
        if (databases.FirstOrDefault(d => !set.Databases.Contains(d, StringComparer.Ordinal)) is { } missing)
        {
            return Error.Validation("Databases", $"The backup does not contain the database {missing}.");
        }

        if (await backups.CheckChecksumsAsync(set, databases, cancellationToken) is { } damaged)
        {
            return Error.Failure("Backup.Damaged", damaged);
        }

        foreach (var guard in guards)
        {
            if (await guard.CheckRestoreAsync(databases, cancellationToken) is { } refusal)
            {
                return Error.Failure("Backup.InUse", refusal);
            }
        }

        var wasRunning = engine.State == Extensibility.Events.EngineState.Running;
        var user = currentUser.Username ?? "the local user";
        await engine.PauseAsync(cancellationToken);
        var restored = new List<string>();
        var resumed = false;
        try
        {
            BackupSetInfo safety;
            try
            {
                safety = await backups.CreateBackupAsync(BackupKind.PreRestore, databases, null, cancellationToken);
            }
            catch (BackupException ex)
            {
                await BackupFolders.LogFailureAsync(log, ex, cancellationToken);
                return Error.Failure("Backup.SafetyFailed", "Could not create the safety backup, so nothing was restored.");
            }

            if (!safety.Verified)
            {
                return Error.Failure("Backup.SafetyFailed",
                    $"The safety backup of {safety.FirstUnverified?.Database} could not be verified, so nothing was restored.");
            }

            foreach (var database in databases)
            {
                try
                {
                    await backups.RestoreAsync(set, database, cancellationToken);
                    restored.Add(database);
                }
                catch (BackupException ex)
                {
                    await BackupFolders.LogFailureAsync(log, ex, cancellationToken);
                    var done = restored.Count == 0 ? "No database was restored." : $"Already restored: {string.Join(", ", restored)}.";
                    return Error.Failure("Backup.RestoreFailed",
                        $"Restoring {database} failed and it was left unchanged. {done} The safety backup {safety.SetName} can undo the restored databases.");
                }
            }

            var schedulerRestored = restored.Contains(backups.SchedulerDatabase, StringComparer.Ordinal);
            if (schedulerRestored)
            {
                settings.Invalidate();
                await engine.RefreshAfterRestoreAsync(cancellationToken);
            }

            await log.LogAsync(SchedulerLogLevel.Warning, LogSources.Backup,
                $"{string.Join(", ", restored)} restored from {set.SetName} by {user}. Safety backup: {safety.SetName}.",
                properties: new Dictionary<string, object?> { ["set"] = set.SetName, ["databases"] = restored, ["userId"] = currentUser.UserId },
                cancellationToken: cancellationToken);

            if (wasRunning)
            {
                // Resuming runs start-up recovery, which handles entries captured mid-execution in the backup.
                await engine.ResumeAsync(CancellationToken.None);
                resumed = true;
            }

            var signedOut = false;
            if (schedulerRestored)
            {
                // The restored user table may differ from the one the session was made from (WP-5g).
                foreach (var provider in authentication)
                {
                    await provider.SignOutAsync();
                    signedOut = true;
                }
            }

            return Result.Success(new RestoreResult(restored, safety.SetName, resumed, signedOut));
        }
        finally
        {
            // F-35: resume only if the engine was running before; a failed restore also leaves it as it was.
            if (wasRunning && !resumed)
            {
                await engine.ResumeAsync(CancellationToken.None);
            }
        }
    }
}
