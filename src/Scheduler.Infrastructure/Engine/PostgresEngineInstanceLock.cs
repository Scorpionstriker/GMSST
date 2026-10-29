using Microsoft.Extensions.Logging;
using Npgsql;
using Scheduler.Application.Common;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Engine;

/// <summary>
/// The single-engine rule (WP-1): a session-level PostgreSQL advisory lock (pg_try_advisory_lock) taken on a
/// dedicated, non-pooled connection that stays open while the engine runs. The lock ends with the connection, so a
/// crashed engine never blocks the next start. Advisory locks belong to one database, so engines on different
/// databases (for example the test databases) do not block each other.
/// </summary>
internal sealed class PostgresEngineInstanceLock(DatabaseConnection connection, TimeProvider clock, ILogger<PostgresEngineInstanceLock> logger)
    : IEngineInstanceLock, IAsyncDisposable
{
    // "GMSSENG1". Different from the key DatabaseInitializer uses to serialise migrations.
    internal const long LockKey = 0x474D5353_454E4731;

    private const string StartSql =
        """
        INSERT INTO scheduler.engine_instance (instance_id, host_name, process_id, started_at, heartbeat_at)
        VALUES (1, @host, @pid, @now, @now)
        ON CONFLICT (instance_id) DO UPDATE
        SET host_name = EXCLUDED.host_name, process_id = EXCLUDED.process_id,
            started_at = EXCLUDED.started_at, heartbeat_at = EXCLUDED.heartbeat_at
        """;

    private const string UpsertSql =
        """
        INSERT INTO scheduler.engine_instance (instance_id, host_name, process_id, started_at, heartbeat_at)
        VALUES (1, @host, @pid, @now, @now)
        ON CONFLICT (instance_id) DO UPDATE
        SET host_name = EXCLUDED.host_name, process_id = EXCLUDED.process_id, heartbeat_at = EXCLUDED.heartbeat_at
        """;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private NpgsqlConnection? _connection;
    private string? _hostName;

    public async Task<EngineLockResult> TryAcquireAsync(string hostName, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { State: System.Data.ConnectionState.Open })
            {
                return EngineLockResult.Taken;
            }

            await CloseAsync();
            var lockConnection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connection.ConnectionString)
            {
                Pooling = false,
                ApplicationName = "GSST engine lock",
                KeepAlive = 30
            }.ConnectionString);
            await lockConnection.OpenAsync(cancellationToken);

            await using (var tryLock = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", lockConnection))
            {
                tryLock.Parameters.AddWithValue("key", LockKey);
                if (await tryLock.ExecuteScalarAsync(cancellationToken) is not true)
                {
                    var holder = await ReadHolderAsync(lockConnection, cancellationToken);
                    await lockConnection.DisposeAsync();
                    return holder;
                }
            }

            var now = clock.GetUtcNow().UtcDateTime;
            await using (var upsert = new NpgsqlCommand(StartSql, lockConnection))
            {
                upsert.Parameters.AddWithValue("host", hostName);
                upsert.Parameters.AddWithValue("pid", Environment.ProcessId);
                upsert.Parameters.AddWithValue("now", now);
                await upsert.ExecuteNonQueryAsync(cancellationToken);
            }

            _connection = lockConnection;
            _hostName = hostName;
            return EngineLockResult.Taken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not { State: System.Data.ConnectionState.Open })
            {
                return false;
            }

            // An upsert, because a restore of the scheduler database replaces the row with the one in the backup.
            await using var command = new NpgsqlCommand(UpsertSql, _connection);
            var now = clock.GetUtcNow().UtcDateTime;
            command.Parameters.AddWithValue("host", _hostName ?? string.Empty);
            command.Parameters.AddWithValue("pid", Environment.ProcessId);
            command.Parameters.AddWithValue("now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            // The connection broke, so PostgreSQL has released the lock; the engine must take it again.
            logger.LogWarning(ex, "The engine lock connection was lost.");
            await CloseAsync();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { State: System.Data.ConnectionState.Open })
            {
                try
                {
                    await using var command = new NpgsqlCommand(
                        "DELETE FROM scheduler.engine_instance WHERE instance_id = 1 AND host_name = @host; SELECT pg_advisory_unlock(@key);",
                        _connection);
                    command.Parameters.AddWithValue("host", _hostName ?? string.Empty);
                    command.Parameters.AddWithValue("key", LockKey);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
                {
                    logger.LogWarning(ex, "The engine lock could not be released cleanly; closing its connection releases it.");
                }
            }

            await CloseAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync();

    private static async Task<EngineLockResult> ReadHolderAsync(NpgsqlConnection lockConnection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT host_name, started_at FROM scheduler.engine_instance WHERE instance_id = 1", lockConnection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new EngineLockResult(false, reader.GetString(0), reader.GetDateTime(1))
            : new EngineLockResult(false);
    }

    private async Task CloseAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
