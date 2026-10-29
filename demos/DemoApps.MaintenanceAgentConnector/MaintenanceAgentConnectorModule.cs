using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.MaintenanceAgentConnector;

/// <summary>
/// Integration method 2 (FR-79, WP-1): the scheduler manages a separate application, the Maintenance Agent, over the
/// loopback address. The module creates two tasks of the built-in Local HTTP Request type; the agent's key is sent as
/// "env:GSST_AGENT_KEY", so it is read from the environment when the task runs and never stored in the database.
/// </summary>
public sealed class MaintenanceAgentConnectorModule : ISchedulerModule
{
    /// <summary>The built-in task type the two tasks use (Scheduler.Engine's LocalHttpRequestHandler).</summary>
    public const string LocalHttpRequestType = "LocalHttpRequest";

    public const string HealthCheckTaskName = "Maintenance: health check";
    public const string CleanupTaskName = "Maintenance: temporary-file clean-up";

    // WP-8h: stable keys, so the tasks can be renamed and are not deleted by accident.
    public const string HealthCheckKey = "maintenance.health-check";
    public const string CleanupKey = "maintenance.temp-cleanup";

    public string Name => "Maintenance Agent connector";

    public string Description => "Manages the separate Maintenance Agent application through Local HTTP Request tasks and shows its status.";

    public IReadOnlyList<ModuleMenuItem> MenuItems { get; } =
    [
        new("Maintenance Agent", "/managed/maintenance-agent", "Managed apps")
    ];

    public void Register(IServiceCollection services)
    {
        services.AddSingleton(sp => new AgentClient(sp.GetService<IConfiguration>()));
        services.AddSingleton<IRestoreGuard, MaintenanceAgentRestoreGuard>();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var agent = services.GetRequiredService<AgentClient>();
        var client = services.GetRequiredService<ISchedulerClient>();
        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = HealthCheckTaskName,
            SystemKey = HealthCheckKey,
            TaskType = LocalHttpRequestType,
            Description = "Asks the Maintenance Agent to check its database, free disk space and memory, every five minutes.",
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300,
            Payload = BuildPayload(agent, "/health", "GET"),
            MaxRetries = 0,
            TimeoutSeconds = 30,
            MisfirePolicy = MisfirePolicy.Skip
        }, cancellationToken);
        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = CleanupTaskName,
            SystemKey = CleanupKey,
            TaskType = LocalHttpRequestType,
            Description = "Asks the Maintenance Agent to delete files older than a day from its temporary folder, every night at 03:00.",
            CronExpression = "0 3 * * *",
            TimeZoneId = LocalZoneId(),
            Payload = BuildPayload(agent, "/cleanup", "POST", """{"olderThanHours":24,"pattern":"*"}"""),
            TimeoutSeconds = 300,
            MisfirePolicy = MisfirePolicy.RunOnce
        }, cancellationToken);
    }

    internal static string BuildPayload(AgentClient agent, string path, string method, string? body = null) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["url"] = agent.BaseUrl + path,
            ["method"] = method,
            ["expectedStatus"] = 200,
            ["headers"] = new Dictionary<string, string> { [AgentClient.KeyHeader] = "env:" + AgentClient.KeyVariable },
            ["notRunningMessage"] = agent.NotRunningMessage,
            ["body"] = body
        }.Where(p => p.Value is not null).ToDictionary());

    private static string LocalZoneId() =>
        TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id
        : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "UTC";
}
