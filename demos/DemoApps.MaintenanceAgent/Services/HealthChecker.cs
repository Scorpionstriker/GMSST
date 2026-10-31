using System.Diagnostics;
using DemoApps.MaintenanceAgent.Data;
using Microsoft.Extensions.Options;

namespace DemoApps.MaintenanceAgent.Services;

public sealed record HealthCheckItem(string Check, bool Ok, string Detail, long? Milliseconds = null);

public sealed record HealthReport(bool Healthy, string Message, DateTime CheckedAtUtc, IReadOnlyList<HealthCheckItem> Checks);

/// <summary>
/// GET /health (FR-79): checks the maintenance, scheduler and inventory databases, the agent's drive and its memory.
/// Results are recorded in maintenance.health_check when the agent's own database answers.
/// </summary>
public sealed class HealthChecker(MaintenanceStore store, HealthDatabaseChecker databases, IOptions<AgentOptions> options,
    TimeProvider clock, ILogger<HealthChecker> logger)
{
    public async Task<HealthReport> CheckAsync(CancellationToken cancellationToken)
    {
        var limits = options.Value;
        var checks = new List<HealthCheckItem>();
        try
        {
            var milliseconds = await store.PingAsync(cancellationToken);
            checks.Add(new HealthCheckItem("Maintenance database", true, $"Connection answered in {milliseconds} ms.", milliseconds));
        }
        catch (Exception ex) when (MaintenanceStore.IsDatabaseError(ex))
        {
            checks.Add(new HealthCheckItem("Maintenance database", false, "Connection failed: " + ex.Message));
        }

        checks.AddRange(await databases.CheckAsync(cancellationToken));

        var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) ?? "/");
        var freeGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
        checks.Add(new HealthCheckItem("Disk", freeGb >= limits.MinFreeGb,
            $"{freeGb:N1} GB free on {drive.Name}; at least {limits.MinFreeGb} GB is required."));

        var memoryMb = Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024;
        checks.Add(new HealthCheckItem("Memory", memoryMb <= limits.MaxMemoryMb,
            $"The agent uses {memoryMb:N0} MB; the limit is {limits.MaxMemoryMb} MB."));

        var failed = checks.Where(c => !c.Ok).ToList();
        var report = new HealthReport(failed.Count == 0,
            string.Join("; ", checks.Select(c => $"{c.Check}: {(c.Ok ? "healthy" : "unhealthy")} - {c.Detail}")),
            clock.GetUtcNow().UtcDateTime, checks);

        try
        {
            await store.AddHealthCheckAsync(new HealthRecord(report.CheckedAtUtc, report.Healthy, report.Message), new { checks }, cancellationToken);
        }
        catch (Exception ex) when (MaintenanceStore.IsDatabaseError(ex))
        {
            logger.LogWarning(ex, "The health check could not be recorded in the maintenance database.");
        }

        logger.LogInformation("Health check completed: {Summary}", report.Message);
        return report;
    }
}

public sealed class HealthDatabaseChecker(Npgsql.NpgsqlDataSource schedulerDatabase, Npgsql.NpgsqlDataSource inventoryDatabase) : IAsyncDisposable
{
    public async Task<IReadOnlyList<HealthCheckItem>> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = new List<HealthCheckItem>(2);
        await CheckAsync("Scheduler database", schedulerDatabase, checks, cancellationToken);
        await CheckAsync("Inventory database", inventoryDatabase, checks, cancellationToken);
        return checks;
    }

    private static async Task CheckAsync(string name, Npgsql.NpgsqlDataSource database, ICollection<HealthCheckItem> checks,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await using var command = database.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            checks.Add(new HealthCheckItem(name, true, $"Connection answered in {watch.ElapsedMilliseconds} ms.",
                watch.ElapsedMilliseconds));
        }
        catch (Exception ex) when (MaintenanceStore.IsDatabaseError(ex))
        {
            checks.Add(new HealthCheckItem(name, false, $"Connection failed: {ex.Message}", watch.ElapsedMilliseconds));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await schedulerDatabase.DisposeAsync();
        await inventoryDatabase.DisposeAsync();
    }
}
