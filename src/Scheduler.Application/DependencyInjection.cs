using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Scheduler.Application.Common;
using Scheduler.Application.Tasks;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers every use-case handler wrapped in its logging, authorisation and validation decorators, every
    /// validator, the task type registry and the scheduler client. Handlers and validators are found by scanning
    /// this assembly, so a new use case cannot be forgotten.
    /// </summary>
    public static IServiceCollection AddSchedulerApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICurrentUser, LocalAdministrator>();
        services.TryAddSingleton<IEngineSignal, NullEngineSignal>();
        services.TryAddSingleton<ISchedulerEventPublisher, NullEventPublisher>();
        services.TryAddSingleton<ITaskTypeRegistry, TaskTypeRegistry>();
        services.TryAddSingleton<ISchedulerClient, SchedulerClient>();

        var assembly = typeof(DependencyInjection).Assembly;
        foreach (var type in ConcreteTypes(assembly))
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = contract.GetGenericTypeDefinition();
                if (definition == typeof(IValidator<>))
                {
                    services.AddTransient(contract, type);
                }
                else if (definition == typeof(ICommandHandler<,>))
                {
                    RegisterDecorated(services, type, contract, nameof(DecorateCommand));
                }
                else if (definition == typeof(IQueryHandler<,>))
                {
                    RegisterDecorated(services, type, contract, nameof(DecorateQuery));
                }
            }
        }

        return services;
    }

    private static IEnumerable<Type> ConcreteTypes(Assembly assembly) =>
        assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

    private static void RegisterDecorated(IServiceCollection services, Type implementation, Type contract, string factoryName)
    {
        services.TryAddTransient(implementation);
        var arguments = contract.GetGenericArguments();
        var factory = typeof(DependencyInjection)
            .GetMethod(factoryName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(arguments[0], arguments[1]);

        services.AddTransient(contract, sp => factory.Invoke(null, [sp, implementation])!);
    }

    private static ICommandHandler<TCommand, TResult> DecorateCommand<TCommand, TResult>(IServiceProvider sp, Type implementation)
    {
        var handler = (ICommandHandler<TCommand, TResult>)sp.GetRequiredService(implementation);
        var currentUser = sp.GetRequiredService<ICurrentUser>();
        ICommandHandler<TCommand, TResult> validated = new ValidationCommandDecorator<TCommand, TResult>(handler, sp.GetServices<IValidator<TCommand>>());
        ICommandHandler<TCommand, TResult> authorized = new AuthorizationCommandDecorator<TCommand, TResult>(validated, currentUser);
        return new LoggingCommandDecorator<TCommand, TResult>(authorized, currentUser, sp.GetRequiredService<ISchedulerLogger>(),
            sp.GetRequiredService<ILogger<LoggingCommandDecorator<TCommand, TResult>>>());
    }

    private static IQueryHandler<TQuery, TResult> DecorateQuery<TQuery, TResult>(IServiceProvider sp, Type implementation)
    {
        var handler = (IQueryHandler<TQuery, TResult>)sp.GetRequiredService(implementation);
        IQueryHandler<TQuery, TResult> validated = new ValidationQueryDecorator<TQuery, TResult>(handler, sp.GetServices<IValidator<TQuery>>());
        IQueryHandler<TQuery, TResult> authorized = new AuthorizationQueryDecorator<TQuery, TResult>(validated, sp.GetRequiredService<ICurrentUser>());
        return new LoggingQueryDecorator<TQuery, TResult>(authorized, sp.GetRequiredService<ISchedulerLogger>(),
            sp.GetRequiredService<ILogger<LoggingQueryDecorator<TQuery, TResult>>>());
    }
}
