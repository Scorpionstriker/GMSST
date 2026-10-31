using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Modules.Security.Authentication;

/// <summary>
/// The signed-in user of this desktop application, held in memory only (Phase 4, Section 8.3). The session ends
/// after SessionTimeoutMinutes without activity (FR-48); <see cref="ICurrentUser.Changed"/> tells the Admin
/// application to return to the login page.
/// </summary>
public sealed class UserSession : ICurrentUser, IDisposable
{
    private readonly TimeProvider _clock;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private DateTime _lastActivityUtc;
    private TimeSpan _timeout = TimeSpan.FromMinutes(20);

    public UserSession(TimeProvider clock)
    {
        _clock = clock;
        _timer = clock.CreateTimer(_ => CheckExpiry(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    public bool IsAuthenticated { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Username { get; private set; }
    public UserRole? Role { get; private set; }

    /// <summary>True when the last sign-out happened because the session timed out, so the login page can say so.</summary>
    public bool ExpiredByTimeout { get; private set; }

    public bool IsAdministrator => IsAuthenticated && Role is UserRole.Administrator or UserRole.Developer;
    public bool IsDeveloper => IsAuthenticated && Role == UserRole.Developer;

    public event EventHandler? Changed;

    public void SignIn(Guid userId, string username, UserRole role, TimeSpan timeout)
    {
        lock (_gate)
        {
            UserId = userId;
            Username = username;
            Role = role;
            IsAuthenticated = true;
            ExpiredByTimeout = false;
            _timeout = timeout;
            _lastActivityUtc = _clock.GetUtcNow().UtcDateTime;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut() => End(expired: false);

    public void RegisterActivity()
    {
        if (CheckExpiry())
        {
            lock (_gate)
            {
                _lastActivityUtc = _clock.GetUtcNow().UtcDateTime;
            }
        }
    }

    /// <summary>Ends the session when it has been idle too long. Returns true while the session is still valid.</summary>
    public bool CheckExpiry()
    {
        bool expired;
        lock (_gate)
        {
            if (!IsAuthenticated)
            {
                return false;
            }

            expired = _clock.GetUtcNow().UtcDateTime - _lastActivityUtc > _timeout;
        }

        if (expired)
        {
            End(expired: true);
        }

        return !expired;
    }

    public void Dispose() => _timer.Dispose();

    private void End(bool expired)
    {
        lock (_gate)
        {
            if (!IsAuthenticated)
            {
                return;
            }

            IsAuthenticated = false;
            UserId = null;
            Username = null;
            Role = null;
            ExpiredByTimeout = expired;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
