using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Modules.Security.Authentication;

/// <summary>Outcome of a user management action; <see cref="Field"/> names the form field an error belongs to.</summary>
public sealed record UserActionResult(bool Succeeded, string? Error = null, string? Field = null)
{
    public static readonly UserActionResult Ok = new(true);
    public static UserActionResult Fail(string error, string? field = null) => new(false, error, field);
}

/// <summary>
/// First administrator, user maintenance and password changes (FR-50 to FR-52). Administrative actions require
/// the signed-in user to be an Administrator; the last active administrator cannot be removed or demoted. Every change
/// is written to the audit log with the administrator and the affected username, never the password (WP-4c).
/// </summary>
public sealed class UserManagement(IUserAccountStore users, PasswordHasher hasher, ICurrentUser session, IAuditLog audit, TimeProvider clock)
{
    public const int UsernameMaxLength = 50;

    public async Task<bool> NeedsFirstAdministratorAsync(CancellationToken cancellationToken = default) =>
        !await users.AnyUsersAsync(cancellationToken);

    /// <summary>Creates the first account at first start; refused once any account exists (FR-50).</summary>
    public async Task<UserActionResult> CreateFirstAdministratorAsync(string username, string password, string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (await users.AnyUsersAsync(cancellationToken))
        {
            return UserActionResult.Fail("An administrator already exists. Sign in instead.");
        }

        var result = await CreateAsync(username, password, confirmation, UserRole.Administrator, cancellationToken);
        if (result.Succeeded)
        {
            await audit.RecordAsync("UserCreated", $"First administrator '{username.Trim()}' created.", cancellationToken);
        }

        return result;
    }

    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default) => users.ListAsync(cancellationToken);

    public async Task<UserActionResult> CreateUserAsync(string username, string password, string confirmation, UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (!session.IsAdministrator)
        {
            return Forbidden;
        }

        var result = await CreateAsync(username, password, confirmation, role, cancellationToken);
        if (result.Succeeded)
        {
            await audit.RecordAsync("UserCreated", $"{Actor} created user '{username.Trim()}' with role {role}.", cancellationToken);
        }

        return result;
    }

    /// <param name="expectedVersion">The version the administrator saw; the change is refused if the account changed since (F-27).</param>
    public async Task<UserActionResult> UpdateUserAsync(Guid userId, UserRole role, bool isActive, uint? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (!session.IsAdministrator)
        {
            return Forbidden;
        }

        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserActionResult.Fail("The user no longer exists.");
        }

        if (expectedVersion is { } seen && seen != user.Version)
        {
            return Changed;
        }

        if (user.Id == session.UserId && (!isActive || role != UserRole.Administrator))
        {
            return UserActionResult.Fail("You cannot deactivate your own account or remove your own administrator role.");
        }

        var removesAdministrator = user is { Role: UserRole.Administrator, IsActive: true } && (role != UserRole.Administrator || !isActive);
        if (removesAdministrator && (await users.ListAsync(cancellationToken)).Count(u => u is { Role: UserRole.Administrator, IsActive: true }) <= 1)
        {
            return UserActionResult.Fail("At least one active administrator is required.");
        }

        var changes = new List<(string Action, string Message)>();
        if (user.Role != role)
        {
            changes.Add(("RoleChanged", $"{Actor} changed the role of '{user.Username}' from {user.Role} to {role}."));
        }

        if (user.IsActive != isActive)
        {
            changes.Add(isActive
                ? ("UserReactivated", $"{Actor} reactivated user '{user.Username}'.")
                : ("UserDeactivated", $"{Actor} deactivated user '{user.Username}'."));
        }

        if (isActive && user.LockoutEndUtc is not null)
        {
            changes.Add(("UserUnlocked", $"{Actor} unlocked user '{user.Username}'."));
        }

        user.Role = role;
        user.IsActive = isActive;
        if (isActive)
        {
            user.LockoutEndUtc = null;
            user.FailedLoginCount = 0;
        }

        try
        {
            await users.UpdateAsync(user, cancellationToken);
        }
        catch (UserAccountConflictException)
        {
            return Changed;
        }

        foreach (var (action, message) in changes)
        {
            await audit.RecordAsync(action, message, cancellationToken);
        }

        return UserActionResult.Ok;
    }

    public async Task<UserActionResult> ResetPasswordAsync(Guid userId, string password, string confirmation, uint? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (!session.IsAdministrator)
        {
            return Forbidden;
        }

        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserActionResult.Fail("The user no longer exists.");
        }

        if (expectedVersion is { } seen && seen != user.Version)
        {
            return Changed;
        }

        if (CheckPassword(password, confirmation, user.Username) is { } problem)
        {
            return problem;
        }

        SetPassword(user, password);
        user.LockoutEndUtc = null;
        user.FailedLoginCount = 0;
        try
        {
            await users.UpdateAsync(user, cancellationToken);
        }
        catch (UserAccountConflictException)
        {
            return Changed;
        }

        await audit.RecordAsync("PasswordReset", $"{Actor} reset the password of '{user.Username}'.", cancellationToken);
        return UserActionResult.Ok;
    }

    /// <summary>Any signed-in user can change their own password after entering the current one (FR-51).</summary>
    public async Task<UserActionResult> ChangeOwnPasswordAsync(string currentPassword, string password, string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (session.UserId is not { } userId || await users.FindByIdAsync(userId, cancellationToken) is not { } user)
        {
            return UserActionResult.Fail("Sign in again to change your password.");
        }

        if (!hasher.Verify(currentPassword ?? string.Empty, user.PasswordHash, user.PasswordSalt))
        {
            return UserActionResult.Fail("The current password is incorrect.", "Current");
        }

        if (CheckPassword(password, confirmation, user.Username) is { } problem)
        {
            return problem;
        }

        SetPassword(user, password);
        await users.UpdateAsync(user, cancellationToken);
        await audit.RecordAsync("PasswordChanged", $"'{user.Username}' changed their own password.", cancellationToken);
        return UserActionResult.Ok;
    }

    private string Actor => session.Username is { } name ? $"Administrator '{name}'" : "An administrator";

    private static readonly UserActionResult Changed =
        UserActionResult.Fail("Someone else changed this user after you opened it. Reload the page and make your change again.");

    private static readonly UserActionResult Forbidden = UserActionResult.Fail("Only administrators can manage users.");

    private async Task<UserActionResult> CreateAsync(string username, string password, string confirmation, UserRole role,
        CancellationToken cancellationToken)
    {
        username = (username ?? string.Empty).Trim();
        if (username.Length is 0 or > UsernameMaxLength || username.Any(char.IsWhiteSpace))
        {
            return UserActionResult.Fail($"Enter a username of 1 to {UsernameMaxLength} characters without spaces.", "Username");
        }

        if (await users.UsernameExistsAsync(username, null, cancellationToken))
        {
            return UserActionResult.Fail("This username is already taken.", "Username");
        }

        if (CheckPassword(password, confirmation, username) is { } problem)
        {
            return problem;
        }

        var hash = hasher.Hash(password);
        await users.CreateAsync(new UserAccount
        {
            Username = username,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            Role = role,
            IsActive = true,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        }, cancellationToken);
        return UserActionResult.Ok;
    }

    private static UserActionResult? CheckPassword(string password, string confirmation, string username)
    {
        if (PasswordPolicy.Validate(password, username) is { } error)
        {
            return UserActionResult.Fail(error, "Password");
        }

        return password != confirmation ? UserActionResult.Fail("The passwords do not match.", "Confirmation") : null;
    }

    private void SetPassword(UserAccount user, string password)
    {
        var hash = hasher.Hash(password);
        user.PasswordHash = hash.Hash;
        user.PasswordSalt = hash.Salt;
    }
}
