using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Scheduler.Application.Backup;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Infrastructure.Logging;

namespace Scheduler.Infrastructure.Persistence;

internal sealed class DatabaseInitializer(
    IDbContextFactory<SchedulerDbContext> contextFactory,
    DatabaseConnection connection,
    IBackupService backups,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    // Serialises start-up across processes (Admin application and Maintenance Agent).
    private const long AdvisoryLockKey = 0x474D5353; // "GMSS"

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!connection.HasPassword)
        {
            throw new InvalidOperationException(
                $"The database password is not set. Set the {DatabaseOptions.PasswordVariable} environment variable for your user account and restart the application.");
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                $"Could not connect to PostgreSQL at {connection.Options.Host}:{connection.Options.Port} (database {connection.Options.Name}). " +
                "Check that the PostgreSQL service is running and that the database settings and password are correct.", ex);
        }

        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_lock({AdvisoryLockKey})", cancellationToken);
        try
        {
            await MigrateAsync(db, cancellationToken);
            await SeedAsync(db, cancellationToken);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({AdvisoryLockKey})", CancellationToken.None);
        }
    }

    private async Task MigrateAsync(SchedulerDbContext db, CancellationToken cancellationToken)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        if ((await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Any())
        {
            try
            {
                var backup = await backups.CreateBackupAsync(BackupKind.PreMigration, cancellationToken);
                logger.LogInformation("Safety backup {File} taken before applying {Count} migration(s).", backup.FileName, pending.Count);
            }
            catch (BackupException ex)
            {
                // The upgrade still goes ahead: migrations run in a transaction, and a missing pg_dump must not block start-up.
                logger.LogWarning(ex, "The safety backup before migration failed: {Details}", ex.Details);
            }
        }

        logger.LogInformation("Applying database migrations: {Migrations}", string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
    }

    private async Task SeedAsync(SchedulerDbContext db, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var existingKeys = await db.Configuration.Where(c => c.TaskId == null).Select(c => c.Key).ToListAsync(cancellationToken);
        foreach (var definition in SettingDefinitions.All.Where(d => !existingKeys.Contains(d.Key)))
        {
            db.Configuration.Add(ConfigurationSetting.Global(definition.Key, definition.DefaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture), now));
        }

        var providers = await db.LoggingProviders.Select(p => p.ProviderName).ToListAsync(cancellationToken);
        (string Name, LoggingProviderType Type, bool Active)[] defaults =
        [
            (LogProviderNames.Database, LoggingProviderType.Database, true),
            (LogProviderNames.File, LoggingProviderType.File, true),
            (LogProviderNames.Console, LoggingProviderType.Console, false)
        ];
        foreach (var (name, type, active) in defaults.Where(d => !providers.Contains(d.Name)))
        {
            db.LoggingProviders.Add(new LoggingProviderSetting(name, type, SchedulerLogLevel.Info, active));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
