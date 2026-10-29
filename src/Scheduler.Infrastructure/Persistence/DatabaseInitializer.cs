using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Scheduler.Application.Backup;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;
using Scheduler.Infrastructure.Logging;

namespace Scheduler.Infrastructure.Persistence;

/// <summary>Pending migrations were not applied because the safety backup failed (FR-73, WP-5i).</summary>
public sealed class DatabaseUpgradeBlockedException(string reason)
    : InvalidOperationException($"The database upgrade was stopped because the safety backup failed: {reason.TrimEnd('.')}.");

internal sealed class DatabaseInitializer(
    IDbContextFactory<SchedulerDbContext> contextFactory,
    DatabaseConnection connection,
    IBackupService backups,
    IEnumerable<ILoggingProvider> loggingProviders,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    // Serialises start-up across processes, for example two Admin applications started at the same time.
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
            // FR-73 (Must, WP-5i): an upgrade is applied only after a verified safety backup of the scheduler database.
            string? reason;
            try
            {
                var backup = await backups.CreateBackupAsync(BackupKind.PreMigration, [connection.Options.Name], null, cancellationToken);
                reason = backup.Verified ? null : $"the backup file of {backup.FirstUnverified?.Database} could not be verified";
                if (reason is null)
                {
                    logger.LogInformation("Safety backup {Set} taken before applying {Count} migration(s).", backup.SetName, pending.Count);
                }
            }
            catch (BackupException ex)
            {
                logger.LogError(ex, "The safety backup before migration failed: {Details}", ex.Details);
                reason = ex.Message;
            }

            if (reason is not null)
            {
                throw new DatabaseUpgradeBlockedException(reason);
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

        // WP-7 (F-13): a provider added by a module gets its own row, active at Info, so it is listed on the
        // Configuration page and can be switched off there like the built-in ones.
        var known = providers.Concat(defaults.Select(d => d.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in loggingProviders.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !known.Contains(n)))
        {
            if (name.Length is 0 or > LoggingProviderSetting.NameMaxLength)
            {
                logger.LogWarning("Logging provider name '{Name}' is empty or longer than {Max} characters and was not registered.",
                    name, LoggingProviderSetting.NameMaxLength);
                continue;
            }

            db.LoggingProviders.Add(new LoggingProviderSetting(name, LoggingProviderType.Custom, SchedulerLogLevel.Info, true));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
