using System.Reflection;
using DemoApps.InventoryMonitor;
using DemoApps.MaintenanceAgentConnector;
using Scheduler.Extensibility.Contracts;
using Scheduler.Modules.Security;

namespace Scheduler.Admin;

/// <summary>
/// The modules loaded into the Admin application (FR-41). To remove a module, delete its line here and
/// its project reference; its task types, services, pages and menu entries disappear together. The first entry is the
/// Admin GUI itself, which provides the core pages and must stay.
/// </summary>
public static class ModuleCatalog
{
    public static readonly ISchedulerModule[] Modules =
    [
        new AdminGuiModule(),
        new SecurityModule(),
        new InventoryModule(),
        new MaintenanceAgentConnectorModule()
    ];

    /// <summary>Module assemblies searched by the router for pages, besides the Admin application's own.</summary>
    public static IEnumerable<Assembly> PageAssemblies =>
        Modules.Select(m => m.GetType().Assembly).Where(a => a != typeof(ModuleCatalog).Assembly).Distinct();
}
