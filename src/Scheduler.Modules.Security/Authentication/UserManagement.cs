using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Modules.Security.Authentication;

/// <summary>
/// Outcome of a user management action; <see cref="Field"/> names the form field an error belongs to. A new
/// <see cref="RecoveryCode"/> is returned once, to be shown to the user, and is never stored in plain text.
/// </summary>
public sealed record UserActionResult(bool Succeeded, string? Error = null, string? Field = null, string? RecoveryCode = null)
{
    public static readonly UserActionResult Ok = new(true);
    public static UserActionResult Fail(string error, string? field = null) => new(false, error, field);
}

/// <summary>
/// First developer, user maintenance and password changes (FR-50 to FR-52). Developer accounts are super-admin
/// accounts: only Developers can create, edit or reset a Developer account, and one active Developer must remain.
/// The service also supports registration, password recovery and audited user changes.
/// </summary>
public sealed class UserManagement(
    IUserAccountStore users,
    PasswordHasher hasher,
    ICurrentUser session,
    UserSession userSession,
    IAuditLog audit,
    ISettingsReader settings,
    TimeProvider clock)
{
    public const int UsernameMaxLength = 50;

    /// <summary>The only message a failed recovery shows, so it cannot tell which part was wrong or whether the user exists.</summary>
    public const string RecoveryFailureMessage = "The username or recovery code is incorrect, or the account cannot be recovered.";

    public async Task<bool> NeedsFirstDeveloperAsync(CancellationToken cancellationToken = default) =>
        !await users.AnyUsersAsync(cancellationToken);

    /// <summary>Creates the first, super-admin account at first start; refused once any account exists (FR-50).</summary>
    public async Task<UserActionResult> CreateFirstDeveloperAsync(string username, string password, string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (await users.AnyUsersAsync(cancellationToken))
        {
            return UserActionResult.Fail("A user account already exists. Sign in instead.");
        }

        var result = await CreateAsync(username, password, confirmation, UserRole.Developer, true, cancellationToken);
        if (result.Succeeded)
        {
            await audit.RecordAsync("UserCreated", $"First Developer '{username.Trim()}' created.", cancellationToken);
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

        if (!Enum.IsDefined(role))
        {
            return UserActionResult.Fail("Select a valid role.", "Role");
        }

        if (role == UserRole.Developer && !session.IsDeveloper)
        {
            return DeveloperOnly;
        }

        var result = await CreateAsync(username, password, confirmation, role, true, cancellationToken);
        if (result.Succeeded)
        {
            await audit.RecordAsync("UserCreated", $"{Actor} created user '{username.Trim()}' with role {role}.", cancellationToken);
        }
        return result;
    }

    /// <summary>
    /// Anyone at the Login page can ask for an account. It is created as an inactive Viewer, so it cannot sign in until
    /// an administrator activates it on the Users page (and may make it an Administrator).
    /// </summary>
    public async Task<UserActionResult> RegisterAsync(string username, string password, string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (!await users.AnyUsersAsync(cancellationToken))
        {
            return UserActionResult.Fail("No administrator exists yet. Create the first administrator on the Login page instead.");
        }

        var result = await CreateAsync(username, password, confirmation, UserRole.Viewer, false, cancellationToken);
        if (result.Succeeded)
        {
            await audit.RecordAsync("UserRegistered",
                $"User '{username.Trim()}' registered and is waiting for an administrator to activate the account.", cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// "Forgot password": sets a new password when the username and recovery code match an active account. A wrong code
    /// counts as a failed sign-in, so the same lockout applies (FR-47). The code works once; a new one is returned.
    /// </summary>
    public async Task<UserActionResult> RecoverPasswordAsync(string username, string recoveryCode, string password, string confirmation,
        CancellationToken cancellationToken = default)
    {
        username = (username ?? string.Empty).Trim();
        var code = RecoveryCode.Normalize(recoveryCode);
        if (PasswordPolicy.Validate(password, username) is { } policyError)
        {
            return UserActionResult.Fail(policyError, "Password");
        }

        if (password != confirmation)
        {
            return UserActionResult.Fail("The passwords do not match.", "Confirmation");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var user = username.Length == 0 ? null : await users.FindByUsernameAsync(username, cancellationToken);
        if (user is null || !user.IsActive || user.LockoutEndUtc > now || user.RecoveryCodeHash is null || user.RecoveryCodeSalt is null)
        {
            // The same time and message whatever the reason, as for sign-in (Algorithm 6.13).
            hasher.VerifyDummy(code);
            var reason = user is null ? "RecoveryInvalid" : !user.IsActive ? "RecoveryInactive" : user.LockoutEndUtc > now ? "RecoveryLockedOut" : "RecoveryNoCode";
            await users.AddLoginAuditAsync(new LoginAuditRecord(user?.Id, username, false, reason, now), cancellationToken);
            return UserActionResult.Fail(RecoveryFailureMessage);
        }

        if (!hasher.Verify(code, user.RecoveryCodeHash, user.RecoveryCodeSalt))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= await settings.GetIntAsync("MaxFailedLogins", 5, cancellationToken))
            {
                user.LockoutEndUtc = now.AddMinutes(await settings.GetIntAsync("LockoutMinutes", 15, cancellationToken));
                user.FailedLoginCount = 0;
            }

            await users.UpdateAsync(user, cancellationToken);
            await users.AddLoginAuditAsync(new LoginAuditRecord(user.Id, username, false, "RecoveryInvalid", now), cancellationToken);
            return UserActionResult.Fail(RecoveryFailureMessage);
        }

        SetPassword(user, password);
        var newCode = SetRecoveryCode(user);
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        try
        {
            await users.UpdateAsync(user, cancellationToken);
        }
        catch (UserAccountConflictException)
        {
            return UserActionResult.Fail("The account changed while the password was being reset. Try again.");
        }

        await audit.RecordAsync("PasswordRecovered", $"'{user.Username}' set a new password with their recovery code.", cancellationToken);
        return UserActionResult.Ok with { RecoveryCode = newCode };
    }

    /// <summary>Replaces the signed-in user's recovery code after they enter their password; the old code stops working.</summary>
    public async Task<UserActionResult> CreateOwnRecoveryCodeAsync(string currentPassword, CancellationToken cancellationToken = default)
    {
        if (session.UserId is not { } userId || await users.FindByIdAsync(userId, cancellationToken) is not { } user)
        {
            return UserActionResult.Fail("Sign in again to create a recovery code.");
        }

        if (!hasher.Verify(currentPassword ?? string.Empty, user.PasswordHash, user.PasswordSalt))
        {
            return UserActionResult.Fail("The current password is incorrect.", "Current");
        }

        var code = SetRecoveryCode(user);
        await users.UpdateAsync(user, cancellationToken);
        await audit.RecordAsync("RecoveryCodeCreated", $"'{user.Username}' created a new recovery code.", cancellationToken);
        return UserActionResult.Ok with { RecoveryCode = code };
    }

    /// <summary>Whether the signed-in user has a recovery code, for the Account page.</summary>
    public async Task<bool> HasRecoveryCodeAsync(CancellationToken cancellationToken = default) =>
        session.UserId is { } userId && await users.FindByIdAsync(userId, cancellationToken) is { RecoveryCodeHash: not null };

    /// <param name="expectedVersion">The version the administrator saw; the change is refused if the account changed since (F-27).</param>
    public async Task<UserActionResult> UpdateUserAsync(Guid userId, UserRole role, bool isActive, uint? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (!session.IsAdministrator)
        {
            return Forbidden;
        }

        if (!Enum.IsDefined(role))
        {
            return UserActionResult.Fail("Select a valid role.", "Role");
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

        if (user.Role == UserRole.Developer && !session.IsDeveloper)
        {
            return DeveloperOnly;
        }

        if (role == UserRole.Developer && !session.IsDeveloper)
        {
            return DeveloperOnly;
        }

        if (user.Id == session.UserId && (!isActive || role != session.Role))
        {
            return UserActionResult.Fail("You cannot deactivate your own account or remove your own role.");
        }

        if (role == UserRole.Developer && !session.IsDeveloper)
        {
            return DeveloperOnly;
        }

        if (user.Id == session.UserId && (!isActive || role != session.Role))
        {
            return UserActionResult.Fail("You cannot deactivate your own account or remove your own role.");
        }

        var removesDeveloper = user is { Role: UserRole.Developer, IsActive: true } && (role != UserRole.Developer || !isActive);
        if (removesDeveloper && (await users.ListAsync(cancellationToken)).Count(u => u is { Role: UserRole.Developer, IsActive: true }) <= 1)
        {
            return UserActionResult.Fail("At least one active Developer is required.");
        }

        var changes = new List<(string Action, string Message)>();
        if (user.Role != role)
        {
            changes.Add(("RoleChanged", $"{Actor} changed the role of '{user.Username}' from {user.Role} to {role}."));
        }

        if (user.IsActive != isActive)
        {
            changes.Add(isActive
                ? user.LastLoginAtUtc is null
                    ? ("UserActivated", $"{Actor} activated user '{user.Username}'.")
                    : ("UserReactivated", $"{Actor} reactivated user '{user.Username}'.")
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

        if (user.Role == UserRole.Developer && !session.IsDeveloper)
        {
            return DeveloperOnly;
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

    /// <summary>Changes the signed-in user's username after they enter their current password.</summary>
    public async Task<UserActionResult> ChangeOwnUsernameAsync(string currentPassword, string username,
        CancellationToken cancellationToken = default)
    {
        if (session.UserId is not { } userId || await users.FindByIdAsync(userId, cancellationToken) is not { } user)
        {
            return UserActionResult.Fail("Sign in again to change your username.");
        }

        if (!hasher.Verify(currentPassword ?? string.Empty, user.PasswordHash, user.PasswordSalt))
        {
            return UserActionResult.Fail("The current password is incorrect.", "Current");
        }

        username = NormalizeUsername(username);
        if (ValidateUsername(username) is { } usernameProblem)
        {
            return usernameProblem;
        }

        if (string.Equals(user.Username, username, StringComparison.Ordinal))
        {
            return UserActionResult.Ok;
        }

        if (await users.UsernameExistsAsync(username, userId, cancellationToken))
        {
            return UserActionResult.Fail("This username is already taken.", "Username");
        }

        var oldUsername = user.Username;
        user.Username = username;
        try
        {
            await users.UpdateAsync(user, cancellationToken);
        }
        catch (UserAccountConflictException)
        {
            return Changed;
        }
        catch (UserAccountNameConflictException)
        {
            return UserActionResult.Fail("This username is already taken.", "Username");
        }

        userSession.UpdateUsername(userId, username);
        await audit.RecordAsync("UsernameChanged", $"'{oldUsername}' changed their username to '{username}'.", cancellationToken);
        return UserActionResult.Ok;
    }

    private string Actor => session.Username is { } name ? $"Administrator '{name}'" : "An administrator";

    private static readonly UserActionResult Changed =
        UserActionResult.Fail("Someone else changed this user after you opened it. Reload the page and make your change again.");

    private static readonly UserActionResult Forbidden = UserActionResult.Fail("Only administrators can manage users.");
    private static readonly UserActionResult DeveloperOnly = UserActionResult.Fail("Only Developers can manage Developer accounts.");

    private async Task<UserActionResult> CreateAsync(string username, string password, string confirmation, UserRole role, bool isActive,
        CancellationToken cancellationToken)
    {
        username = NormalizeUsername(username);
        if (ValidateUsername(username) is { } usernameProblem)
        {
            return usernameProblem;
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
        var user = new UserAccount
        {
            Username = username,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            Role = role,
            IsActive = isActive,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        var code = SetRecoveryCode(user);
        await users.CreateAsync(user, cancellationToken);
        return UserActionResult.Ok with { RecoveryCode = code };
    }

    private static string NormalizeUsername(string? username) => (username ?? string.Empty).Trim();

    private static UserActionResult? ValidateUsername(string username) =>
        username.Length is 0 or > UsernameMaxLength || username.Any(char.IsWhiteSpace)
            ? UserActionResult.Fail($"Enter a username of 1 to {UsernameMaxLength} characters without spaces.", "Username")
            : null;

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

    private string SetRecoveryCode(UserAccount user)
    {
        var code = RecoveryCode.Generate();
        var hash = hasher.Hash(RecoveryCode.Normalize(code));
        user.RecoveryCodeHash = hash.Hash;
        user.RecoveryCodeSalt = hash.Salt;
        return code;
    }
}
