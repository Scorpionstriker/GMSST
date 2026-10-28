using System.Reflection;
using DemoApps.InventoryMonitor;
using Scheduler.Extensibility.Contracts;
using Scheduler.Modules.Security;

namespace Scheduler.Admin;

/// <summary>
/// The modules loaded into the Admin application (FR-41). To remove a module, delete its line here and
/// its project reference; its task types, services, pages and menu entries disappear together.
/// </summary>
public static class ModuleCatalog
{
    public static readonly ISchedulerModule[] Modules =
    [
        new SecurityModule(),
        new InventoryModule()
    ];

    /// <summary>Module assemblies searched by the router for pages.</summary>
    public static IEnumerable<Assembly> PageAssemblies =>
        Modules.Select(m => m.GetType().Assembly).Distinct();
}
