using Scheduler.Extensibility.Contracts;

namespace Scheduler.Modules.Security.Authentication;

/// <summary>
/// Local username and password sign-in (Algorithm 6.13): one generic failure message, equal timing for unknown
/// users, lockout after MaxFailedLogins attempts for LockoutMinutes, and an audit record of every attempt
/// (FR-45, FR-47, FR-53).
/// </summary>
public sealed class LocalAuthenticationProvider(
    IUserAccountStore users,
    PasswordHasher hasher,
    ISettingsReader settings,
    UserSession session,
    TimeProvider clock) : IAuthenticationProvider
{
    public const string GenericFailureMessage = "Invalid username or password.";

    public async Task<AuthenticationResult> SignInAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        username = (username ?? string.Empty).Trim();
        password ??= string.Empty;
        var now = clock.GetUtcNow().UtcDateTime;
        var user = username.Length == 0 ? null : await users.FindByUsernameAsync(username, cancellationToken);

        if (user is null || !user.IsActive)
        {
            hasher.VerifyDummy(password);
            await AuditAsync(user?.Id, username, false, user is null ? "InvalidCredentials" : "Inactive", now, cancellationToken);
            return Fail(GenericFailureMessage);
        }

        if (user.LockoutEndUtc > now)
        {
            await AuditAsync(user.Id, username, false, "LockedOut", now, cancellationToken);
            var minutes = (int)Math.Ceiling((user.LockoutEndUtc.Value - now).TotalMinutes);
            return Fail($"Account locked. Try again after {minutes} minute{(minutes == 1 ? "" : "s")}.");
        }

        if (!hasher.Verify(password, user.PasswordHash, user.PasswordSalt))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= await settings.GetIntAsync("MaxFailedLogins", 5, cancellationToken))
            {
                user.LockoutEndUtc = now.AddMinutes(await settings.GetIntAsync("LockoutMinutes", 15, cancellationToken));
                user.FailedLoginCount = 0;
            }

            await users.UpdateAsync(user, cancellationToken);
            await AuditAsync(user.Id, username, false, "InvalidCredentials", now, cancellationToken);
            return Fail(GenericFailureMessage);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;
        await users.UpdateAsync(user, cancellationToken);
        await AuditAsync(user.Id, user.Username, true, null, now, cancellationToken);

        var timeout = TimeSpan.FromMinutes(await settings.GetIntAsync("SessionTimeoutMinutes", 20, cancellationToken));
        session.SignIn(user.Id, user.Username, user.Role, timeout);
        return new AuthenticationResult(true, user.Username, user.Role, null);
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        session.SignOut();
        return Task.CompletedTask;
    }

    private Task AuditAsync(Guid? userId, string username, bool succeeded, string? reason, DateTime now, CancellationToken cancellationToken) =>
        users.AddLoginAuditAsync(new LoginAuditRecord(userId, username, succeeded, reason, now), cancellationToken);

    private static AuthenticationResult Fail(string message) => new(false, null, null, message);
}
