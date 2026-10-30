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

    /// <summary>One sentence describing the module on the Modules page.</summary>
    string? Description => null;

    /// <summary>Menu entries this module adds to the Admin application. Empty for headless modules.</summary>
    IReadOnlyList<ModuleMenuItem> MenuItems { get; }

    void Register(IServiceCollection services);

    /// <summary>
    /// Runs once at start-up, after the scheduler database is ready and before the engine starts. Use it
    /// to apply the module's own migrations or to create its tasks through <see cref="ISchedulerClient"/>.
    /// </summary>
    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Whether an administrator may switch the module off on the Modules page. A switched-off module stays installed
    /// but is disconnected: its menu entries and pages are hidden and its task types do not run. Return false for a
    /// module the application cannot work without, such as the one that provides sign-in.
    /// </summary>
    bool CanBeDisabled => true;

    /// <summary>
    /// Runs when an administrator switches the module on or off while the application runs, after
    /// <see cref="InitializeAsync"/> when it is switched on. Use it to pause or resume work the scheduler cannot tell
    /// belongs to the module, such as tasks of a built-in task type.
    /// </summary>
    Task OnEnabledChangedAsync(IServiceProvider services, bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>A menu entry contributed by a module. Pages are discovered from the module's assembly.</summary>
/// <param name="Title">Menu text.</param>
/// <param name="Route">Page route, for example "/inventory/products".</param>
/// <param name="Group">Optional menu group, for example "Inventory".</param>
/// <param name="AdministratorOnly">Hide the entry from Viewer users (FR-49).</param>
public sealed record ModuleMenuItem(string Title, string Route, string? Group = null, bool AdministratorOnly = false);
