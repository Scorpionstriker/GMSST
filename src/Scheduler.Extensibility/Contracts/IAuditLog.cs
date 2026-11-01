namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// Records user-initiated administrative, account, and module actions. Entries are written to the scheduler's log
/// with source "Security" and appear in the Security and audit report. Never pass a password, recovery code, or key.
/// </summary>
public interface IAuditLog
{
    /// <summary>Writes one audit entry naming the signed-in user as the person responsible.</summary>
    Task RecordAsync(string action, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one audit entry and identifies the application or module that produced it. Existing implementations
    /// remain compatible; implementations that support application metadata should preserve it in the audit record.
    /// </summary>
    Task RecordAsync(string action, string message, string application, CancellationToken cancellationToken = default) =>
        RecordAsync(action, message, cancellationToken);
}
