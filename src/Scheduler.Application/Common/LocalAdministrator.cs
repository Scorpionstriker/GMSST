using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Common;

/// <summary>
/// The user when no authentication module is installed: the person at the computer is the administrator.
/// This keeps the Admin application working when the Login and Security module is removed (FR-41).
/// </summary>
internal sealed class LocalAdministrator : ICurrentUser
{
    public bool IsAuthenticated => true;
    public Guid? UserId => null;
    public string? Username => "Local administrator";
    public UserRole? Role => UserRole.Administrator;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
