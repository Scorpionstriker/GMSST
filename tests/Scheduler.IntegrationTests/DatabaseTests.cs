using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Application.Executions;
using Scheduler.Application.Queue;
using Scheduler.Application.Reports;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.IntegrationTests;

public sealed class DatabaseTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [SkippableFact]
    public async Task Migration_CreatesTheThirteenTablesAndSeedsDefaults()
    {
        RequiresDatabase.Check();
        await using var db = database.CreateContext();
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'scheduler' AND table_name <> '__ef_migrations_history'")
            .ToListAsync();
        // The twelve tables of Phase 4, Section 5, and engine_instance (WP-1).
        Assert.Equal(13, tables.Count);
        Assert.Contains("engine_instance", tables);

        var keys = await db.Configuration.Where(c => c.TaskId == null).Select(c => c.Key).ToListAsync();
        Assert.All(SettingDefinitions.All, d => Assert.Contains(d.Key, keys));
        Assert.Equal(3, await db.LoggingProviders.CountAsync());
        Assert.False(await db.AppUsers.AnyAsync());
    }

    [SkippableFact]
    public async Task Initializer_CanRunAgainWithoutDuplicatingSeedData()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("init-again");
        await services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

        await using var db = database.CreateContext();
        Assert.Equal(SettingDefinitions.All.Count, await db.Configuration.CountAsync(c => c.TaskId == null));
    }

    [SkippableFact]
    public async Task TaskNames_AreUniqueIgnoringCase_ButDeletedTasksDoNotCount()
    {
        RequiresDatabase.Check();
        var now = DateTime.UtcNow;
        await using var db = database.CreateContext();
        var first = new ScheduledTask("Nightly Report", null, CountingHandler.Type, "{}", 5, null, 60, now, null);
        db.Tasks.Add(first);
        await db.SaveChangesAsync();

        db.Tasks.Add(new ScheduledTask("NIGHTLY report", null, CountingHandler.Type, "{}", 5, null, 60, now, null));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("ux_task_name", Assert.IsType<PostgresException>(ex.InnerException).ConstraintName);

        db.ChangeTracker.Clear();
        var stored = await db.Tasks.FirstAsync(t => t.Id == first.Id);
        stored.Delete(now, null);
        db.Tasks.Add(new ScheduledTask("NIGHTLY report", null, CountingHandler.Type, "{}", 5, null, 60, now, null));
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task CheckConstraints_RejectInvalidValues()
    {
        RequiresDatabase.Check();
        await using var db = database.CreateContext();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO scheduler.task (task_id, name, task_type, priority, status, timeout_seconds) VALUES (gen_random_uuid(), 'bad', 'x', 11, 'Active', 10)"));
        Assert.Equal("ck_task_priority", ex.ConstraintName);

        ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO scheduler.configuration (config_id, task_id, config_key, config_value, scope) VALUES (gen_random_uuid(), NULL, 'MaxConcurrency', '2', 'Global')"));
        Assert.Equal("ux_configuration_task_key", ex.ConstraintName);
    }

    [SkippableFact]
    public async Task ConcurrentEdits_AreRejected()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("concurrency");
        var id = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();

        await using var first = factory.Create();
        await using var second = factory.Create();
        var a = (await first.Tasks.GetAsync(id))!;
        var b = (await second.Tasks.GetAsync(id))!;
        a.Pause(DateTime.UtcNow, null);
        await first.SaveChangesAsync();

        b.Disable(DateTime.UtcNow, null);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => second.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task ReadQueries_TranslateToSql()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("queries");
        await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type, name: "Query 50%_test");

        var tasks = services.GetRequiredService<ITaskQueries>();
        foreach (var sort in Enum.GetValues<TaskSortColumn>())
        {
            await tasks.SearchAsync(new TaskSearchCriteria(SortBy: sort, Descending: true), new PageRequest());
        }

        var found = await tasks.SearchAsync(new TaskSearchCriteria(Text: "50%_"), new PageRequest());
        Assert.Single(found.Items);
        Assert.Empty((await tasks.SearchAsync(new TaskSearchCriteria(Text: "5_%"), new PageRequest())).Items);
        await tasks.GetActiveSchedulesAsync(null);

        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(1);
        await services.GetRequiredService<IQueueQueries>().GetEntriesAsync([QueueStatus.Waiting], new PageRequest());
        await services.GetRequiredService<IQueueQueries>().GetCountsAsync();
        var executions = services.GetRequiredService<IExecutionQueries>();
        await executions.SearchAsync(new ExecutionFilter(from, to, Status: ExecutionStatus.Success), new PageRequest());
        await executions.SearchErrorsAsync(new ErrorFilter(from, to, Text: "x"), new PageRequest());
        var reports = services.GetRequiredService<IReportQueries>();
        var filter = new ReportFilter(from, to, null, null);
        await reports.GetDashboardAsync(DateTime.UtcNow);
        await reports.GetExecutionSummaryAsync(filter);
        await reports.GetFailuresAsync(filter, 10);
        await reports.GetPerformanceAsync(filter);
        await reports.GetLoginAuditAsync(filter, 10);
    }
}
