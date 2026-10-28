using Npgsql;

namespace Scheduler.Infrastructure.Persistence;

/// <summary>
/// The "Database" section of appsettings.json. The password is never stored here: it is read from
/// the GMSS_DB_PASSWORD user environment variable or the operating-system credential store (NFR-15).
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string PasswordVariable = "GMSS_DB_PASSWORD";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Name { get; set; } = "gmss";
    public string Username { get; set; } = "gmss_app";

    public string BuildConnectionString(string? password)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Name,
            Username = Username,
            Password = password,
            SearchPath = SchedulerDbContext.Schema
        };
        return builder.ConnectionString;
    }
}
