using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common.Persistence;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.IntegrationTests;

/// <summary>WP-3: retention keeps history complete, follows AuditRetentionDays for audit records and cleans log files.</summary>
public sealed class RetentionTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [SkippableFact]
    public async Task OldQueueEntry_IsRemoved_AndItsExecutionStays_WithoutAQueueId()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("retention");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var old = DateTime.UtcNow.AddDays(-8);
        var queueId = Guid.CreateVersion7();
        Guid executionId;
        await using (var db = database.CreateContext())
        {
            var worker = new WorkerThread("retention", WorkerThread.DefaultPoolName, old);
            db.WorkerThreads.Add(worker);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO scheduler.task_queue (queue_id, task_id, trigger_type, priority, scheduled_time, enqueued_at, queue_status, attempt)
                VALUES ({queueId}, {taskId}, 'Manual', 5, {old}, {old}, 'Processed', 0)
                """);
            var execution = TaskExecution.Start(await db.TaskQueue.SingleAsync(q => q.Id == queueId), worker.Id, old);
            execution.Complete(ExecutionStatus.Success, old.AddSeconds(1), null);
            db.TaskExecutions.Add(execution);
            await db.SaveChangesAsync();
            executionId = execution.Id;
        }

        var now = DateTime.UtcNow;
        var result = await services.GetRequiredService<IRetentionStore>().PurgeAsync(now.AddDays(-7), now.AddDays(-90), now.AddDays(-365));

        Assert.True(result.QueueEntries >= 1);
        await using var check = database.CreateContext();
        Assert.False(await check.TaskQueue.AnyAsync(q => q.Id == queueId));
        var kept = await check.TaskExecutions.SingleAsync(e => e.Id == executionId);
        Assert.Null(kept.QueueId);
        Assert.Equal(TriggerType.Manual, kept.TriggerType);
    }

    [SkippableFact]
    public async Task AuditRecords_FollowAuditRetentionDays()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("audit-retention");
        var old = DateTime.UtcNow.AddDays(-100);
        await using (var db = database.CreateContext())
        {
            var provider = await db.LoggingProviders.Select(p => p.Id).FirstAsync();
            db.LoginAudits.Add(new LoginAudit(null, "old-attempt", false, "InvalidCredentials", old));
            db.ExecutionLogs.Add(new ExecutionLogEntry(null, provider, SchedulerLogLevel.Info, "Security", """{"message":"old audit"}""", old));
            db.ExecutionLogs.Add(new ExecutionLogEntry(null, provider, SchedulerLogLevel.Info, "SchedulerEngine", """{"message":"old engine"}""", old));
            await db.SaveChangesAsync();
        }

        var store = services.GetRequiredService<IRetentionStore>();
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-99);

        // History kept 90 days, audit 365: the engine entry goes, the audit records stay.
        await store.PurgeAsync(now.AddDays(-7), now.AddDays(-90), now.AddDays(-365));
        await using (var db = database.CreateContext())
        {
            Assert.False(await db.ExecutionLogs.AnyAsync(l => l.Source == "SchedulerEngine" && l.LoggedAtUtc < cutoff));
            Assert.True(await db.ExecutionLogs.AnyAsync(l => l.Source == "Security" && l.LoggedAtUtc < cutoff));
            Assert.True(await db.LoginAudits.AnyAsync(a => a.UsernameAttempted == "old-attempt"));
        }

        // Audit kept 50 days: now they go too.
        var result = await store.PurgeAsync(now.AddDays(-7), now.AddDays(-90), now.AddDays(-50));
        Assert.Equal(2, result.AuditEntries);
        await using var check = database.CreateContext();
        Assert.False(await check.ExecutionLogs.AnyAsync(l => l.Source == "Security" && l.LoggedAtUtc < cutoff));
        Assert.False(await check.LoginAudits.AnyAsync(a => a.UsernameAttempted == "old-attempt"));
    }

    [SkippableFact]
    public void OldLogFiles_AreDeleted_OnlyWhenTheyMatchTheFileProviderPattern()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("log-files");
        var folder = Path.Combine(database.BackupFolder, "logs");
        Directory.CreateDirectory(folder);
        var now = DateTime.UtcNow;
        var old = Path.Combine(folder, $"gmss{now.AddDays(-100):yyyyMMdd}.log");
        var recent = Path.Combine(folder, $"gmss{now:yyyyMMdd}.log");
        var other = Path.Combine(folder, "notes.log");
        var lookalike = Path.Combine(folder, "gmssABCDEFGH.log");
        foreach (var file in new[] { old, recent, other, lookalike })
        {
            File.WriteAllText(file, "x");
        }

        Assert.Equal(1, services.GetRequiredService<ILogFileCleaner>().DeleteOlderThan(now.AddDays(-90)));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(lookalike));
    }
}
