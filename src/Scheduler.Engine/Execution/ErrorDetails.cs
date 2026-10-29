using System.Text.Json;

namespace Scheduler.Engine.Execution;

/// <summary>Turns an exception into the structured stack trace stored in error_log (FR-30).</summary>
internal static class ErrorDetails
{
    public static string? ToJson(Exception? exception) =>
        exception is null ? null : JsonSerializer.Serialize(Describe(exception, depth: 0));

    private static Dictionary<string, object?> Describe(Exception exception, int depth)
    {
        var data = new Dictionary<string, object?>
        {
            ["type"] = exception.GetType().FullName,
            ["message"] = exception.Message,
            ["frames"] = (exception.StackTrace ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(100)
                .ToArray()
        };

        var inner = exception is AggregateException { InnerExceptions.Count: 1 } aggregate ? aggregate.InnerExceptions[0] : exception.InnerException;
        if (inner is not null && depth < 5)
        {
            data["inner"] = Describe(inner, depth + 1);
        }

        return data;
    }

    public static string TypeName(Exception exception) =>
        (exception is AggregateException { InnerExceptions.Count: 1 } aggregate ? aggregate.InnerExceptions[0] : exception).GetType().Name;
}
