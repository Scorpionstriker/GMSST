using Scheduler.Domain.Enums;

namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// The person using the Admin application. The Login and Security module supplies a session-based
/// implementation; without that module the host registers a local administrator, so the application
/// keeps working when the module is removed (FR-41, FR-44).
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Username { get; }
    UserRole? Role { get; }

    bool IsAdministrator => IsAuthenticated && Role is UserRole.Administrator or UserRole.Developer;
    bool IsDeveloper => IsAuthenticated && Role == UserRole.Developer;

    /// <summary>Called on user activity, so an idle session can expire (FR-48).</summary>
    void RegisterActivity()
    {
    }

    /// <summary>Raised when the user signs in or out, or the session expires.</summary>
    event EventHandler? Changed;
}
