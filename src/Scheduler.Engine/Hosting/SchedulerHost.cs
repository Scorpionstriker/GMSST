using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scheduler.Application.Backup;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Cron;
using Scheduler.Application.Modules;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Enums;
using Scheduler.Engine.Handlers;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.Hosting;

/// <summary>Stable keys of the built-in tasks (WP-8h).</summary>
public static class SystemKeys
{
    public const string DatabaseBackup = "builtin.database-backup";
    public const string LogCleanup = "builtin.log-cleanup";
}

/// <summary>The start-up result of one module, shown on the Modules page (FR-60).</summary>
/// <param name="Enabled">False when an administrator has switched the module off; it is then not started.</param>
/// <param name="CanBeDisabled">False for a module the application cannot work without, such as sign-in.</param>
public sealed record ModuleStatus(string Name, string? Description, string Assembly, bool Initialized, string? Error, bool Enabled = true,
    bool CanBeDisabled = true);

/// <summary>The outcome of switching a module on or off.</summary>
/// <param name="Status">The module's new status.</param>
/// <param name="ApplicationMessage">What happened to the module's application, when it was stopped or launched with it; otherwise null.</param>
/// <param name="ApplicationFailed">True when stopping or launching the application failed.</param>
public sealed record ModuleSwitchResult(ModuleStatus Status, string? ApplicationMessage, bool ApplicationFailed);

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
    IModuleSwitches switches,
    IEngineSignal signal,
    ISchedulerLogger log,
    ILogger<SchedulerHost> logger)
{
    private readonly List<ModuleStatus> _modules = [];
    private readonly SemaphoreSlim _moduleGate = new(1, 1);
    private readonly HashSet<string> _stoppedBySwitch = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ModuleStatus> Modules
    {
        get
        {
            lock (_modules)
            {
                return _modules.ToArray();
            }
        }
    }

    public SchedulerEngine Engine => engine;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken);

        // Fails fast with a clear message when two handlers claim the same task type.
        logger.LogInformation("Task types available on this host: {Types}", string.Join(", ", registry.TaskTypeNames));

        var statuses = new List<ModuleStatus>();
        foreach (var module in services.GetServices<ISchedulerModule>())
        {
            if (!switches.IsEnabled(module.Name))
            {
                // A switched-off module is not started; switching it on later starts it (SetModuleEnabledAsync).
                logger.LogInformation("Module {Module} is switched off and was not started.", module.Name);
                statuses.Add(StatusOf(module, initialized: false, error: null));
                continue;
            }

            statuses.Add(await InitializeModuleAsync(module, cancellationToken));
        }

        lock (_modules)
        {
            _modules.Clear();
            _modules.AddRange(statuses);
        }

        if (options.RegisterBuiltInHandlers && options.CreateBuiltInTasks)
        {
            await EnsureBuiltInTasksAsync(cancellationToken);
        }

        await engine.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => engine.StopAsync(cancellationToken);

    /// <summary>
    /// Switches a module on or off while the application runs, and saves the choice. Switching off hides the module's
    /// menu entries and pages, stops its task types from being queued or run (their tasks wait until the module is
    /// switched on again) and stops the module's application (<see cref="IManagedApplication"/>) if it runs. Switching on
    /// starts the module if it was not started yet, and launches its application again if switching off stopped it.
    /// </summary>
    public async Task<ModuleSwitchResult> SetModuleEnabledAsync(string moduleName, bool enabled, string? changedBy = null,
        CancellationToken cancellationToken = default)
    {
        var module = services.GetServices<ISchedulerModule>().FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No module named '{moduleName}' is installed.", nameof(moduleName));

        await _moduleGate.WaitAsync(cancellationToken);
        try
        {
            var application = services.GetServices<IManagedApplication>()
                .FirstOrDefault(a => string.Equals(a.ModuleName, module.Name, StringComparison.OrdinalIgnoreCase));
            ManagedApplicationResult? applicationResult = null;
            if (!enabled && application is not null && (await application.GetStatusAsync(cancellationToken)).IsRunning)
            {
                // Disconnecting the module also stops what it manages; it is launched again when the module is switched on.
                applicationResult = await application.StopAsync(cancellationToken);
                if (applicationResult.Succeeded)
                {
                    _stoppedBySwitch.Add(module.Name);
                }
            }

            await switches.SetEnabledAsync(module.Name, enabled, cancellationToken);

            var status = Modules.FirstOrDefault(m => m.Name == module.Name) ?? StatusOf(module, initialized: false, error: null);
            if (enabled && !status.Initialized)
            {
                status = await InitializeModuleAsync(module, cancellationToken);
            }
            else
            {
                status = status with { Enabled = enabled };
            }

            try
            {
                await module.OnEnabledChangedAsync(services, enabled, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Module {Module} failed while being switched {State}.", module.Name, enabled ? "on" : "off");
                status = status with { Error = ex.Message };
            }

            lock (_modules)
            {
                var index = _modules.FindIndex(m => m.Name == module.Name);
                if (index >= 0)
                {
                    _modules[index] = status;
                }
                else
                {
                    _modules.Add(status);
                }
            }

            await log.LogAsync(SchedulerLogLevel.Info, LogSources.Admin,
                $"Module '{module.Name}' was switched {(enabled ? "on" : "off")} by {changedBy ?? "the local user"}.",
                properties: new Dictionary<string, object?> { ["module"] = module.Name, ["enabled"] = enabled }, cancellationToken: cancellationToken);

            if (enabled && application is not null && _stoppedBySwitch.Remove(module.Name) && status.Initialized)
            {
                applicationResult = await application.StartAsync(cancellationToken);
            }

            // The engine re-reads the task types it may run on its next cycle.
            signal.Wake();
            return new ModuleSwitchResult(status, applicationResult?.Message, applicationResult is { Succeeded: false });
        }
        finally
        {
            _moduleGate.Release();
        }
    }

    private async Task<ModuleStatus> InitializeModuleAsync(ISchedulerModule module, CancellationToken cancellationToken)
    {
        try
        {
            await module.InitializeAsync(services, cancellationToken);
            return StatusOf(module, initialized: true, error: null);
        }
        catch (Exception ex)
        {
            // A failing module is reported on the Modules page; the scheduler itself keeps working.
            logger.LogError(ex, "Module {Module} failed to initialise.", module.Name);
            return StatusOf(module, initialized: false, error: ex.Message);
        }
    }

    private ModuleStatus StatusOf(ISchedulerModule module, bool initialized, string? error) =>
        new(module.Name, module.Description, module.GetType().Assembly.GetName().Name ?? "", initialized, error, switches.IsEnabled(module.Name),
            module.CanBeDisabled);

    /// <summary>The Database Backup task runs daily at 01:00 and Log Clean-up at 02:00, local time (Table 8.3).</summary>
    private async Task EnsureBuiltInTasksAsync(CancellationToken cancellationToken)
    {
        var zone = TimeZones.LocalId();
        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Database Backup",
            SystemKey = SystemKeys.DatabaseBackup,
            TaskType = DatabaseBackupHandler.Type,
            Description = "Built-in daily backup of the scheduler, Inventory and Maintenance Agent databases.",
            CronExpression = "0 1 * * *",
            Payload = System.Text.Json.JsonSerializer.Serialize(new { databases = services.GetService<IBackupService>()?.KnownDatabases ?? [] }),
            TimeZoneId = zone,
            MisfirePolicy = MisfirePolicy.RunOnce,
            TimeoutSeconds = 1800
        }, cancellationToken);
        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Log Clean-up",
            SystemKey = SystemKeys.LogCleanup,
            TaskType = LogCleanupHandler.Type,
            Description = "Built-in daily removal of old executions, errors, log entries, log files and audit records.",
            CronExpression = "0 2 * * *",
            TimeZoneId = zone,
            MisfirePolicy = MisfirePolicy.RunOnce,
            TimeoutSeconds = 600
        }, cancellationToken);
    }
}
