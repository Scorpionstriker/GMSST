using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Cron;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Enums;
using Scheduler.Engine.Handlers;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.Hosting;

/// <summary>The start-up result of one module, shown on the Modules page (FR-60).</summary>
public sealed record ModuleStatus(string Name, string? Description, string Assembly, bool Initialized, string? Error);

/// <summary>
/// Starts everything in the right order: database migrations and seed data, task type check, module
/// initialisation, built-in tasks, then the engine. Used by the Admin application (from the MAUI window
/// lifetime, which does not run hosted services) and by <see cref="EngineHostedService"/> in console hosts.
/// </summary>
public sealed class SchedulerHost(
    IServiceProvider services,
    IDatabaseInitializer database,
    ITaskTypeRegistry registry,
    ISchedulerClient client,
    SchedulerEngine engine,
    EngineOptions options,
    ILogger<SchedulerHost> logger)
{
    private readonly List<ModuleStatus> _modules = [];

    public IReadOnlyList<ModuleStatus> Modules => _modules;

    public SchedulerEngine Engine => engine;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken);

        // Fails fast with a clear message when two handlers claim the same task type.
        logger.LogInformation("Task types available on this host: {Types}", string.Join(", ", registry.TaskTypeNames));

        _modules.Clear();
        foreach (var module in services.GetServices<ISchedulerModule>())
        {
            try
            {
                await module.InitializeAsync(services, cancellationToken);
                _modules.Add(new ModuleStatus(module.Name, module.Description, module.GetType().Assembly.GetName().Name ?? "", true, null));
            }
            catch (Exception ex)
            {
                // A failing module is reported on the Modules page; the scheduler itself keeps working.
                logger.LogError(ex, "Module {Module} failed to initialise.", module.Name);
                _modules.Add(new ModuleStatus(module.Name, module.Description, module.GetType().Assembly.GetName().Name ?? "", false, ex.Message));
            }
        }

        if (options.RegisterBuiltInHandlers && options.CreateBuiltInTasks)
        {
            await EnsureBuiltInTasksAsync(cancellationToken);
        }

        await engine.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => engine.StopAsync(cancellationToken);

    /// <summary>The Database Backup task runs daily at 01:00 and Log Clean-up at 02:00, local time (Table 8.3).</summary>
    private async Task EnsureBuiltInTasksAsync(CancellationToken cancellationToken)
    {
        var zone = TimeZones.LocalId();
        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Database Backup",
            TaskType = DatabaseBackupHandler.Type,
            Description = "Built-in daily backup of the scheduler database.",
            CronExpression = "0 1 * * *",
            TimeZoneId = zone,
            MisfirePolicy = MisfirePolicy.RunOnce,
            TimeoutSeconds = 1800
        }, cancellationToken);
        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Log Clean-up",
            TaskType = LogCleanupHandler.Type,
            Description = "Built-in daily removal of old executions, errors and log entries.",
            CronExpression = "0 2 * * *",
            TimeZoneId = zone,
            MisfirePolicy = MisfirePolicy.RunOnce,
            TimeoutSeconds = 600
        }, cancellationToken);
    }
}
