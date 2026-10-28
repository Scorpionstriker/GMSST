using Microsoft.Extensions.DependencyInjection;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine;

public static class DependencyInjection
{
    /// <summary>Registers the engine, the executor and the handler registry.</summary>
    public static IServiceCollection AddSchedulerEngine(this IServiceCollection services)
    {
        services.AddSingleton<EngineSignal>();
        services.AddSingleton<TaskExecutor>();
        services.AddSingleton<SchedulerEngine>();
        return services;
    }

    /// <summary>
    /// Lets each module register its handlers, providers and services (FR-41). Removing a module from
    /// this list removes everything it contributed.
    /// </summary>
    public static IServiceCollection AddSchedulerModules(this IServiceCollection services, params ISchedulerModule[] modules)
    {
        foreach (var module in modules)
        {
            module.Register(services);
            services.AddSingleton(module);
        }
        return services;
    }
}
