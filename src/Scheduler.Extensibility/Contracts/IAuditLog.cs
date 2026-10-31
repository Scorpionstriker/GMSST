namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// Records who did what to accounts and security settings (FR-49, WP-4c). Entries are written to the scheduler's
/// log with source "Security" and appear in the Security and audit report. Never pass a password or a key.
/// </summary>
public interface IAuditLog
{
    /// <summary>Writes one audit entry naming the signed-in user as the person responsible.</summary>
    Task RecordAsync(string action, string message, CancellationToken cancellationToken = default);
}
