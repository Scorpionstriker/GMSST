using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Backup;
using Scheduler.Application.Common;

namespace Scheduler.IntegrationTests;

/// <summary>FR-67 to FR-72: verified backups, checksum check, safety backup and restore. Needs pg_dump and pg_restore on the PATH.</summary>
public sealed class BackupTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [SkippableFact]
    public async Task Restore_ReturnsTheDatabaseToTheBackup_AfterTakingASafetyBackup()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup");
        var before = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);

        var created = await services.GetRequiredService<ICommandHandler<CreateBackupCommand, BackupInfo>>().HandleAsync(new CreateBackupCommand());
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.True(created.Value!.Verified);
        Assert.StartsWith("gmss_backup_", created.Value.FileName);

        var after = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var restore = services.GetRequiredService<ICommandHandler<RestoreBackupCommand, Unit>>();

        var unconfirmed = await restore.HandleAsync(new RestoreBackupCommand(created.Value.FileName, "restore please"));
        Assert.True(unconfirmed.IsFailure);

        var result = await restore.HandleAsync(new RestoreBackupCommand(created.Value.FileName, RestoreBackupCommand.ConfirmationWord));
        Assert.True(result.IsSuccess, result.Error?.Message);

        await using var db = database.CreateContext();
        Assert.True(await db.Tasks.AnyAsync(t => t.Id == before));
        Assert.False(await db.Tasks.AnyAsync(t => t.Id == after));

        var backups = await services.GetRequiredService<IBackupService>().ListAsync();
        Assert.Contains(backups, b => b.Kind == BackupKind.PreRestore && b.Verified);
    }

    [SkippableFact]
    public async Task DamagedBackup_IsNotRestored()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup-damaged");
        var backup = await services.GetRequiredService<IBackupService>().CreateBackupAsync(BackupKind.Manual);
        await File.AppendAllTextAsync(Path.Combine(database.BackupFolder, backup.FileName), "tampered");

        var result = await services.GetRequiredService<ICommandHandler<RestoreBackupCommand, Unit>>()
            .HandleAsync(new RestoreBackupCommand(backup.FileName, RestoreBackupCommand.ConfirmationWord));

        Assert.True(result.IsFailure);
    }

    [SkippableFact]
    public async Task Retention_KeepsTheNewestBackups()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup-retention");
        var backupService = services.GetRequiredService<IBackupService>();
        for (var i = 0; i < 3; i++)
        {
            await backupService.CreateBackupAsync(BackupKind.Scheduled);
        }

        await backupService.ApplyRetentionAsync(2);
        var normal = (await backupService.ListAsync()).Where(b => b.Kind is BackupKind.Manual or BackupKind.Scheduled).ToList();
        Assert.Equal(2, normal.Count);
        Assert.Equal(normal.Count, Directory.GetFiles(database.BackupFolder, "gmss_backup_*.dump").Length);
    }

    [SkippableFact]
    public async Task PathTraversal_IsRejected()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("backup-path");
        Assert.False(await services.GetRequiredService<IBackupService>().VerifyChecksumAsync("../manifest.json"));
    }
}
