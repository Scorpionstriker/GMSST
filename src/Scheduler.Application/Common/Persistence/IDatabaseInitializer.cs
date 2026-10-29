namespace Scheduler.Application.Common.Persistence;

/// <summary>Brings the database up to date at start-up and seeds the default data of Phase 4, Table 5.16.</summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Applies pending migrations (after a safety backup when the database already holds data) and inserts any
    /// missing default settings and logging providers. Existing values are never overwritten. Safe to call from
    /// several processes and on every start.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
