using System.Text.Json;
using Microsoft.Extensions.Logging;
using Scheduler.Application.Backup;
using Scheduler.Application.Configuration;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.Handlers;

/// <summary>
/// Takes a scheduled, verified backup set and applies the backup retention (FR-68, FR-69, Phase 4 Table 8.3). The payload
/// <c>{ "databases": ["gmss", "inventory_demo", "maintenance_demo"] }</c> chooses the databases; without it, all known
/// databases that exist are backed up (WP-5e). The run fails when a file cannot be verified.
/// </summary>
public sealed class DatabaseBackupHandler(IBackupService backups, ISettingsProvider settings) : ITaskHandler
{
    public const string Type = "DatabaseBackup";

    public string TaskType => Type;
    public string DisplayName => "Database backup";
    public string Description => "Backs up the chosen databases with pg_dump, one verified file each, and keeps the newest BackupRetentionCount sets.";
    public string PayloadExample => """{ "databases": ["gmss", "inventory_demo", "maintenance_demo"] }""";

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        IReadOnlyList<string>? databases = null;
        if (context.Payload.ValueKind == JsonValueKind.Object && context.Payload.TryGetProperty("databases", out var list)
            && list.ValueKind != JsonValueKind.Null)
        {
            if (list.ValueKind != JsonValueKind.Array || list.EnumerateArray().Any(d => d.ValueKind != JsonValueKind.String))
            {
                return TaskResult.Failure("\"databases\" must be a list of database names, for example [\"gmss\"].");
            }

            databases = list.EnumerateArray().Select(d => d.GetString()!).ToList();
            if (databases.Count == 0)
            {
                return TaskResult.Failure("\"databases\" must name at least one database.");
            }

            if (databases.FirstOrDefault(d => !backups.KnownDatabases.Contains(d, StringComparer.Ordinal)) is { } unknown)
            {
                return TaskResult.Failure($"\"{unknown}\" is not one of the databases that can be backed up ({string.Join(", ", backups.KnownDatabases)}).");
            }
        }

        try
        {
            var set = await backups.CreateBackupAsync(BackupKind.Scheduled, databases, null, context.CancellationToken);
            var removed = await backups.ApplyRetentionAsync(await settings.GetAsync(SettingKeys.BackupRetentionCount, context.CancellationToken),
                context.CancellationToken);
            foreach (var warning in set.Warnings)
            {
                context.Logger.LogWarning("{Warning}", warning);
            }

            context.Logger.LogInformation("Backup set {Set} created ({Size} bytes); {Removed} old set(s) removed.", set.SetName, set.SizeBytes, removed);
            var output = new { set.SetName, set.Databases, set.SizeBytes, set.Verified, set.Warnings, removed };
            return set.Verified
                ? TaskResult.Success($"Backup {set.SetName} of {string.Join(", ", set.Databases)} created and verified.", output)
                : TaskResult.Failure($"Backup {set.SetName} was created but {set.FirstUnverified?.FileName} could not be verified.", output);
        }
        catch (BackupException ex)
        {
            context.Logger.LogError("Backup failed: {Details}", ex.Details);
            return TaskResult.Failure(ex.Message);
        }
    }
}
