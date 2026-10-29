using Microsoft.Extensions.Logging;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.Handlers;

/// <summary>Writes a message to the scheduler log. Useful to check that a schedule fires when expected (FR-07).</summary>
public sealed class LogEntryHandler : ITaskHandler
{
    public const string Type = "LogEntry";

    public string TaskType => Type;
    public string DisplayName => "Log entry";
    public string Description => "Writes a message to the scheduler log.";
    public string PayloadExample => """{ "message": "Hello from the scheduler", "level": "Info" }""";

    public Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        var message = context.GetString("message");
        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.FromResult(TaskResult.Failure("The payload must contain a \"message\" text."));
        }

        var level = context.GetString("level")?.Trim().ToLowerInvariant() switch
        {
            null or "" or "info" or "information" => LogLevel.Information,
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            var other => (LogLevel?)null
        };
        if (level is null)
        {
            return Task.FromResult(TaskResult.Failure("\"level\" must be Info, Warning or Error."));
        }

        context.Logger.Log(level.Value, "{Message}", message);
        return Task.FromResult(TaskResult.Success("Message logged."));
    }
}
