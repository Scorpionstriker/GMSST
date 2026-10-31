using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Scheduler.Application;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Engine;
using Scheduler.Extensibility.Contracts;
using Scheduler.Infrastructure;
using Scheduler.Infrastructure.Configuration;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.IntegrationTests;

/// <summary>
/// Creates an empty database gmss_test_* on the local PostgreSQL server for one test class, applies the migrations
/// and drops the database afterwards. Connection settings come from GMSS_TEST_DB_HOST/PORT/USER (defaults
/// localhost, 5432, gmss_app) and the password from GMSS_DB_PASSWORD; without a password the tests are skipped.
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    private readonly List<ServiceProvider> _providers = [];

    public static string? Password => Environment.GetEnvironmentVariable(DatabaseOptions.PasswordVariable);

    public DatabaseOptions Options { get; } = new()
    {
        Host = Environment.GetEnvironmentVariable("GMSS_TEST_DB_HOST") ?? "localhost",
        Port = int.TryParse(Environment.GetEnvironmentVariable("GMSS_TEST_DB_PORT"), out var port) ? port : 5432,
        Username = Environment.GetEnvironmentVariable("GMSS_TEST_DB_USER") ?? "gmss_app",
        Name = "gmss_test_" + Guid.NewGuid().ToString("N")[..12]
    };

    public string BackupFolder { get; } = Path.Combine(Path.GetTempPath(), "gmss-tests", Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(Password))
        {
            return;
        }

        await using (var admin = new NpgsqlConnection(AdminConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{Options.Name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var services = CreateServices("setup", configureEngine: null);
        await services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        if (string.IsNullOrEmpty(Password))
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(AdminConnectionString());
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Options.Name}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
        if (Directory.Exists(BackupFolder))
        {
            Directory.Delete(BackupFolder, recursive: true);
        }
    }

    /// <summary>Drops the database and creates it again, empty, as on a newly installed computer (WP-5j).</summary>
    public async Task RecreateEmptyAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(AdminConnectionString());
        await admin.OpenAsync();
        await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Options.Name}\" WITH (FORCE)", admin))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{Options.Name}\"", admin);
        await create.ExecuteNonQueryAsync();
    }

    /// <summary>A complete scheduler host (application, infrastructure and engine) with the test handlers registered.</summary>
    public ServiceProvider CreateServices(string hostName, Action<EngineOptions>? configureEngine = null, Action<IServiceCollection>? configure = null)
    {
        var engineOptions = new EngineOptions { HostName = hostName, CreateBuiltInTasks = false };
        configureEngine?.Invoke(engineOptions);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSchedulerApplication();
        services.AddSchedulerInfrastructure(new DatabaseConnection(Options, Password));
        services.Configure<PathsOptions>(p =>
        {
            p.BackupFolder = BackupFolder;
            p.LogFolder = Path.Combine(BackupFolder, "logs");
        });
        services.AddSchedulerEngine(engineOptions);
        services.AddTransient<ITaskHandler, CountingHandler>();
        services.AddTransient<ITaskHandler, FailingHandler>();
        services.AddTransient<ITaskHandler, HangingHandler>();
        services.AddTransient<ITaskHandler, ThrowingHandler>();
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    public SchedulerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SchedulerDbContext>().UseNpgsql(Options.BuildConnectionString(Password)).Options);

    /// <summary>Sets a global setting directly in the database.</summary>
    public async Task SetSettingAsync(string key, int value)
    {
        await using var db = CreateContext();
        await db.Configuration.Where(c => c.TaskId == null && c.Key == key)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Value, value.ToString()));
    }

    private string AdminConnectionString() =>
        new NpgsqlConnectionStringBuilder(Options.BuildConnectionString(Password)) { Database = "postgres", SearchPath = null, Pooling = false }
            .ConnectionString;
}

/// <summary>Skips a test when no database password is configured.</summary>
public static class RequiresDatabase
{
    public static void Check() => Skip.If(string.IsNullOrEmpty(TestDatabase.Password),
        "Set GMSS_DB_PASSWORD (and a role with CREATEDB) to run the database tests.");
}

public static class Wait
{
    /// <summary>Polls <paramref name="condition"/> until it is true or the timeout passes.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"Timed out after {timeout.TotalSeconds} s waiting until {because}.");
    }
}
