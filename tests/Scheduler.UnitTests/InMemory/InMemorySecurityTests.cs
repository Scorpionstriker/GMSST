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
    public async Task FirstAdministrator_CanBeCreatedOnlyOnce_AndThenSignIn()
    {
        await using var host = CreateHost();
        var users = host.Get<UserManagement>();

        Assert.True(await users.NeedsFirstAdministratorAsync());
        Assert.False((await users.CreateFirstAdministratorAsync("admin", "short1", "short1")).Succeeded);
        Assert.True((await users.CreateFirstAdministratorAsync("admin", Password, Password)).Succeeded);
        Assert.False((await users.CreateFirstAdministratorAsync("other", Password, Password)).Succeeded);

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
        Assert.All(security, e => Assert.Equal("admin", e.Properties?["user"]));
        Assert.DoesNotContain(security, e => e.Message.Contains("Another password 9") || e.Message.Contains(Password));
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

    private static InMemoryHost CreateHost() => new(services => new SecurityModule().Register(services));

    private static async Task<InMemoryHost> SignedInAdministratorAsync()
    {
        var host = CreateHost();
        Assert.True((await host.Get<UserManagement>().CreateFirstAdministratorAsync("admin", Password, Password)).Succeeded);
        Assert.True((await host.Get<IAuthenticationProvider>().SignInAsync("admin", Password)).Succeeded);
        return host;
    }
}
