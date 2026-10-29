using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common.Persistence;
using Scheduler.Infrastructure.Configuration;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.IntegrationTests;

/// <summary>FR-73 (WP-5i): pending migrations are not applied when the safety backup fails.</summary>
public sealed class UpgradeSafetyTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [SkippableFact]
    public async Task PendingMigration_IsNotApplied_WhenTheSafetyBackupFails()
    {
        RequiresDatabase.Check();
        string lastMigration;
        await using (var db = database.CreateContext())
        {
            // As if the database were one version behind: the newest migration is marked as not applied.
            lastMigration = (await db.Database.SqlQueryRaw<string>(
                "SELECT \"MigrationId\" AS \"Value\" FROM scheduler.__ef_migrations_history ORDER BY \"MigrationId\" DESC LIMIT 1").ToListAsync()).Single();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM scheduler.__ef_migrations_history WHERE \"MigrationId\" = {0}", lastMigration);
        }

        var missingTools = Path.Combine(database.BackupFolder, "no-tools-here");
        var services = database.CreateServices("upgrade", configure: s => s.Configure<PathsOptions>(p => p.PostgresBinFolder = missingTools));

        var blocked = await Assert.ThrowsAsync<DatabaseUpgradeBlockedException>(() => services.GetRequiredService<IDatabaseInitializer>().InitializeAsync());
        Assert.StartsWith("The database upgrade was stopped because the safety backup failed: pg_dump was not found in", blocked.Message);

        await using var check = database.CreateContext();
        var applied = await check.Database.SqlQueryRaw<string>("SELECT \"MigrationId\" AS \"Value\" FROM scheduler.__ef_migrations_history").ToListAsync();
        Assert.DoesNotContain(lastMigration, applied);
    }
}
