namespace Scheduler.Application.Dependencies;

/// <summary>A prerequisite relationship: <see cref="TaskId"/> runs after <see cref="DependsOnTaskId"/> succeeds.</summary>
public readonly record struct DependencyEdge(Guid TaskId, Guid DependsOnTaskId);

/// <summary>Detects self-dependencies and circular dependency chains (Algorithm 6.10, FR-27, BR-05).</summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<Guid, List<Guid>> _prerequisites = [];

    public DependencyGraph(IEnumerable<DependencyEdge> edges)
    {
        foreach (var edge in edges)
        {
            if (!_prerequisites.TryGetValue(edge.TaskId, out var list))
            {
                _prerequisites[edge.TaskId] = list = [];
            }

            list.Add(edge.DependsOnTaskId);
        }
    }

    /// <summary>
    /// True when making <paramref name="taskId"/> depend on <paramref name="dependsOnId"/> would create a cycle,
    /// that is when the prerequisite already depends, directly or indirectly, on the task. The task's own
    /// existing edges are ignored, because the editor replaces them.
    /// </summary>
    public bool WouldCreateCycle(Guid taskId, Guid dependsOnId)
    {
        if (taskId == dependsOnId)
        {
            return true;
        }

        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(dependsOnId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == taskId)
            {
                return true;
            }

            if (!visited.Add(current) || !_prerequisites.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var id in next)
            {
                stack.Push(id);
            }
        }

        return false;
    }
}
