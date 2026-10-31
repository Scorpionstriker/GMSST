using System.Reflection;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Modules;

/// <summary>
/// Which installed modules are switched on. A switched-off module stays installed but is disconnected: its menu
/// entries and pages are hidden, its task types are not queued or run, and its start-up is skipped. Every module is
/// on by default. The choice is kept in the user settings file of this computer, so it survives a restart.
/// </summary>
public interface IModuleSwitches
{
    /// <summary>Names of the modules that are switched off.</summary>
    IReadOnlyCollection<string> DisabledModules { get; }

    bool IsEnabled(string moduleName);

    /// <summary>
    /// False when <paramref name="assembly"/> belongs to a switched-off module. Assemblies of the core, such as the
    /// built-in task handlers, are always on.
    /// </summary>
    bool IsEnabled(Assembly assembly);

    /// <summary>Raised after a module is switched on or off, so menus and pages can refresh.</summary>
    event EventHandler? Changed;

    /// <summary>Switches the module on or off and saves the choice. Use SchedulerHost.SetModuleEnabledAsync, which also starts the module.</summary>
    Task SetEnabledAsync(string moduleName, bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>Keeps the names of the switched-off modules. Implemented by the user settings file.</summary>
public interface IModuleStateStore
{
    IReadOnlyCollection<string> GetDisabledModules();

    Task SaveDisabledModulesAsync(IReadOnlyCollection<string> moduleNames, CancellationToken cancellationToken = default);
}

/// <summary>Keeps the choice in memory only; used by hosts without a user settings file, such as the tests.</summary>
internal sealed class InMemoryModuleStateStore : IModuleStateStore
{
    private IReadOnlyCollection<string> _disabled = [];

    public IReadOnlyCollection<string> GetDisabledModules() => _disabled;

    public Task SaveDisabledModulesAsync(IReadOnlyCollection<string> moduleNames, CancellationToken cancellationToken = default)
    {
        _disabled = moduleNames.ToArray();
        return Task.CompletedTask;
    }
}

internal sealed class ModuleSwitches : IModuleSwitches
{
    private readonly IModuleStateStore _store;
    private readonly IReadOnlyList<ISchedulerModule> _modules;
    private readonly Lock _gate = new();
    private HashSet<string> _disabled;

    public ModuleSwitches(IModuleStateStore store, IEnumerable<ISchedulerModule> modules)
    {
        _store = store;
        _modules = modules.ToList();

        // A name in the file that matches no installed module is ignored; a module that must stay on is never off.
        _disabled = new HashSet<string>(
            store.GetDisabledModules().Where(name => _modules.Any(m => m.CanBeDisabled && Same(m.Name, name))),
            StringComparer.OrdinalIgnoreCase);
    }

    public event EventHandler? Changed;

    public IReadOnlyCollection<string> DisabledModules
    {
        get
        {
            lock (_gate)
            {
                return _disabled.ToArray();
            }
        }
    }

    public bool IsEnabled(string moduleName)
    {
        lock (_gate)
        {
            return !_disabled.Contains(moduleName);
        }
    }

    public bool IsEnabled(Assembly assembly) =>
        _modules.Where(m => m.GetType().Assembly == assembly).All(m => IsEnabled(m.Name));

    public async Task SetEnabledAsync(string moduleName, bool enabled, CancellationToken cancellationToken = default)
    {
        var module = _modules.FirstOrDefault(m => Same(m.Name, moduleName))
            ?? throw new ArgumentException($"No module named '{moduleName}' is installed.", nameof(moduleName));
        if (!enabled && !module.CanBeDisabled)
        {
            throw new InvalidOperationException($"The module '{module.Name}' cannot be switched off.");
        }

        string[] snapshot;
        lock (_gate)
        {
            var changed = enabled ? _disabled.Remove(module.Name) : _disabled.Add(module.Name);
            if (!changed)
            {
                return;
            }

            snapshot = _disabled.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        await _store.SaveDisabledModulesAsync(snapshot, cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
