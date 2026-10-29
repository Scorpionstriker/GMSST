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

/// <summary>An entry of the backup manifest (FR-70).</summary>
public sealed record BackupInfo(string FileName, DateTime CreatedAtUtc, BackupKind Kind, long SizeBytes, bool Verified, string Sha256);

/// <summary>
/// pg_dump/pg_restore backups with a SHA-256 manifest kept outside the database (FR-67 to FR-72).
/// Implemented in the infrastructure layer.
/// </summary>
public interface IBackupService
{
    string BackupFolder { get; }

    /// <summary>Creates, verifies and records a full backup. Throws <see cref="BackupException"/> on failure.</summary>
    Task<BackupInfo> CreateBackupAsync(BackupKind kind, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the file exists and its SHA-256 checksum matches the manifest.</summary>
    Task<bool> VerifyChecksumAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>Restores in a single transaction, so a failed restore leaves the data unchanged. Throws <see cref="BackupException"/>.</summary>
    Task RestoreAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>Keeps the newest <paramref name="keepCount"/> normal backups and safety backups younger than seven days.</summary>
    Task<int> ApplyRetentionAsync(int keepCount, CancellationToken cancellationToken = default);
}

/// <summary>A backup or restore failed. The message is safe to show; tool output is in <see cref="Details"/>.</summary>
public sealed class BackupException(string message, string? details = null, Exception? inner = null) : Exception(message, inner)
{
    public string? Details { get; } = details;
}

/// <summary>"Back up now" (FR-67).</summary>
public sealed record CreateBackupCommand : IRequireAdministrator;

/// <summary>The backup list with verification results (FR-70).</summary>
public sealed record GetBackupsQuery : IRequireAdministrator;

public sealed record BackupOverview(string Folder, int RetentionCount, IReadOnlyList<BackupInfo> Backups);

/// <summary>Restores a backup after the administrator typed RESTORE (FR-72, Algorithm 6.14).</summary>
public sealed record RestoreBackupCommand(string FileName, string Confirmation) : IRequireAdministrator
{
    public const string ConfirmationWord = "RESTORE";
}

internal sealed class CreateBackupHandler(IBackupService backups, ISettingsProvider settings, ISchedulerLogger log)
    : ICommandHandler<CreateBackupCommand, BackupInfo>
{
    public async Task<Result<BackupInfo>> HandleAsync(CreateBackupCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await backups.CreateBackupAsync(BackupKind.Manual, cancellationToken);
            await backups.ApplyRetentionAsync(await settings.GetAsync(SettingKeys.BackupRetentionCount, cancellationToken), cancellationToken);
            return Result.Success(info);
        }
        catch (BackupException ex)
        {
            await log.LogAsync(SchedulerLogLevel.Error, LogSources.Backup, ex.Message,
                properties: new Dictionary<string, object?> { ["details"] = ex.Details }, cancellationToken: cancellationToken);
            return Error.Failure("Backup.Failed", ex.Message);
        }
    }
}

internal sealed class GetBackupsHandler(IBackupService backups, ISettingsProvider settings) : IQueryHandler<GetBackupsQuery, BackupOverview>
{
    public async Task<Result<BackupOverview>> HandleAsync(GetBackupsQuery query, CancellationToken cancellationToken = default) =>
        Result.Success(new BackupOverview(backups.BackupFolder, await settings.GetAsync(SettingKeys.BackupRetentionCount, cancellationToken),
            await backups.ListAsync(cancellationToken)));
}

internal sealed class RestoreBackupValidator : IValidator<RestoreBackupCommand>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(RestoreBackupCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ValidationErrors()
            .AddIf(string.IsNullOrWhiteSpace(command.FileName) || command.FileName != Path.GetFileName(command.FileName), "FileName",
                "Select a backup from the list.")
            .AddIf(!string.Equals(command.Confirmation, RestoreBackupCommand.ConfirmationWord, StringComparison.Ordinal), "Confirmation",
                "Type RESTORE in capital letters to confirm.")
            .Errors);
}

internal sealed class RestoreBackupHandler(
    IBackupService backups,
    IEngineControl engine,
    ISettingsProvider settings,
    ICurrentUser currentUser,
    ISchedulerLogger log) : ICommandHandler<RestoreBackupCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RestoreBackupCommand command, CancellationToken cancellationToken = default)
    {
        if (!await backups.VerifyChecksumAsync(command.FileName, cancellationToken))
        {
            return Error.Failure("Backup.Damaged", "The backup file is missing or damaged: its checksum does not match the manifest.");
        }

        await engine.PauseAsync(cancellationToken);
        try
        {
            try
            {
                await backups.CreateBackupAsync(BackupKind.PreRestore, cancellationToken);
            }
            catch (BackupException ex)
            {
                await log.LogAsync(SchedulerLogLevel.Error, LogSources.Backup, ex.Message,
                    properties: new Dictionary<string, object?> { ["details"] = ex.Details }, cancellationToken: cancellationToken);
                return Error.Failure("Backup.SafetyFailed", "Could not create the safety backup, so nothing was restored.");
            }

            try
            {
                await backups.RestoreAsync(command.FileName, cancellationToken);
            }
            catch (BackupException ex)
            {
                await log.LogAsync(SchedulerLogLevel.Error, LogSources.Backup, ex.Message,
                    properties: new Dictionary<string, object?> { ["details"] = ex.Details }, cancellationToken: cancellationToken);
                return Error.Failure("Backup.RestoreFailed", "The restore failed; the current data is unchanged.");
            }

            settings.Invalidate();
            await log.LogAsync(SchedulerLogLevel.Warning, LogSources.Backup, $"Database restored from {command.FileName} by {currentUser.Username ?? "the local user"}.",
                properties: new Dictionary<string, object?> { ["file"] = command.FileName, ["userId"] = currentUser.UserId },
                cancellationToken: cancellationToken);
            return Result.Success(Unit.Value);
        }
        finally
        {
            // Resuming runs start-up recovery, which handles entries captured mid-execution in the backup.
            await engine.ResumeAsync(CancellationToken.None);
        }
    }
}
