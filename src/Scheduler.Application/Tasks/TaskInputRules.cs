using System.Text;
using System.Text.Json;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Cron;
using Scheduler.Application.Dependencies;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Tasks;

/// <summary>
/// The task editor validation rules of Table 6.1 (FR-74, FR-75; Algorithm 6.12). The same rules run in the
/// form for immediate feedback and here as the authoritative check.
/// </summary>
public static class TaskInputRules
{
    /// <summary>Checks every rule that needs no database. <paramref name="existingTaskType"/> is the stored type when editing.</summary>
    public static ValidationErrors ValidateFields(TaskInput input, ITaskTypeRegistry registry, DateTime nowUtc, string? existingTaskType = null)
    {
        var errors = new ValidationErrors();

        var name = input.Name?.Trim() ?? string.Empty;
        errors.AddIf(name.Length == 0 || name.Length > ScheduledTask.NameMaxLength, TaskFields.Name,
            $"Name is required (max {ScheduledTask.NameMaxLength} characters).");

        errors.AddIf(input.Description?.Length > ScheduledTask.DescriptionMaxLength, TaskFields.Description,
            $"Description is too long (max {ScheduledTask.DescriptionMaxLength:N0} characters).");

        var keepsUnregisteredType = existingTaskType is not null && string.Equals(existingTaskType, input.TaskType, StringComparison.Ordinal);
        errors.AddIf(string.IsNullOrWhiteSpace(input.TaskType) || (!registry.IsRegistered(input.TaskType) && !keepsUnregisteredType),
            TaskFields.TaskType, "Select a valid task type.");

        errors.AddIf(input.Priority is < ScheduledTask.MinPriority or > ScheduledTask.MaxPriority, TaskFields.Priority,
            "Priority must be between 1 and 10.");

        errors.AddIf(input.MaxRetries is < 0 or > ScheduledTask.MaxRetriesLimit, TaskFields.MaxRetries,
            "Max retries must be between 0 and 10.");

        errors.AddIf(input.TimeoutSeconds is < 1 or > ScheduledTask.MaxTimeoutSeconds, TaskFields.TimeoutSeconds,
            "Timeout must be between 1 second and 24 hours.");

        errors.AddIf(input.MaxExecutions is < 1 or > ScheduledTask.MaxExecutionsLimit, TaskFields.MaxExecutions,
            $"Execution limit must be between 1 and {ScheduledTask.MaxExecutionsLimit:N0}.");

        var payloadError = ValidatePayload(input.Payload);
        if (payloadError is not null)
        {
            errors.Add(TaskFields.Payload, payloadError);
        }

        ValidateSchedule(input.Schedule, nowUtc, errors);
        return errors;
    }

    /// <summary>Checks the rules that need stored data: unique name and dependency cycles.</summary>
    public static async Task ValidateAgainstStoreAsync(TaskInput input, Guid taskId, IUnitOfWork unitOfWork, ValidationErrors errors,
        CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length > 0 && await unitOfWork.Tasks.NameExistsAsync(name, taskId, cancellationToken))
        {
            errors.Add(TaskFields.Name, "Name must be unique.");
        }

        if (input.DependsOn.Count == 0)
        {
            return;
        }

        if (input.DependsOn.Distinct().Count() != input.DependsOn.Count)
        {
            errors.Add(TaskFields.DependsOn, "Each task can be selected only once.");
            return;
        }

        var existing = await unitOfWork.Tasks.GetExistingIdsAsync(input.DependsOn, cancellationToken);
        if (existing.Count != input.DependsOn.Count)
        {
            errors.Add(TaskFields.DependsOn, "A selected task no longer exists.");
            return;
        }

        var graph = new DependencyGraph(await unitOfWork.Tasks.GetDependencyEdgesAsync(cancellationToken));
        foreach (var dependsOn in input.DependsOn)
        {
            if (graph.WouldCreateCycle(taskId, dependsOn))
            {
                errors.Add(TaskFields.DependsOn, dependsOn == taskId
                    ? "A task cannot depend on itself."
                    : "Dependency would create a cycle.");
                return;
            }
        }
    }

    public static string? ValidatePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        if (Encoding.UTF8.GetByteCount(payload) > ScheduledTask.PayloadMaxBytes)
        {
            return "Payload must be at most 64 KB.";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object ? null : "Payload must be a JSON object, for example {}.";
        }
        catch (JsonException)
        {
            return "Payload must be valid JSON.";
        }
    }

    public static void ValidateSchedule(ScheduleInput schedule, DateTime nowUtc, ValidationErrors errors, bool requireFutureOnce = true)
    {
        if (!Enum.IsDefined(schedule.Type))
        {
            errors.Add(TaskFields.ScheduleType, "Select Cron, Interval or Once.");
            return;
        }

        errors.AddIf(!Enum.IsDefined(schedule.MisfirePolicy), TaskFields.MisfirePolicy, "Select Skip, Run once or Run all.");

        switch (schedule.Type)
        {
            case ScheduleType.Cron:
                if (!CronExpression.TryParse(schedule.CronExpression, out _, out var cronError))
                {
                    errors.Add(TaskFields.CronExpression, cronError!);
                }

                break;
            case ScheduleType.Interval:
                errors.AddIf(schedule.IntervalSeconds is null or < 1, TaskFields.IntervalSeconds, "Interval must be a positive whole number.");
                break;
        }

        if (!TimeZones.TryFind(schedule.TimeZoneId, out var zone))
        {
            errors.Add(TaskFields.TimeZoneId, "Select a valid time zone.");
            return;
        }

        // Cron and interval schedules start now when no start is given (NFR-04); a single run needs its time.
        if (schedule.StartTime is null && schedule.Type == ScheduleType.Once)
        {
            errors.Add(TaskFields.StartTime, "Run date and time are required.");
            return;
        }

        var startUtc = schedule.StartTime is { } start ? TimeZones.ToUtc(start, zone) : nowUtc;
        if (requireFutureOnce && schedule.Type == ScheduleType.Once && startUtc <= nowUtc)
        {
            errors.Add(TaskFields.StartTime, "Run time must be in the future.");
        }

        if (schedule.EndTime is not null && TimeZones.ToUtc(schedule.EndTime.Value, zone) <= startUtc)
        {
            errors.Add(TaskFields.EndTime, "End time must be later than start time.");
        }
    }
}
