using System.Data.Common;

namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// Gives a module a connection pool for its own local PostgreSQL database, so the module keeps its data apart from
/// the scheduler's database and needs no reference to the scheduler's data layer (FR-78, WP-1). The host, port and
/// user come from the module's configuration section (for example "Inventory"), falling back to the scheduler's
/// "Database" section. The password always comes from the GMSS_DB_PASSWORD environment variable (NFR-15).
/// </summary>
public interface IModuleDatabases
{
    /// <summary>
    /// The pool for the database named by <c>Name</c> in <paramref name="configurationSection"/>, or
    /// <paramref name="defaultDatabaseName"/> when the section does not name one. The same instance is returned
    /// for the same section.
    /// </summary>
    DbDataSource Get(string configurationSection, string defaultDatabaseName);

    /// <summary>The name of the database <see cref="Get"/> connects to for this section.</summary>
    string GetDatabaseName(string configurationSection, string defaultDatabaseName);
}
