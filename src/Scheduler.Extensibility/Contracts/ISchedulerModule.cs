using Microsoft.Extensions.DependencyInjection;

namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// Entry point of a module. A module registers its task handlers, providers and services, and can
/// contribute pages and menu entries to the Admin application. Removing the module removes all of
/// them together (FR-41).
/// </summary>
public interface ISchedulerModule
{
    /// <summary>Display name shown on the Modules page (FR-60).</summary>
    string Name { get; }

    /// <summary>Menu entries this module adds to the Admin application. Empty for headless modules.</summary>
    IReadOnlyList<ModuleMenuItem> MenuItems { get; }

    void Register(IServiceCollection services);
}

/// <summary>A menu entry contributed by a module. Pages are discovered from the module's assembly.</summary>
/// <param name="Title">Menu text.</param>
/// <param name="Route">Page route, for example "/inventory/products".</param>
/// <param name="Group">Optional menu group, for example "Inventory".</param>
/// <param name="AdministratorOnly">Hide the entry from Viewer users (FR-49).</param>
public sealed record ModuleMenuItem(string Title, string Route, string? Group = null, bool AdministratorOnly = false);
