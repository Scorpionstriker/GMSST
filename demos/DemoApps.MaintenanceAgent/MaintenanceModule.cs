using DemoApps.MaintenanceAgent.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.MaintenanceAgent;

/// <summary>The agent's handlers and tasks, packaged like any other module (FR-41, FR-79).</summary>
public sealed class MaintenanceModule : ISchedulerModule
{
    public string Name => "Maintenance Agent";

    public string Description => "Local health checks and temporary-file clean-up, run by the headless Maintenance Agent.";

    public IReadOnlyList<ModuleMenuItem> MenuItems => [];

    public void Register(IServiceCollection services)
    {
        services.AddTransient<ITaskHandler, HealthCheckHandler>();
        services.AddTransient<ITaskHandler, TempFileCleanupHandler>();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(MaintenanceOptions.DefaultRoot);
        var client = services.GetRequiredService<ISchedulerClient>();
        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Maintenance: health check",
            TaskType = HealthCheckHandler.Type,
            Description = "Checks the database, free disk space and memory every five minutes.",
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300,
            StartTimeUtc = DateTime.UtcNow.AddSeconds(10),
            Payload = """{ "minFreeGb": 2, "maxMemoryMb": 512 }""",
            MaxRetries = 0,
            MisfirePolicy = MisfirePolicy.Skip
        }, cancellationToken);
        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Maintenance: temporary-file clean-up",
            TaskType = TempFileCleanupHandler.Type,
            Description = "Deletes files older than a day from the GMSS temporary folder, every night at 03:00 UTC.",
            CronExpression = "0 3 * * *",
            Payload = """{ "olderThanHours": 24, "pattern": "*" }""",
            MisfirePolicy = MisfirePolicy.RunOnce
        }, cancellationToken);
    }
}
