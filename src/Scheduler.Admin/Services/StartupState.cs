using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Npgsql;
using Scheduler.Engine.Hosting;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Admin.Services;

public enum StartupPhase
{
    Starting,
    Ready,
    Failed
}

/// <summary>
/// Starts the scheduler (database migration, modules, engine) once, when the window opens, and tells the layout
/// whether the pages can be shown yet. A failure is shown as a plain-language message with a Try again button,
/// instead of a crash (FR-62, NFR-12).
/// </summary>
public sealed class StartupState(SchedulerHost host, DatabaseConnection connection, ILogger<StartupState> logger)
{
    private readonly Lock _gate = new();
    private Task? _start;

    public StartupPhase Phase { get; private set; } = StartupPhase.Starting;

    public string? Error { get; private set; }

    public event Action? Changed;

    /// <summary>Starts the scheduler if it has not been started yet; repeated calls return the same task.</summary>
    public Task EnsureStartedAsync()
    {
        lock (_gate)
        {
            return _start ??= StartAsync();
        }
    }

    /// <summary>Tries again after a failed start, for example once PostgreSQL is running.</summary>
    public Task RetryAsync()
    {
        lock (_gate)
        {
            if (Phase == StartupPhase.Failed)
            {
                _start = StartAsync();
            }

            return _start ?? StartAsync();
        }
    }

    private async Task StartAsync()
    {
        SetPhase(StartupPhase.Starting, null);
        if (!connection.HasPassword)
        {
            SetPhase(StartupPhase.Failed,
                $"The database password is not set. Set the {DatabaseOptions.PasswordVariable} environment variable for your user account, " +
                "then close and reopen the Admin application. See the README for the steps.");
            return;
        }

        try
        {
            await Task.Run(() => host.StartAsync());
            SetPhase(StartupPhase.Ready, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The scheduler failed to start.");
            SetPhase(StartupPhase.Failed, Describe(ex));
        }
    }

    private string Describe(Exception ex)
    {
        var options = connection.Options;
        if (Find<DatabaseUpgradeBlockedException>(ex) is { } blocked)
        {
            // FR-73: shown as it is, with how to fix the usual cause.
            return blocked.Message + " Check the PostgreSQL tools folder (Paths:PostgresBinFolder in the user settings file), then start again.";
        }

        return Find<PostgresException>(ex) switch
        {
            { SqlState: "28P01" } => $"PostgreSQL rejected the password for user '{options.Username}'. Check {DatabaseOptions.PasswordVariable}.",
            { SqlState: "3D000" } => $"The database '{options.Name}' does not exist. Create it with database\\setup_windows.ps1 (or database/create_database.sql), then try again.",
            { SqlState: "28000" } => $"User '{options.Username}' is not allowed to connect to '{options.Name}'. Check the database permissions.",
            { } postgres => $"The database reported an error during start-up: {postgres.MessageText}",
            null when Find<SocketException>(ex) is not null || ex is NpgsqlException or TimeoutException =>
                $"PostgreSQL cannot be reached at {options.Host}:{options.Port}. Check that the PostgreSQL service is running, then try again.",
            _ => "The scheduler could not start. The details have been written to the log file."
        };
    }

    private static T? Find<T>(Exception? ex) where T : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is T match)
            {
                return match;
            }
        }

        return null;
    }

    private void SetPhase(StartupPhase phase, string? error)
    {
        Phase = phase;
        Error = error;
        Changed?.Invoke();
    }
}
