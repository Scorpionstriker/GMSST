using Microsoft.Extensions.Logging;
using Scheduler.Application.Backup;
using Scheduler.Application.Configuration;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.Handlers;

/// <summary>Takes a scheduled, verified backup and applies the backup retention (FR-68, Phase 4 Table 8.3).</summary>
public sealed class DatabaseBackupHandler(IBackupService backups, ISettingsProvider settings) : ITaskHandler
{
    public const string Type = "DatabaseBackup";

    public string TaskType => Type;
    public string DisplayName => "Database backup";
    public string Description => "Backs up the scheduler database with pg_dump and keeps the newest BackupRetentionCount backups.";
    public string PayloadExample => "{}";

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        try
        {
            var backup = await backups.CreateBackupAsync(BackupKind.Scheduled, context.CancellationToken);
            var removed = await backups.ApplyRetentionAsync(await settings.GetAsync(SettingKeys.BackupRetentionCount, context.CancellationToken),
                context.CancellationToken);
            context.Logger.LogInformation("Backup {File} created ({Size} bytes); {Removed} old backup(s) removed.", backup.FileName, backup.SizeBytes, removed);

            var output = new { backup.FileName, backup.SizeBytes, backup.Verified, removed };
            return backup.Verified
                ? TaskResult.Success($"Backup {backup.FileName} created and verified.", output)
                : TaskResult.Failure($"Backup {backup.FileName} was created but could not be verified.", output);
        }
        catch (BackupException ex)
        {
            context.Logger.LogError("Backup failed: {Details}", ex.Details);
            return TaskResult.Failure(ex.Message);
        }
    }
}
