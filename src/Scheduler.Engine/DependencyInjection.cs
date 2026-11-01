using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Application.Tasks;
using Scheduler.Engine.Events;
using Scheduler.Engine.Execution;
using Scheduler.Engine.Handlers;
using Scheduler.Engine.Hosting;
using Scheduler.Engine.HealthMonitoring;
using Scheduler.Extensibility.Contracts;
using Scheduler.Extensibility.Events;

namespace Scheduler.Engine;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the engine, the executor, the event hub and, unless switched off, the built-in task handlers.
    /// Call after AddSchedulerApplication: it replaces the application layer's no-op signal and event publisher.
    /// </summary>
    public static IServiceCollection AddSchedulerEngine(this IServiceCollection services, EngineOptions? options = null)
    {
        options ??= new EngineOptions();
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEngineInstanceLock, NoEngineInstanceLock>();
        services.TryAddSingleton<IResourceHealthReader, EmptyResourceHealthReader>();
        services.TryAddSingleton<IUserAccountStore, EmptyUserAccountStore>();
        services.TryAddSingleton<ISchedulerNotifications, UnavailableSchedulerNotifications>();
        services.AddSingleton<ResourceHealthMonitor>();

        services.AddSingleton<EngineSignal>();
        services.Replace(ServiceDescriptor.Singleton<IEngineSignal>(sp => sp.GetRequiredService<EngineSignal>()));
        services.AddSingleton<SchedulerEventHub>();
        services.Replace(ServiceDescriptor.Singleton<ISchedulerEventPublisher>(sp => sp.GetRequiredService<SchedulerEventHub>()));
        services.AddSingleton<ISchedulerEvents>(sp => sp.GetRequiredService<SchedulerEventHub>());

        services.AddSingleton<FailureRecorder>();
        // The constructors are internal, so the instances are built here rather than by the container.
        services.AddSingleton(sp => new TaskExecutor(
            sp.GetRequiredService<IUnitOfWorkFactory>(),
            sp.GetRequiredService<ITaskTypeRegistry>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<FailureRecorder>(),
            sp.GetRequiredService<ISchedulerLogger>(),
            sp.GetRequiredService<ISchedulerEventPublisher>(),
            sp.GetRequiredService<IEngineSignal>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<TaskExecutor>>()));
        services.AddSingleton(sp => new SchedulerEngine(
            sp.GetRequiredService<IUnitOfWorkFactory>(),
            sp.GetRequiredService<ISettingsProvider>(),
            sp.GetRequiredService<ITaskTypeRegistry>(),
            sp.GetRequiredService<TaskExecutor>(),
            sp.GetRequiredService<FailureRecorder>(),
            sp.GetRequiredService<EngineSignal>(),
            sp.GetRequiredService<ISchedulerEventPublisher>(),
            sp.GetRequiredService<ISchedulerLogger>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<EngineOptions>(),
            sp.GetRequiredService<IEngineInstanceLock>(),
            sp.GetRequiredService<ResourceHealthMonitor>(),
            sp.GetRequiredService<ILogger<SchedulerEngine>>()));
        services.AddSingleton<IEngineControl>(sp => sp.GetRequiredService<SchedulerEngine>());
        services.AddSingleton<SchedulerHost>();

        if (options.RegisterBuiltInHandlers)
        {
            services.AddTransient<ITaskHandler, LogEntryHandler>();
            services.AddTransient<ITaskHandler, LocalHttpRequestHandler>();
            services.AddTransient<ITaskHandler, DatabaseBackupHandler>();
            services.AddTransient<ITaskHandler, LogCleanupHandler>();
        }

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

        // WP-4a: the developer login bypass applies to whichever current user the modules left registered.
        return services.UseLoginBypass();
    }
}
