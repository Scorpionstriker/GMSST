using Scheduler.Domain.Enums;
using Scheduler.Domain.Rules;

namespace Scheduler.Domain.Entities;

/// <summary>
/// A unit of work: what runs (task type and payload), how important it is and how long it may take.
/// Named ScheduledTask to avoid confusion with System.Threading.Tasks.Task (Phase 4, Section 3.4).
/// </summary>
public sealed class ScheduledTask : AuditableEntity
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;
    public const int TaskTypeMaxLength = 100;
    public const int MinPriority = 1;
    public const int MaxPriority = 10;
    public const int DefaultPriority = 5;
    public const int MaxRetriesLimit = 10;
    public const int MaxTimeoutSeconds = 86_400;
    public const int DefaultTimeoutSeconds = 300;
    public const int PayloadMaxBytes = 64 * 1024;
    public const string EmptyPayload = "{}";

    private readonly List<Schedule> _schedules = [];
    private readonly List<TaskDependency> _dependencies = [];

    private ScheduledTask()
    {
        Name = string.Empty;
        TaskType = string.Empty;
        Payload = EmptyPayload;
    }

    public ScheduledTask(
        string name,
        string? description,
        string taskType,
        string payload,
        int priority,
        int? maxRetries,
        int timeoutSeconds,
        DateTime nowUtc,
        Guid? userId)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        TaskType = taskType;
        Payload = string.IsNullOrWhiteSpace(payload) ? EmptyPayload : payload;
        Priority = priority;
        MaxRetries = maxRetries;
        TimeoutSeconds = timeoutSeconds;
        Status = ScheduledTaskStatus.Active;
        MarkCreated(nowUtc, userId);
    }

    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Name of the registered ITaskHandler that runs this task (FR-06).</summary>
    public string TaskType { get; private set; }

    /// <summary>JSON object passed to the handler (FR-01).</summary>
    public string Payload { get; private set; }

    /// <summary>1 to 10; 1 is claimed first (BR-09).</summary>
    public int Priority { get; private set; }

    public ScheduledTaskStatus Status { get; private set; }

    /// <summary>Retry limit for this task; null uses the global MaxRetryLimit (FR-31).</summary>
    public int? MaxRetries { get; private set; }

    public int TimeoutSeconds { get; private set; }

    /// <summary>
    /// Stable key of a task the scheduler or a module creates itself, for example "builtin.database-backup" (WP-8h).
    /// Such a task can be paused, edited and renamed, but not deleted. Null for tasks users create.
    /// </summary>
    public string? SystemKey { get; private set; }

    public bool IsBuiltIn => SystemKey is not null;

    public const int SystemKeyMaxLength = 100;

    public void AssignSystemKey(string key) => SystemKey = key;

    public IReadOnlyCollection<Schedule> Schedules => _schedules;
    public IReadOnlyCollection<TaskDependency> Dependencies => _dependencies;

    /// <summary>The schedule currently in force, if any.</summary>
    public Schedule? CurrentSchedule =>
        _schedules.Where(s => !s.IsDeleted).OrderByDescending(s => s.CreatedAtUtc).FirstOrDefault();

    public void UpdateDetails(
        string name,
        string? description,
        string taskType,
        string payload,
        int priority,
        int? maxRetries,
        int timeoutSeconds,
        DateTime nowUtc,
        Guid? userId)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        TaskType = taskType;
        Payload = string.IsNullOrWhiteSpace(payload) ? EmptyPayload : payload;
        Priority = priority;
        MaxRetries = maxRetries;
        TimeoutSeconds = timeoutSeconds;
        MarkUpdated(nowUtc, userId);
    }

    public void Pause(DateTime nowUtc, Guid? userId) => ChangeStatus(ScheduledTaskStatus.Paused, nowUtc, userId);

    public void Disable(DateTime nowUtc, Guid? userId) => ChangeStatus(ScheduledTaskStatus.Disabled, nowUtc, userId);

    public void Resume(DateTime nowUtc, Guid? userId) => ChangeStatus(ScheduledTaskStatus.Active, nowUtc, userId);

    public bool CanChangeStatusTo(ScheduledTaskStatus target) => StatusTransitions.IsAllowed(Status, target);

    private void ChangeStatus(ScheduledTaskStatus target, DateTime nowUtc, Guid? userId)
    {
        StatusTransitions.Ensure(CanChangeStatusTo(target), "task", Status, target);
        Status = target;
        MarkUpdated(nowUtc, userId);
    }

    /// <summary>Soft delete: the task disappears from normal use, its history stays (FR-04).</summary>
    public void Delete(DateTime nowUtc, Guid? userId)
    {
        IsDeleted = true;
        MarkUpdated(nowUtc, userId);
    }

    public void Restore(DateTime nowUtc, Guid? userId)
    {
        IsDeleted = false;
        MarkUpdated(nowUtc, userId);
    }

    /// <summary>
    /// Replaces the schedule in force. The old schedule is soft-deleted and kept, so a task can have
    /// more than one schedule over its lifetime (FR-17).
    /// </summary>
    public Schedule SetSchedule(Schedule schedule, DateTime nowUtc, Guid? userId)
    {
        foreach (var old in _schedules.Where(s => !s.IsDeleted))
        {
            old.Retire(nowUtc, userId);
        }

        _schedules.Add(schedule);
        MarkUpdated(nowUtc, userId);
        return schedule;
    }

    /// <summary>Replaces the set of prerequisite tasks. Cycle checks happen before this is called (FR-27).</summary>
    public void SetDependencies(IEnumerable<Guid> dependsOnTaskIds, DateTime nowUtc)
    {
        var wanted = dependsOnTaskIds.Where(id => id != Id).ToHashSet();
        _dependencies.RemoveAll(d => !wanted.Contains(d.DependsOnTaskId));
        foreach (var id in wanted.Where(id => _dependencies.All(d => d.DependsOnTaskId != id)))
        {
            _dependencies.Add(new TaskDependency(Id, id, nowUtc));
        }
    }
}
