using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Infrastructure.Persistence;

/// <summary>
/// One connection pool per module database (WP-1). A module section may set Host, Port, Username and Name; anything
/// it leaves out comes from the scheduler's own connection. The password is the scheduler's, from GMSS_DB_PASSWORD.
/// </summary>
internal sealed class ModuleDatabases(DatabaseConnection connection, IConfiguration? configuration) : IModuleDatabases, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, NpgsqlDataSource> _sources = new(StringComparer.OrdinalIgnoreCase);

    public DbDataSource Get(string configurationSection, string defaultDatabaseName) =>
        _sources.GetOrAdd(configurationSection, _ => new NpgsqlDataSourceBuilder(BuildConnectionString(configurationSection, defaultDatabaseName)).Build());

    public string GetDatabaseName(string configurationSection, string defaultDatabaseName) =>
        Resolve(configurationSection, defaultDatabaseName).Name;

    internal string BuildConnectionString(string configurationSection, string defaultDatabaseName)
    {
        var options = Resolve(configurationSection, defaultDatabaseName);
        return new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = options.Name,
            Username = options.Username,
            Password = connection.Password,
            ApplicationName = "GSST " + configurationSection
        }.ConnectionString;
    }

    private DatabaseOptions Resolve(string configurationSection, string defaultDatabaseName)
    {
        var main = connection.Options;
        var options = new DatabaseOptions { Host = main.Host, Port = main.Port, Username = main.Username, Name = defaultDatabaseName };
        configuration?.GetSection(configurationSection).Bind(options);
        return options;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in _sources.Values)
        {
            await source.DisposeAsync();
        }

        _sources.Clear();
    }
}
