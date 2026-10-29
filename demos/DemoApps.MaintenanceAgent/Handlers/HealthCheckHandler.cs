using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.MaintenanceAgent.Handlers;

/// <summary>
/// Local health check (FR-79): the database answers, the disk holding <c>path</c> has at least <c>minFreeGb</c> free,
/// and this process uses less than <c>maxMemoryMb</c>. The run fails when any check fails, so problems show up in the
/// Admin application's Error Log and on the dashboard.
/// </summary>
public sealed class HealthCheckHandler(DbDataSource database) : ITaskHandler
{
    public const string Type = "HealthCheck";

    public string TaskType => Type;
    public string DisplayName => "Maintenance: health check";
    public string Description => "Checks the database connection, free disk space and the agent's memory use.";
    public string PayloadExample => """{ "minFreeGb": 2, "maxMemoryMb": 512, "path": "" }""";

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        var minFreeGb = context.GetInt32("minFreeGb") ?? 2;
        var maxMemoryMb = context.GetInt32("maxMemoryMb") ?? 512;
        if (minFreeGb < 0 || maxMemoryMb <= 0)
        {
            return TaskResult.Failure("\"minFreeGb\" must be 0 or more and \"maxMemoryMb\" more than 0.");
        }

        var checks = new List<object>();
        var failures = new List<string>();

        var watch = Stopwatch.StartNew();
        try
        {
            await using var command = database.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(context.CancellationToken);
            checks.Add(new { check = "Database", ok = true, milliseconds = watch.ElapsedMilliseconds });
        }
        catch (DbException ex)
        {
            failures.Add("The database did not answer: " + ex.Message);
            checks.Add(new { check = "Database", ok = false });
        }

        var path = context.GetString("path") is { Length: > 0 } p ? p : AppContext.BaseDirectory;
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? "/");
        var freeGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
        checks.Add(new { check = "Disk", drive = drive.Name, freeGb = Math.Round(freeGb, 1), ok = freeGb >= minFreeGb });
        if (freeGb < minFreeGb)
        {
            failures.Add($"Only {freeGb:N1} GB free on {drive.Name}; at least {minFreeGb} GB is required.");
        }

        var memoryMb = Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024;
        checks.Add(new { check = "Memory", workingSetMb = Math.Round(memoryMb), ok = memoryMb <= maxMemoryMb });
        if (memoryMb > maxMemoryMb)
        {
            failures.Add($"The agent uses {memoryMb:N0} MB of memory; the limit is {maxMemoryMb} MB.");
        }

        context.Logger.LogInformation("Health check: {Passed} of {Total} checks passed.", checks.Count - failures.Count, checks.Count);
        return failures.Count == 0
            ? TaskResult.Success("All health checks passed.", new { checks })
            : TaskResult.Failure(string.Join(" ", failures), new { checks });
    }
}
