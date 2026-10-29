using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Scheduler.Application.Backup;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Engine;
using Scheduler.Extensibility.Contracts;
using Scheduler.Extensibility.Events;
using Scheduler.Infrastructure.Configuration;

namespace Scheduler.IntegrationTests;

/// <summary>
/// FR-67 to FR-72 and WP-5: verified backup sets, checksum check, copy folder, restore guards, safety backup, engine
/// state after a restore and a restore into a freshly created database. Needs pg_dump and pg_restore (found on the
/// PATH or in the usual install folders).
/// </summary>
public sealed class BackupTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private string[] Databases => [database.Options.Name];

    [SkippableFact]
    public async Task Restore_ReturnsTheDatabaseToTheBackup_AfterTakingASafetyBackup()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup");
        var before = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);

        var created = await services.GetRequiredService<ICommandHandler<CreateBackupCommand, BackupSetInfo>>()
            .HandleAsync(new CreateBackupCommand(Databases));
        Assert.True(created.IsSuccess, created.Error?.Message);
        var set = created.Value!;
        Assert.True(set.Verified);
        Assert.StartsWith("gsst_backup_", set.SetName);
        Assert.True(File.Exists(Path.Combine(set.Folder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(set.Folder, database.Options.Name + ".dump")));

        var after = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var restore = services.GetRequiredService<ICommandHandler<RestoreBackupCommand, RestoreResult>>();

        var unconfirmed = await restore.HandleAsync(new RestoreBackupCommand(set.SetName, Databases, "restore please"));
        Assert.True(unconfirmed.IsFailure);

        var result = await restore.HandleAsync(new RestoreBackupCommand(set.SetName, Databases, RestoreBackupCommand.ConfirmationWord));
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(Databases, result.Value!.Restored);

        await using var db = database.CreateContext();
        Assert.True(await db.Tasks.AnyAsync(t => t.Id == before));
        Assert.False(await db.Tasks.AnyAsync(t => t.Id == after));

        var sets = await services.GetRequiredService<IBackupService>().ListAsync();
        Assert.Contains(sets, s => s.Kind == BackupKind.PreRestore && s.Verified && s.SetName == result.Value.SafetySet);
    }

    [SkippableFact]
    public async Task Restore_ResumesTheEngineOnlyIfItWasRunning()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup-engine");
        var backups = services.GetRequiredService<IBackupService>();
        var set = await backups.CreateBackupAsync(BackupKind.Manual, Databases);
        var restore = services.GetRequiredService<ICommandHandler<RestoreBackupCommand, RestoreResult>>();
        var engine = services.GetRequiredService<SchedulerEngine>();
        await engine.StartAsync();
        try
        {
            var running = await restore.HandleAsync(new RestoreBackupCommand(set.SetName, Databases, RestoreBackupCommand.ConfirmationWord));
            Assert.True(running.IsSuccess, running.Error?.Message);
            Assert.True(running.Value!.EngineResumed);
            Assert.Equal(EngineState.Running, engine.State);

            // After the restore the engine still works: its workers were registered again.
            var taskId = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type, name: "after restore");
            Assert.True(await services.GetRequiredService<ISchedulerClient>().RunNowAsync("after restore"));
            await Wait.UntilAsync(async () =>
            {
                await using var db = database.CreateContext();
                return await db.TaskExecutions.AnyAsync(e => e.TaskId == taskId && e.Status == Domain.Enums.ExecutionStatus.Success);
            }, TimeSpan.FromSeconds(30), "a task ran after the restore");

            await engine.PauseAsync();
            var paused = await restore.HandleAsync(new RestoreBackupCommand(set.SetName, Databases, RestoreBackupCommand.ConfirmationWord));
            Assert.True(paused.IsSuccess, paused.Error?.Message);
            Assert.False(paused.Value!.EngineResumed);
            Assert.Equal(EngineState.Paused, engine.State);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [SkippableFact]
    public async Task DamagedBackup_IsNotRestored()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup-damaged");
        var set = await services.GetRequiredService<IBackupService>().CreateBackupAsync(BackupKind.Manual, Databases);
        await File.AppendAllTextAsync(Path.Combine(set.Folder, set.Files[0].FileName), "tampered");

        var result = await services.GetRequiredService<ICommandHandler<RestoreBackupCommand, RestoreResult>>()
            .HandleAsync(new RestoreBackupCommand(set.SetName, Databases, RestoreBackupCommand.ConfirmationWord));

        Assert.True(result.IsFailure);
        Assert.Contains("damaged", result.Error!.Message);
    }

    [SkippableFact]
    public async Task RestoreGuard_StopsTheRestore()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup-guard", configure: s => s.AddSingleton<IRestoreGuard>(new RefusingGuard()));
        var set = await services.GetRequiredService<IBackupService>().CreateBackupAsync(BackupKind.Manual, Databases);

        var result = await services.GetRequiredService<ICommandHandler<RestoreBackupCommand, RestoreResult>>()
            .HandleAsync(new RestoreBackupCommand(set.SetName, Databases, RestoreBackupCommand.ConfirmationWord));

        Assert.True(result.IsFailure);
        Assert.Equal(RefusingGuard.Message, result.Error!.Message);
        Assert.DoesNotContain(await services.GetRequiredService<IBackupService>().ListAsync(), s => s.Kind == BackupKind.PreRestore && s.CreatedAtUtc > set.CreatedAtUtc);
    }

    [SkippableFact]
    public async Task VerifiedSet_IsCopiedToTheCopyFolder_AndAFailedCopyIsOnlyAWarning()
    {
        RequiresDatabase.Check();
        var copyFolder = Path.Combine(database.BackupFolder, "copies");
        var services = database.CreateServices("backup-copy", configure: s => s.Configure<PathsOptions>(p => p.BackupCopyFolder = copyFolder));
        var set = await services.GetRequiredService<IBackupService>().CreateBackupAsync(BackupKind.Manual, Databases);

        Assert.Empty(set.Warnings);
        Assert.True(File.Exists(Path.Combine(copyFolder, set.SetName, set.Files[0].FileName)));
        Assert.Contains(await services.GetRequiredService<IBackupService>().ListAsync(copyFolder), s => s.SetName == set.SetName && s.Verified);

        // A copy folder that cannot be created (a file is in the way) gives a warning, not a failed backup.
        var blocked = Path.Combine(database.BackupFolder, "not-a-folder");
        await File.WriteAllTextAsync(blocked, "x");
        var failing = database.CreateServices("backup-copy-fail", configure: s => s.Configure<PathsOptions>(p => p.BackupCopyFolder = blocked));
        var second = await failing.GetRequiredService<IBackupService>().CreateBackupAsync(BackupKind.Manual, Databases);
        Assert.True(second.Verified);
        Assert.Single(second.Warnings);
    }

    [SkippableFact]
    public async Task Retention_KeepsTheNewestSets()
    {
        RequiresDatabase.Check();
        var folder = Path.Combine(database.BackupFolder, "retention");
        var services = database.CreateServices("backup-retention", configure: s => s.Configure<PathsOptions>(p => p.BackupFolder = folder));
        var backupService = services.GetRequiredService<IBackupService>();
        for (var i = 0; i < 3; i++)
        {
            await backupService.CreateBackupAsync(BackupKind.Scheduled, Databases);
        }

        await backupService.ApplyRetentionAsync(2);
        var normal = (await backupService.ListAsync()).Where(b => b.Kind is BackupKind.Manual or BackupKind.Scheduled).ToList();
        Assert.Equal(2, normal.Count);
        Assert.Equal(2, Directory.GetDirectories(folder, "gsst_backup_*").Length);
    }

    [SkippableFact]
    public async Task PathTraversal_IsRejected()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup-path");
        var backups = services.GetRequiredService<IBackupService>();
        Assert.Null(await backups.FindAsync("../" + Path.GetFileName(database.BackupFolder)));
        Assert.Null(await backups.FindAsync(".."));
    }

    [SkippableFact]
    public async Task BackupOfAMissingDatabase_FailsWithAClearMessage()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup-missing");
        var backups = services.GetRequiredService<IBackupService>();

        var chosen = await Assert.ThrowsAsync<BackupException>(() => backups.CreateBackupAsync(BackupKind.Manual, ["gsst_no_such_database"]));
        Assert.Equal("The database gsst_no_such_database does not exist on this PostgreSQL server.", chosen.Message);
    }

    /// <summary>WP-5j: a backup set restores into a new, empty database, as on another computer.</summary>
    [SkippableFact]
    public async Task Set_RestoresIntoAFreshlyCreatedDatabase()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup-fresh");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var backups = services.GetRequiredService<IBackupService>();
        var set = await backups.CreateBackupAsync(BackupKind.Manual, Databases);

        NpgsqlConnection.ClearAllPools();
        await database.RecreateEmptyAsync();

        await backups.RestoreAsync(set, database.Options.Name);
        await using var db = database.CreateContext();
        Assert.True(await db.Tasks.AnyAsync(t => t.Id == taskId));
    }

    private sealed class RefusingGuard : IRestoreGuard
    {
        public const string Message = "Stop the Maintenance Agent first, then restore.";

        public Task<string?> CheckRestoreAsync(IReadOnlyCollection<string> databaseNames, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(Message);
    }
}
