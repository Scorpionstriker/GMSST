using Microsoft.Extensions.DependencyInjection;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.IntegrationTests;

internal static class TaskSetup
{
    /// <summary>A task whose schedule is far in the future, so it only runs when it is run manually.</summary>
    public static Task<Guid> CreateManualTaskAsync(IServiceProvider services, string taskType, string payload = "{}", int? maxRetries = 0,
        int timeoutSeconds = 30, string? name = null) =>
        services.GetRequiredService<ISchedulerClient>().EnsureTaskAsync(new TaskDefinition
        {
            Name = name ?? $"{taskType} {Guid.NewGuid():N}",
            TaskType = taskType,
            Payload = payload,
            MaxRetries = maxRetries,
            TimeoutSeconds = timeoutSeconds,
            ScheduleType = ScheduleType.Cron,
            CronExpression = "0 0 1 1 *"
        });
}
