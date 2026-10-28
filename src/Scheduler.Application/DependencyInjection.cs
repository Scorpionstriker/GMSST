using Microsoft.Extensions.DependencyInjection;

namespace Scheduler.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case handlers, validators and application services.</summary>
    public static IServiceCollection AddSchedulerApplication(this IServiceCollection services)
    {
        // Register handlers, validators, CronExpression, ScheduleCalculator, RetryPolicy and
        // DependencyGraph here as they are implemented.
        return services;
    }
}
