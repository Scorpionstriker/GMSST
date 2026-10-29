using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Tasks;

/// <summary>A task type registered by a handler: what the task editor offers (FR-06) and what this host can run (FR-25).</summary>
public sealed record TaskTypeInfo(string TaskType, string DisplayName, string? Description, string? PayloadExample, Assembly Assembly);

/// <summary>The task types this host has handlers for, built from every registered <see cref="ITaskHandler"/>.</summary>
public interface ITaskTypeRegistry
{
    IReadOnlyList<TaskTypeInfo> TaskTypes { get; }
    IReadOnlyCollection<string> TaskTypeNames { get; }
    bool IsRegistered(string taskType);

    /// <summary>Resolves the handler for <paramref name="taskType"/> from <paramref name="services"/>, or null.</summary>
    ITaskHandler? Resolve(IServiceProvider services, string taskType);
}

internal sealed class TaskTypeRegistry(IServiceScopeFactory scopeFactory) : ITaskTypeRegistry
{
    private readonly Lazy<IReadOnlyList<TaskTypeInfo>> _types = new(() => Build(scopeFactory));

    public IReadOnlyList<TaskTypeInfo> TaskTypes => _types.Value;

    public IReadOnlyCollection<string> TaskTypeNames => _types.Value.Select(t => t.TaskType).ToArray();

    public bool IsRegistered(string taskType) =>
        _types.Value.Any(t => string.Equals(t.TaskType, taskType, StringComparison.Ordinal));

    public ITaskHandler? Resolve(IServiceProvider services, string taskType) =>
        services.GetServices<ITaskHandler>().FirstOrDefault(h => string.Equals(h.TaskType, taskType, StringComparison.Ordinal));

    private static IReadOnlyList<TaskTypeInfo> Build(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<ITaskHandler>().ToList();

        var duplicate = handlers.GroupBy(h => h.TaskType, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"The task type '{duplicate.Key}' is registered by more than one handler: " +
                string.Join(", ", duplicate.Select(h => h.GetType().FullName)) + ". Task type names must be unique.");
        }

        return handlers
            .Select(h => new TaskTypeInfo(h.TaskType, h.DisplayName, h.Description, h.PayloadExample, h.GetType().Assembly))
            .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
