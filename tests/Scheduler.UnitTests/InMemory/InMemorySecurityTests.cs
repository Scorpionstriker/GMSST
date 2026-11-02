using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;
using Scheduler.Modules.Security;
using Scheduler.Modules.Security.Authentication;

namespace Scheduler.UnitTests.InMemory;

/// <summary>The Login and Security module (FR-45 to FR-53) with accounts kept in memory.</summary>
public class InMemorySecurityTests
{
    private const string Password = "Correct horse 42";

    [Fact]
    public async Task FirstDeveloper_CanBeCreatedOnlyOnce_AndThenSignIn()
    {
        await using var host = CreateHost();
        var users = host.Get<UserManagement>();

        Assert.True(await users.NeedsFirstDeveloperAsync());
        Assert.False((await users.CreateFirstDeveloperAsync("admin", "short1", "short1")).Succeeded);
        Assert.True((await users.CreateFirstDeveloperAsync("admin", Password, Password)).Succeeded);
        Assert.False((await users.CreateFirstDeveloperAsync("other", Password, Password)).Succeeded);

        Assert.True((await host.Get<IAuthenticationProvider>().SignInAsync("ADMIN", Password)).Succeeded);
        Assert.True(host.Get<ICurrentUser>().IsAdministrator);
        var stored = await host.Users.FindByUsernameAsync("admin");
        Assert.DoesNotContain(Password, stored!.PasswordHash);
    }

    [Fact]
    public async Task FiveWrongPasswords_LockTheAccount_WithAGenericMessage()
    {
        await using var host = await SignedInAdministratorAsync();
        var auth = host.Get<IAuthenticationProvider>();
        Assert.True((await host.Get<UserManagement>().CreateUserAsync("viewer1", Password, Password, UserRole.Viewer)).Succeeded);

        var unknown = await auth.SignInAsync("nobody", "whatever");
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(unknown.Message, (await auth.SignInAsync("viewer1", "wrong password 1")).Message);
        }

        var locked = await auth.SignInAsync("viewer1", Password);
        Assert.False(locked.Succeeded);
        Assert.Equal(LocalAuthenticationProvider.GenericFailureMessage, locked.Message);
        Assert.Contains(host.Users.Audit, a => a is { UsernameAttempted: "viewer1", Reason: "LockedOut" });
        Assert.Contains(host.Users.Audit, a => a is { UsernameAttempted: "nobody", UserId: null, Reason: "InvalidCredentials" });
    }

    [Fact]
    public async Task LastAdministrator_CannotBeDeactivated()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        var self = (await users.ListAsync()).Single(u => u.Username == "admin");

        Assert.False((await users.UpdateUserAsync(self.Id, UserRole.Viewer, true)).Succeeded);
    }

    [Fact]
    public async Task ViewerSession_CannotManageUsers_ButCanChangeOwnPassword()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        await users.CreateUserAsync("viewer2", Password, Password, UserRole.Viewer);

        Assert.True((await host.Get<IAuthenticationProvider>().SignInAsync("viewer2", Password)).Succeeded);
        Assert.False((await users.CreateUserAsync("viewer3", Password, Password, UserRole.Viewer)).Succeeded);
        Assert.False((await users.ChangeOwnPasswordAsync("wrong", "New password 77", "New password 77")).Succeeded);
        Assert.True((await users.ChangeOwnPasswordAsync(Password, "New password 77", "New password 77")).Succeeded);
    }

    [Fact]
    public async Task RegistrationAndPasswordChanges_CreateInAppNotificationsForTheRequiredUsers()
    {
        await using var host = CreateHost();
        var management = host.Get<UserManagement>();
        Assert.True((await management.CreateFirstDeveloperAsync("admin", Password, Password)).Succeeded);
        var developer = (await management.ListAsync()).Single();

        Assert.True((await management.RegisterAsync("newviewer", Password, Password)).Succeeded);
        Assert.Contains(host.Notifications.Items, entry => entry.UserId == developer.Id
            && entry.Item.Title == "Account awaiting activation");

        var viewer = (await management.ListAsync()).Single(user => user.Username == "newviewer");
        Assert.True((await host.Get<IAuthenticationProvider>().SignInAsync("admin", Password)).Succeeded);
        Assert.True((await management.UpdateUserAsync(viewer.Id, UserRole.Viewer, true, viewer.Version)).Succeeded);
        Assert.True((await host.Get<IAuthenticationProvider>().SignInAsync("newviewer", Password)).Succeeded);
        Assert.True((await management.ChangeOwnPasswordAsync(Password, "New password 77", "New password 77")).Succeeded);
        Assert.Contains(host.Notifications.Items, entry => entry.UserId == viewer.Id && entry.Item.Title == "Password changed");
        Assert.Contains(host.Notifications.Items, entry => entry.UserId == developer.Id && entry.Item.Title == "Password changed");
    }

    [Fact]
    public async Task User_CanChangeOwnUsernameAfterEnteringCurrentPassword()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        await users.CreateUserAsync("viewer2", Password, Password, UserRole.Viewer);
        Assert.True((await host.Get<IAuthenticationProvider>().SignInAsync("viewer2", Password)).Succeeded);

        var wrongPassword = await users.ChangeOwnUsernameAsync("wrong password 1", "renamed");
        Assert.False(wrongPassword.Succeeded);
        Assert.Equal("Current", wrongPassword.Field);

        var changed = await users.ChangeOwnUsernameAsync(Password, " renamed ");
        Assert.True(changed.Succeeded);
        Assert.Equal("renamed", host.Get<ICurrentUser>().Username);
        Assert.NotNull(await host.Users.FindByUsernameAsync("renamed"));
        Assert.Null(await host.Users.FindByUsernameAsync("viewer2"));
        await host.Get<IAuthenticationProvider>().SignOutAsync();
        Assert.True((await host.Get<IAuthenticationProvider>().SignInAsync("renamed", Password)).Succeeded);
        Assert.Contains(host.Logger.Entries, entry => entry.Properties?["action"] as string == "UsernameChanged");
    }

    [Fact]
    public async Task User_CannotChangeOwnUsernameToInvalidOrTakenName()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        await users.CreateUserAsync("viewer2", Password, Password, UserRole.Viewer);
        await users.CreateUserAsync("viewer3", Password, Password, UserRole.Viewer);
        Assert.True((await host.Get<IAuthenticationProvider>().SignInAsync("viewer2", Password)).Succeeded);

        var duplicate = await users.ChangeOwnUsernameAsync(Password, "VIEWER3");
        var invalid = await users.ChangeOwnUsernameAsync(Password, "has spaces");

        Assert.Equal("Username", duplicate.Field);
        Assert.Equal("This username is already taken.", duplicate.Error);
        Assert.Equal("Username", invalid.Field);
        Assert.Null(await host.Users.FindByUsernameAsync("has spaces"));
    }

    [Fact]
    public async Task UserChanges_AreAudited_WithoutPasswords()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        Assert.True((await users.CreateUserAsync("audited", Password, Password, UserRole.Viewer)).Succeeded);
        var created = (await users.ListAsync()).Single(u => u.Username == "audited");
        Assert.True((await users.UpdateUserAsync(created.Id, UserRole.Administrator, false)).Succeeded);
        Assert.True((await users.ResetPasswordAsync(created.Id, "Another password 9", "Another password 9")).Succeeded);

        var security = host.Logger.Entries.Where(e => e.Source == LogSources.Security && e.Message.Contains("'audited'")).ToList();
        var actions = security.Select(e => e.Properties?["action"] as string).ToList();
        Assert.Contains("UserCreated", actions);
        Assert.Contains("RoleChanged", actions);
        Assert.Contains("UserDeactivated", actions);
        Assert.Contains("PasswordReset", actions);
        Assert.All(security, entry => Assert.Equal("Login and Security", entry.Properties?["application"]));
        Assert.All(security, e => Assert.Equal("admin", e.Properties?["user"]));
        Assert.DoesNotContain(security, e => e.Message.Contains("Another password 9") || e.Message.Contains(Password));
    }

    [Fact]
    public async Task DeleteUser_RemovesAccountAndUnlinksRetainedLoginAudit()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        Assert.True((await users.CreateUserAsync("delete-me", Password, Password, UserRole.Viewer)).Succeeded);
        var target = (await users.ListAsync()).Single(user => user.Username == "delete-me");
        await host.Users.AddLoginAuditAsync(new LoginAuditRecord(target.Id, target.Username, true, null, DateTime.UtcNow));

        var result = await users.DeleteUserAsync(target.Id, target.Version);

        Assert.True(result.Succeeded);
        Assert.Null(await host.Users.FindByIdAsync(target.Id));
        Assert.Contains(host.Users.Audit,
            record => record.UsernameAttempted == target.Username && record.UserId is null);
        Assert.Contains(host.Logger.Entries,
            entry => entry.Source == LogSources.Security && entry.Properties?["action"] as string == "UserDeleted");
    }

    [Fact]
    public async Task DeleteUser_RefusesSelfDeletionAndStaleVersions()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        var self = (await users.ListAsync()).Single(user => user.Username == "admin");
        Assert.Equal("You cannot delete your own account.", (await users.DeleteUserAsync(self.Id)).Error);

        Assert.True((await users.CreateUserAsync("stale-delete", Password, Password, UserRole.Viewer)).Succeeded);
        var opened = (await users.ListAsync()).Single(user => user.Username == "stale-delete");
        await host.Get<IAuthenticationProvider>().SignInAsync("stale-delete", "wrong password 1");
        await host.Get<IAuthenticationProvider>().SignInAsync("admin", Password);

        var result = await users.DeleteUserAsync(opened.Id, opened.Version);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Someone else changed this user", result.Error);
        Assert.NotNull(await host.Users.FindByIdAsync(opened.Id));
    }

    [Fact]
    public async Task Administrator_CannotDeleteDeveloperAccount()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        Assert.True((await users.CreateUserAsync("admin-user", Password, Password, UserRole.Administrator)).Succeeded);
        var developer = (await users.ListAsync()).Single(user => user.Role == UserRole.Developer);
        Assert.True((await host.Get<IAuthenticationProvider>().SignInAsync("admin-user", Password)).Succeeded);

        var result = await users.DeleteUserAsync(developer.Id, developer.Version);

        Assert.False(result.Succeeded);
        Assert.Equal("Only Developers can manage Developer accounts.", result.Error);
        Assert.NotNull(await host.Users.FindByIdAsync(developer.Id));
    }

    [Fact]
    public async Task EditingAUserThatChangedSinceItWasOpened_IsRefused()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        await users.CreateUserAsync("stale", Password, Password, UserRole.Viewer);
        var opened = (await users.ListAsync()).Single(u => u.Username == "stale");

        // Someone else changes the account (here: a failed sign-in) after the page was opened.
        await host.Get<IAuthenticationProvider>().SignInAsync("stale", "wrong password 1");
        await host.Get<IAuthenticationProvider>().SignInAsync("admin", Password);

        var result = await users.UpdateUserAsync(opened.Id, UserRole.Administrator, true, opened.Version);
        Assert.False(result.Succeeded);
        Assert.StartsWith("Someone else changed this user", result.Error);
        var fresh = (await users.ListAsync()).Single(u => u.Username == "stale");
        Assert.True((await users.UpdateUserAsync(fresh.Id, UserRole.Administrator, true, fresh.Version)).Succeeded);
    }

    [Fact]
    public async Task RegisteredAccount_IsAnInactiveViewer_UntilAnAdministratorActivatesIt()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        var auth = host.Get<IAuthenticationProvider>();
        await auth.SignOutAsync();

        var registered = await users.RegisterAsync("newcomer", Password, Password);
        Assert.True(registered.Succeeded);
        Assert.False(string.IsNullOrEmpty(registered.RecoveryCode));
        Assert.False((await users.RegisterAsync("NEWCOMER", Password, Password)).Succeeded);
        Assert.False((await auth.SignInAsync("newcomer", Password)).Succeeded);
        var stored = await host.Users.FindByUsernameAsync("newcomer");
        Assert.Equal(UserRole.Viewer, stored!.Role);
        Assert.DoesNotContain(registered.RecoveryCode!, stored.RecoveryCodeHash);

        Assert.True((await auth.SignInAsync("admin", Password)).Succeeded);
        Assert.True((await users.UpdateUserAsync(stored.Id, UserRole.Viewer, true, stored.Version)).Succeeded);
        Assert.True((await auth.SignInAsync("newcomer", Password)).Succeeded);
        Assert.False(host.Get<ICurrentUser>().IsAdministrator);
    }

    [Fact]
    public async Task Register_IsRefused_BeforeTheFirstAdministratorExists()
    {
        await using var host = CreateHost();
        Assert.False((await host.Get<UserManagement>().RegisterAsync("early", Password, Password)).Succeeded);
        Assert.True(await host.Get<UserManagement>().NeedsFirstDeveloperAsync());
        Assert.Contains(host.Logger.Entries,
            entry => entry.Source == LogSources.Security && entry.Properties?["action"] as string == "RegistrationRejected");
    }

    [Fact]
    public async Task PasswordRecoveryValidationFailures_AreAuditedWithoutCredentials()
    {
        await using var host = CreateHost();
        await host.Get<UserManagement>().CreateFirstDeveloperAsync("admin", Password, Password);
        var users = host.Get<UserManagement>();

        Assert.False((await users.RecoverPasswordAsync("admin", "RECOVERY-CODE", "short", "short")).Succeeded);
        Assert.False((await users.RecoverPasswordAsync("admin", "RECOVERY-CODE", "New password 77", "Different password 88")).Succeeded);

        var entries = host.Logger.Entries
            .Where(entry => entry.Source == LogSources.Security
                            && entry.Properties?["action"] as string == "PasswordRecoveryRejected")
            .ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.DoesNotContain("RECOVERY-CODE", entry.Message));
        Assert.All(entries, entry => Assert.DoesNotContain("New password 77", entry.Message));
        Assert.All(entries, entry => Assert.DoesNotContain("Different password 88", entry.Message));
    }

    [Fact]
    public async Task ForgotPassword_WithTheRecoveryCode_SetsANewPassword_AndTheCodeWorksOnce()
    {
        await using var host = CreateHost();
        var users = host.Get<UserManagement>();
        var code = (await users.CreateFirstDeveloperAsync("admin", Password, Password)).RecoveryCode!;
        const string NewPassword = "Brand new secret 8";

        // Typed by hand: lower case and spaces instead of dashes are accepted.
        var typed = code.Replace('-', ' ').ToLowerInvariant();
        var recovered = await users.RecoverPasswordAsync("admin", typed, NewPassword, NewPassword);
        Assert.True(recovered.Succeeded);
        Assert.NotEqual(code, recovered.RecoveryCode);

        var auth = host.Get<IAuthenticationProvider>();
        Assert.False((await auth.SignInAsync("admin", Password)).Succeeded);
        Assert.True((await auth.SignInAsync("admin", NewPassword)).Succeeded);
        Assert.False((await users.RecoverPasswordAsync("admin", code, Password, Password)).Succeeded);
        Assert.True((await users.RecoverPasswordAsync("admin", recovered.RecoveryCode!, Password, Password)).Succeeded);
        Assert.Contains(host.Logger.Entries, e => e.Source == LogSources.Security && e.Properties?["action"] as string == "PasswordRecovered");
    }

    [Fact]
    public async Task WrongRecoveryCodes_LockTheAccount_WithOneGenericMessage()
    {
        await using var host = CreateHost();
        var users = host.Get<UserManagement>();
        var code = (await users.CreateFirstDeveloperAsync("admin", Password, Password)).RecoveryCode!;

        var unknown = await users.RecoverPasswordAsync("nobody", code, "New password 77", "New password 77");
        Assert.Equal(UserManagement.RecoveryFailureMessage, unknown.Error);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(unknown.Error, (await users.RecoverPasswordAsync("admin", "AAAA-AAAA-AAAA-AAAA", "New password 77", "New password 77")).Error);
        }

        // Locked now: even the right code is refused, with the same message.
        Assert.Equal(unknown.Error, (await users.RecoverPasswordAsync("admin", code, "New password 77", "New password 77")).Error);
        Assert.Contains(host.Users.Audit, a => a is { UsernameAttempted: "admin", Reason: "RecoveryLockedOut" });
        Assert.False((await host.Get<IAuthenticationProvider>().SignInAsync("admin", Password)).Succeeded);
    }

    [Fact]
    public async Task NewRecoveryCode_ReplacesTheOldOne_AfterThePasswordIsEntered()
    {
        await using var host = await SignedInAdministratorAsync();
        var users = host.Get<UserManagement>();
        var first = (await users.CreateUserAsync("viewer4", Password, Password, UserRole.Viewer)).RecoveryCode!;
        await host.Get<IAuthenticationProvider>().SignInAsync("viewer4", Password);

        Assert.Equal("Current", (await users.CreateOwnRecoveryCodeAsync("wrong")).Field);
        var second = (await users.CreateOwnRecoveryCodeAsync(Password)).RecoveryCode!;
        Assert.True(await users.HasRecoveryCodeAsync());

        Assert.False((await users.RecoverPasswordAsync("viewer4", first, "New password 77", "New password 77")).Succeeded);
        Assert.True((await users.RecoverPasswordAsync("viewer4", second, "New password 77", "New password 77")).Succeeded);
    }

    [Theory]
    [InlineData("k7qm 2xnb r9td hw4p", "K7QM2XNBR9TDHW4P")]
    [InlineData(" K7QM-2XNB-R9TD-HW4P ", "K7QM2XNBR9TDHW4P")]
    [InlineData(null, "")]
    public void RecoveryCode_IsComparedWithoutCaseSpacesOrDashes(string? typed, string expected) =>
        Assert.Equal(expected, RecoveryCode.Normalize(typed));

    [Fact]
    public void RecoveryCode_HasFourGroupsOfFourEasyToReadCharacters()
    {
        var code = RecoveryCode.Generate();
        Assert.Matches("^[A-HJKMNP-Z2-9]{4}(-[A-HJKMNP-Z2-9]{4}){3}$", code);
        Assert.NotEqual(code, RecoveryCode.Generate());
    }

    private static InMemoryHost CreateHost() => new(services => new SecurityModule().Register(services));

    private static async Task<InMemoryHost> SignedInAdministratorAsync()
    {
        var host = CreateHost();
        Assert.True((await host.Get<UserManagement>().CreateFirstDeveloperAsync("admin", Password, Password)).Succeeded);
        Assert.True((await host.Get<IAuthenticationProvider>().SignInAsync("admin", Password)).Succeeded);
        return host;
    }
}
