namespace Scheduler.Domain.Entities;

/// <summary>One sign-in attempt and its outcome (FR-53).</summary>
public sealed class LoginAudit
{
    public const int ReasonMaxLength = 100;

    private LoginAudit()
    {
        UsernameAttempted = string.Empty;
    }

    public LoginAudit(Guid? userId, string usernameAttempted, bool succeeded, string? reason, DateTime attemptedAtUtc)
    {
        UserId = userId;
        UsernameAttempted = usernameAttempted.Length > AppUser.UsernameMaxLength
            ? usernameAttempted[..AppUser.UsernameMaxLength]
            : usernameAttempted;
        Succeeded = succeeded;
        Reason = reason;
        AttemptedAtUtc = attemptedAtUtc;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid? UserId { get; private set; }
    public string UsernameAttempted { get; private set; }
    public bool Succeeded { get; private set; }
    public string? Reason { get; private set; }
    public DateTime AttemptedAtUtc { get; private set; }
}
