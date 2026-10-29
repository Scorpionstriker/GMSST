using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduler.Application.Backup;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Application.Executions;
using Scheduler.Application.Queue;
using Scheduler.Application.Reports;
using Scheduler.Application.Tasks;
using Scheduler.Extensibility.Contracts;
using Scheduler.Infrastructure.Backup;
using Scheduler.Infrastructure.Caching;
using Scheduler.Infrastructure.Configuration;
using Scheduler.Infrastructure.Logging;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Persistence.Queries;
using Scheduler.Infrastructure.Persistence.Repositories;

namespace Scheduler.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence (EF Core on PostgreSQL), the repositories and queries, the settings cache, the
    /// Console, File and Database logging providers and the backup service. Reads the "Database" and "Paths"
    /// sections of <paramref name="configuration"/>; the password comes from GMSS_DB_PASSWORD (NFR-15).
    /// </summary>
    public static IServiceCollection AddSchedulerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.SectionName));
        return services.AddSchedulerInfrastructure(DatabaseConnection.FromEnvironment(databaseOptions));
    }

    public static IServiceCollection AddSchedulerInfrastructure(this IServiceCollection services, DatabaseConnection connection)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<PathsOptions>();
        services.AddSingleton(connection);

        // One data source per process; modules (for example the Inventory Monitor) can use it for their own schema.
        var dataSource = new Npgsql.NpgsqlDataSourceBuilder(connection.ConnectionString).Build();
        services.AddSingleton(dataSource);
        services.AddSingleton<System.Data.Common.DbDataSource>(dataSource);
        services.AddDbContextFactory<SchedulerDbContext>(options => ConfigureDbContext(options, dataSource));

        services.AddMemoryCache();
        services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.AddSingleton<IRetentionStore, RetentionStore>();
        services.AddSingleton<IUserAccountStore, UserAccountStore>();
        services.AddSingleton<ITaskQueries, TaskQueries>();
        services.AddSingleton<IQueueQueries, QueueQueries>();
        services.AddSingleton<IExecutionQueries, ExecutionQueries>();
        services.AddSingleton<IReportQueries, ReportQueries>();

        services.AddSingleton<CachedSettingsProvider>();
        services.AddSingleton<ISettingsProvider>(sp => sp.GetRequiredService<CachedSettingsProvider>());
        services.AddSingleton<ISettingsReader>(sp => sp.GetRequiredService<CachedSettingsProvider>());

        services.AddSingleton<ILoggingProvider, DatabaseLoggingProvider>();
        services.AddSingleton<ILoggingProvider, FileLoggingProvider>();
        services.AddSingleton<ILoggingProvider, ConsoleLoggingProvider>();
        services.AddSingleton<ISchedulerLogger, SchedulerLogWriter>();

        services.AddSingleton<IBackupService, PgBackupService>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, Npgsql.NpgsqlDataSource dataSource) =>
        options.UseNpgsql(dataSource, Npgsql)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, DatabaseConnection connection) =>
        options.UseNpgsql(connection.ConnectionString, Npgsql);

    private static void Npgsql(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(SchedulerDbContext.MigrationsHistoryTable, SchedulerDbContext.Schema);

}
