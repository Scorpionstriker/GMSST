using Scheduler.Domain.Enums;

namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// Storage of Admin application accounts and the login audit. The scheduler stores them in its
/// app_user and login_audit tables; an authentication module reads and writes them through this
/// contract, so it never needs the data layer (Phase 4, Table 3.1).
/// </summary>
public interface IUserAccountStore
{
    Task<bool> AnyUsersAsync(CancellationToken cancellationToken = default);
    Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<UserAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>True when another user already has this name, ignoring letter case (BR-11).</summary>
    Task<bool> UsernameExistsAsync(string username, Guid? exceptUserId, CancellationToken cancellationToken = default);

    Task CreateAsync(UserAccount user, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserAccount user, CancellationToken cancellationToken = default);
    Task AddLoginAuditAsync(LoginAuditRecord record, CancellationToken cancellationToken = default);
}

/// <summary>An account as seen by authentication modules.</summary>
public sealed class UserAccount
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public UserRole Role { get; set; } = UserRole.Viewer;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The hashed one-time recovery code used by "Forgot password"; null when the user has none.</summary>
    public string? RecoveryCodeHash { get; set; }
    public string? RecoveryCodeSalt { get; set; }

    /// <summary>The row version when the account was read. An update fails with <see cref="UserAccountConflictException"/> if it changed since.</summary>
    public uint Version { get; init; }
}

/// <summary>The account was changed by someone else after it was read (WP-7, F-27).</summary>
public sealed class UserAccountConflictException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A username was claimed by another account before the change was saved.</summary>
public sealed class UserAccountNameConflictException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>One sign-in attempt (FR-53).</summary>
public sealed record LoginAuditRecord(Guid? UserId, string UsernameAttempted, bool Succeeded, string? Reason, DateTime AttemptedAtUtc);
