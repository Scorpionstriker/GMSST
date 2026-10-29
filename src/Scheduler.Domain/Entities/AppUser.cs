using Scheduler.Domain.Enums;

namespace Scheduler.Domain.Entities;

/// <summary>
/// An Admin application account of the Login and Security module (FR-44 to FR-53). Only a salted
/// PBKDF2 hash of the password is stored (FR-46).
/// </summary>
public sealed class AppUser
{
    public const int UsernameMaxLength = 50;

    private AppUser()
    {
        Username = string.Empty;
        PasswordHash = string.Empty;
        PasswordSalt = string.Empty;
    }

    public AppUser(Guid id, string username, string passwordHash, string passwordSalt, UserRole role, bool isActive, DateTime nowUtc)
    {
        Id = id;
        Username = username;
        PasswordHash = passwordHash;
        PasswordSalt = passwordSalt;
        Role = role;
        IsActive = isActive;
        CreatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public string Username { get; set; }
    public string PasswordHash { get; set; }
    public string PasswordSalt { get; set; }
    public UserRole Role { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
