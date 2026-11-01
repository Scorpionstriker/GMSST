using System.Text.Json;
using Microsoft.Extensions.Logging;
using Scheduler.Domain.Enums;

namespace Scheduler.Extensibility.Contracts;

/// <summary>Everything a running handler is given by the engine (FR-22, FR-40).</summary>
public interface ITaskContext
{
    Guid TaskId { get; }
    Guid ExecutionId { get; }
    string TaskName { get; }

    /// <summary>0 for the first attempt, 1 for the first retry, and so on.</summary>
    int RetryNumber { get; }

    /// <summary>Why the run happened: Scheduled, Manual, Retry, Dependency, CatchUp or Event.</summary>
    TriggerType TriggerType { get; }

    /// <summary>Name of the triggering event, for event-triggered runs.</summary>
    string? EventName => null;

    /// <summary>Module or application that raised the event, for event-triggered runs.</summary>
    string? EventSource => null;

    /// <summary>Event-specific JSON data, separate from the task's configured payload.</summary>
    JsonElement? EventData => null;

    /// <summary>The time the run was planned for, in UTC.</summary>
    DateTime ScheduledTimeUtc { get; }

    /// <summary>The task's JSON payload (FR-01). Always a JSON object.</summary>
    JsonElement Payload { get; }

    /// <summary>
    /// Logger whose entries are stored with this execution and shown in its details (FR-28, FR-58).
    /// Never log passwords or other secrets.
    /// </summary>
    ILogger Logger { get; }

    /// <summary>Signalled on time-out, cancellation or shutdown.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>The services of the host, for handlers that need more than their constructor gives them.</summary>
    IServiceProvider Services { get; }
}

/// <summary>Helpers for reading the payload.</summary>
public static class TaskContextExtensions
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the payload into <typeparamref name="T"/> (property names are case-insensitive).</summary>
    public static T? GetPayload<T>(this ITaskContext context) => context.Payload.Deserialize<T>(PayloadOptions);

    /// <summary>Returns a string property of the payload, or null when it is missing or not a string.</summary>
    public static string? GetString(this ITaskContext context, string propertyName) =>
        TryGetProperty(context.Payload, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Returns a whole-number property of the payload, or null when it is missing or not a number.</summary>
    public static int? GetInt32(this ITaskContext context, string propertyName) =>
        TryGetProperty(context.Payload, propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool TryGetProperty(JsonElement payload, string name, out JsonElement value)
    {
        value = default;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }
}
