namespace Scheduler.Infrastructure.Persistence;

/// <summary>
/// The resolved connection settings, shared by EF Core and the backup tools. The password is read once from the
/// GMSS_DB_PASSWORD environment variable and is never written to a file or a command line (NFR-15).
/// </summary>
public sealed class DatabaseConnection(DatabaseOptions options, string? password)
{
    public DatabaseOptions Options { get; } = options;
    internal string? Password { get; } = password;
    public bool HasPassword => !string.IsNullOrEmpty(Password);
    public string ConnectionString => Options.BuildConnectionString(Password);

    public static DatabaseConnection FromEnvironment(DatabaseOptions options) =>
        new(options, Environment.GetEnvironmentVariable(DatabaseOptions.PasswordVariable));
}
