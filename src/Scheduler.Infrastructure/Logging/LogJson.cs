using System.Text.Json;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Infrastructure.Logging;

/// <summary>Turns a log entry into JSON, leaving out values whose names suggest a secret (Phase 4, Section 8.6).</summary>
internal static class LogJson
{
    private static readonly string[] SecretMarkers = ["password", "secret", "token", "apikey", "api_key", "connectionstring"];

    public static Dictionary<string, object?> ToObject(SchedulerLogEntry entry, bool includeEnvelope)
    {
        var data = new Dictionary<string, object?>();
        if (includeEnvelope)
        {
            data["time"] = entry.TimestampUtc.ToString("O");
            data["level"] = entry.Level.ToString();
            data["source"] = entry.Component;
            if (entry.ExecutionId is { } executionId)
            {
                data["executionId"] = executionId;
            }
        }

        data["message"] = entry.Message;
        if (entry.Properties is { Count: > 0 } properties)
        {
            data["properties"] = properties.ToDictionary(p => p.Key, p => IsSecret(p.Key) ? "***" : Simplify(p.Value));
        }

        return data;
    }

    public static string Serialize(SchedulerLogEntry entry, bool includeEnvelope) => JsonSerializer.Serialize(ToObject(entry, includeEnvelope));

    private static bool IsSecret(string name) =>
        SecretMarkers.Any(marker => name.Replace("-", string.Empty).Contains(marker, StringComparison.OrdinalIgnoreCase));

    // Values that cannot be serialised (for example an exception object) are stored as text.
    private static object? Simplify(object? value) => value switch
    {
        null or string or bool or int or long or double or decimal or Guid or DateTime or DateTimeOffset => value,
        Enum e => e.ToString(),
        TimeSpan t => t.ToString(),
        _ => value.ToString()
    };
}
