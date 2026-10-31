using System.Diagnostics;
using DemoApps.MaintenanceAgent.Data;
using Microsoft.Extensions.Options;

namespace DemoApps.MaintenanceAgent.Services;

public sealed record HealthCheckItem(string Check, bool Ok, string Detail);

public sealed record HealthReport(bool Healthy, string Message, DateTime CheckedAtUtc, IReadOnlyList<HealthCheckItem> Checks);

/// <summary>
/// GET /health (FR-79): the agent's database answers, the agent's drive has at least MinFreeGb free and the agent
/// uses at most MaxMemoryMb of memory. The result is recorded in maintenance.health_check when the database answers.
/// </summary>
public sealed class HealthChecker(MaintenanceStore store, IOptions<AgentOptions> options, TimeProvider clock, ILogger<HealthChecker> logger)
{
    public async Task<HealthReport> CheckAsync(CancellationToken cancellationToken)
    {
        var limits = options.Value;
        var checks = new List<HealthCheckItem>();
        var databaseOk = false;
        try
        {
            var milliseconds = await store.PingAsync(cancellationToken);
            checks.Add(new HealthCheckItem("Database", true, $"Answered in {milliseconds} ms."));
            databaseOk = true;
        }
        catch (Exception ex) when (MaintenanceStore.IsDatabaseError(ex))
        {
            checks.Add(new HealthCheckItem("Database", false, "The database maintenance_demo did not answer: " + ex.Message));
        }

        var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) ?? "/");
        var freeGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
        checks.Add(new HealthCheckItem("Disk", freeGb >= limits.MinFreeGb,
            $"{freeGb:N1} GB free on {drive.Name}; at least {limits.MinFreeGb} GB is required."));

        var memoryMb = Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024;
        checks.Add(new HealthCheckItem("Memory", memoryMb <= limits.MaxMemoryMb,
            $"The agent uses {memoryMb:N0} MB; the limit is {limits.MaxMemoryMb} MB."));

        var failed = checks.Where(c => !c.Ok).ToList();
        var report = new HealthReport(failed.Count == 0,
            failed.Count == 0 ? "All health checks passed." : string.Join(" ", failed.Select(c => c.Detail)),
            clock.GetUtcNow().UtcDateTime, checks);

        if (databaseOk)
        {
            try
            {
                await store.AddHealthCheckAsync(new HealthRecord(report.CheckedAtUtc, report.Healthy, report.Message), new { checks }, cancellationToken);
            }
            catch (Exception ex) when (MaintenanceStore.IsDatabaseError(ex))
            {
                logger.LogWarning(ex, "The health check could not be recorded.");
            }
        }

        return report;
    }
}
