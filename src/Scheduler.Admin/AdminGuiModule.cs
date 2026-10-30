using Microsoft.Extensions.DependencyInjection;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Admin;

/// <summary>
/// The administration GUI itself, listed on the Modules page like any other module. It provides the core pages and
/// their menu entries; the scheduler engine and the database stay part of the host. It cannot be switched off, because
/// the Modules page that switches modules back on is one of its pages.
/// </summary>
public sealed class AdminGuiModule : ISchedulerModule
{
    public const string ModuleName = "Scheduler Admin GUI";

    public string Name => ModuleName;

    public string Description =>
        "The administration application you are using: the dashboard, tasks, queue, monitoring, configuration, modules, backup and help pages.";

    public bool CanBeDisabled => false;

    public IReadOnlyList<ModuleMenuItem> MenuItems { get; } =
    [
        new("Dashboard", "/", "Overview"),
        new("Tasks", "/tasks", "Scheduling"),
        new("Queue", "/queue", "Scheduling"),
        new("Execution history", "/executions", "Monitoring"),
        new("Error log", "/errors", "Monitoring"),
        new("Reports", "/reports", "Monitoring"),
        new("Configuration", "/configuration", "Administration", AdministratorOnly: true),
        new("Modules", "/modules", "Administration"),
        new("Backup and restore", "/backup", "Administration", AdministratorOnly: true),
        new("Help", "/help", "Help")
    ];

    /// <summary>The GUI's services are registered by the Admin application itself (AddAdminServices).</summary>
    public void Register(IServiceCollection services)
    {
    }
}
