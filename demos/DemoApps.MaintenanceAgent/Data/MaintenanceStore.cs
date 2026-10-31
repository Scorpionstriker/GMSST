using System.Data.Common;
using System.Text.Json;
using Npgsql;

namespace DemoApps.MaintenanceAgent.Data;

public sealed record HealthRecord(DateTime CheckedAtUtc, bool Healthy, string Message);

public sealed record CleanupRecord(DateTime RanAtUtc, bool Succeeded, string Folder, int Deleted, int Skipped, long Bytes, bool DryRun, string Message);

/// <summary>
/// The agent's own data in its database maintenance_demo (WP-1): one row per health check and per clean-up run.
/// The tables are created at start-up by an idempotent script. All SQL is parameterised.
/// </summary>
public sealed class MaintenanceStore(NpgsqlDataSource dataSource)
{
    private const string SchemaSql =
        """
        CREATE SCHEMA IF NOT EXISTS maintenance;
        CREATE TABLE IF NOT EXISTS maintenance.health_check (
            check_id UUID PRIMARY KEY,
            checked_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            healthy BOOLEAN NOT NULL,
            message VARCHAR(1000) NOT NULL,
            details JSONB NOT NULL DEFAULT '{}');
        CREATE INDEX IF NOT EXISTS ix_health_check_time ON maintenance.health_check (checked_at DESC);
        CREATE TABLE IF NOT EXISTS maintenance.cleanup_run (
            run_id UUID PRIMARY KEY,
            ran_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            succeeded BOOLEAN NOT NULL,
            folder VARCHAR(500) NOT NULL,
            deleted_count INTEGER NOT NULL,
            skipped_count INTEGER NOT NULL,
            deleted_bytes BIGINT NOT NULL,
            dry_run BOOLEAN NOT NULL,
            message VARCHAR(1000) NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_cleanup_run_time ON maintenance.cleanup_run (ran_at DESC);
        """;

    private int _schemaReady;

    /// <summary>Creates the tables once. Called at start-up and again before each write while the database was unavailable.</summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _schemaReady) == 1)
        {
            return;
        }

        await using var command = dataSource.CreateCommand(SchemaSql);
        await command.ExecuteNonQueryAsync(cancellationToken);
        Volatile.Write(ref _schemaReady, 1);
    }

    /// <summary>Checks that the database answers; returns the round trip in milliseconds.</summary>
    public async Task<long> PingAsync(CancellationToken cancellationToken = default)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await using var command = dataSource.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(cancellationToken);
        return watch.ElapsedMilliseconds;
    }

    public async Task AddHealthCheckAsync(HealthRecord record, object details, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "INSERT INTO maintenance.health_check (check_id, checked_at, healthy, message, details) VALUES (@id, @at, @healthy, @message, CAST(@details AS jsonb))");
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("at", record.CheckedAtUtc);
        command.Parameters.AddWithValue("healthy", record.Healthy);
        command.Parameters.AddWithValue("message", Truncate(record.Message));
        command.Parameters.AddWithValue("details", JsonSerializer.Serialize(details));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddCleanupRunAsync(CleanupRecord record, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "INSERT INTO maintenance.cleanup_run (run_id, ran_at, succeeded, folder, deleted_count, skipped_count, deleted_bytes, dry_run, message) " +
            "VALUES (@id, @at, @ok, @folder, @deleted, @skipped, @bytes, @dry, @message)");
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("at", record.RanAtUtc);
        command.Parameters.AddWithValue("ok", record.Succeeded);
        command.Parameters.AddWithValue("folder", Truncate(record.Folder, 500));
        command.Parameters.AddWithValue("deleted", record.Deleted);
        command.Parameters.AddWithValue("skipped", record.Skipped);
        command.Parameters.AddWithValue("bytes", record.Bytes);
        command.Parameters.AddWithValue("dry", record.DryRun);
        command.Parameters.AddWithValue("message", Truncate(record.Message));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<HealthRecord?> GetLastHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "SELECT checked_at, healthy, message FROM maintenance.health_check ORDER BY checked_at DESC LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new HealthRecord(reader.GetDateTime(0), reader.GetBoolean(1), reader.GetString(2)) : null;
    }

    public async Task<CleanupRecord?> GetLastCleanupRunAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "SELECT ran_at, succeeded, folder, deleted_count, skipped_count, deleted_bytes, dry_run, message FROM maintenance.cleanup_run ORDER BY ran_at DESC LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CleanupRecord(reader.GetDateTime(0), reader.GetBoolean(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.GetInt64(5), reader.GetBoolean(6), reader.GetString(7))
            : null;
    }

    /// <summary>True when the exception means the database could not be reached or used.</summary>
    public static bool IsDatabaseError(Exception ex) => ex is DbException or TimeoutException or System.Net.Sockets.SocketException;

    private static string Truncate(string text, int length = 1000) => text.Length <= length ? text : text[..length];
}
