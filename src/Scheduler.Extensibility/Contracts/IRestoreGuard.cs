namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// Lets a module stop a restore of a database that its separate application is using (WP-5). For example, the
/// Maintenance Agent connector refuses to restore maintenance_demo while the Maintenance Agent is running.
/// </summary>
public interface IRestoreGuard
{
    /// <summary>Returns null when the listed databases may be restored now, otherwise the reason shown to the user.</summary>
    Task<string?> CheckRestoreAsync(IReadOnlyCollection<string> databaseNames, CancellationToken cancellationToken = default);
}
