using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers persistence, the task queue, logging providers, caching and backup services.</summary>
    public static IServiceCollection AddSchedulerInfrastructure(this IServiceCollection services, DatabaseOptions database)
    {
        var password = Environment.GetEnvironmentVariable(DatabaseOptions.PasswordVariable);

        services.AddDbContextFactory<SchedulerDbContext>(options =>
            options.UseNpgsql(database.BuildConnectionString(password)));

        services.AddMemoryCache();

        // Register PostgresTaskQueue, ConfigurationService, the logging providers and the
        // backup/restore service here as they are implemented.
        return services;
    }
}
