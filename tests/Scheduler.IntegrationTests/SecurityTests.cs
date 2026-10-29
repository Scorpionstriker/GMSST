using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;
using Scheduler.Modules.Security;
using Scheduler.Modules.Security.Authentication;

namespace Scheduler.IntegrationTests;

/// <summary>FR-45 to FR-53 with the real account store.</summary>
public sealed class SecurityTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string Password = "Correct horse 42";

    [SkippableFact]
    public async Task FiveWrongPasswords_LockTheAccount_WithAGenericMessageForUnknownUsers()
    {
        RequiresDatabase.Check();
        var services = CreateServices();
        await EnsureAdministratorAsync(services);
        var auth = services.GetRequiredService<IAuthenticationProvider>();
        var users = services.GetRequiredService<UserManagement>();
        await SignInAsAdministratorAsync(services);
        Assert.True((await users.CreateUserAsync("viewer1", Password, Password, UserRole.Viewer)).Succeeded);

        var unknown = await auth.SignInAsync("nobody", "whatever");
        var wrong = await auth.SignInAsync("viewer1", "wrong password 1");
        Assert.Equal(unknown.Message, wrong.Message);

        for (var i = 0; i < 4; i++)
        {
            await auth.SignInAsync("viewer1", "wrong password 1");
        }

        var locked = await auth.SignInAsync("viewer1", Password);
        Assert.False(locked.Succeeded);
        Assert.StartsWith("Account locked", locked.Message);

        await using var db = database.CreateContext();
        var audits = await db.LoginAudits.Where(a => a.UsernameAttempted == "viewer1").ToListAsync();
        Assert.Equal(6, audits.Count);
        Assert.Contains(audits, a => a.Reason == "LockedOut");
        Assert.Contains(await db.LoginAudits.ToListAsync(), a => a is { UsernameAttempted: "nobody", UserId: null, Reason: "InvalidCredentials" });
    }

    [SkippableFact]
    public async Task LastAdministrator_CannotBeDeactivated()
    {
        RequiresDatabase.Check();
        var services = CreateServices();
        await EnsureAdministratorAsync(services);
        await SignInAsAdministratorAsync(services);
        var users = services.GetRequiredService<UserManagement>();
        var self = (await users.ListAsync()).Single(u => u.Username == "admin");

        Assert.False((await users.UpdateUserAsync(self.Id, UserRole.Viewer, true)).Succeeded);
    }

    [SkippableFact]
    public async Task ViewerSession_CannotManageUsers_ButCanChangeOwnPassword()
    {
        RequiresDatabase.Check();
        var services = CreateServices();
        await EnsureAdministratorAsync(services);
        await SignInAsAdministratorAsync(services);
        var users = services.GetRequiredService<UserManagement>();
        await users.CreateUserAsync("viewer2", Password, Password, UserRole.Viewer);

        Assert.True((await services.GetRequiredService<IAuthenticationProvider>().SignInAsync("viewer2", Password)).Succeeded);
        Assert.False((await users.CreateUserAsync("viewer3", Password, Password, UserRole.Viewer)).Succeeded);
        Assert.False((await users.ChangeOwnPasswordAsync("wrong", "New password 77", "New password 77")).Succeeded);
        Assert.True((await users.ChangeOwnPasswordAsync(Password, "New password 77", "New password 77")).Succeeded);
    }

    private ServiceProvider CreateServices() =>
        database.CreateServices("security", configure: services => new SecurityModule().Register(services));

    private static async Task EnsureAdministratorAsync(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManagement>();
        if (await users.NeedsFirstAdministratorAsync())
        {
            await users.CreateFirstAdministratorAsync("admin", Password, Password);
        }
    }

    private static async Task SignInAsAdministratorAsync(IServiceProvider services) =>
        Assert.True((await services.GetRequiredService<IAuthenticationProvider>().SignInAsync("admin", Password)).Succeeded);
}

/// <summary>FR-50: runs in its own empty database, because it needs a database without users.</summary>
public sealed class FirstRunTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string Password = "Correct horse 42";

    [SkippableFact]
    public async Task FirstAdministrator_CanBeCreatedOnlyOnce_AndThenSignIn()
    {
        RequiresDatabase.Check();
        var services = CreateServices();
        var users = services.GetRequiredService<UserManagement>();

        Assert.True(await users.NeedsFirstAdministratorAsync());
        Assert.False((await users.CreateFirstAdministratorAsync("admin", "short1", "short1")).Succeeded);
        Assert.True((await users.CreateFirstAdministratorAsync("admin", Password, Password)).Succeeded);
        Assert.False((await users.CreateFirstAdministratorAsync("other", Password, Password)).Succeeded);

        var result = await services.GetRequiredService<IAuthenticationProvider>().SignInAsync("ADMIN", Password);
        Assert.True(result.Succeeded);
        var current = services.GetRequiredService<ICurrentUser>();
        Assert.True(current.IsAdministrator);
        Assert.Equal("admin", current.Username);

        await using var db = database.CreateContext();
        var stored = await db.AppUsers.SingleAsync(u => u.Username == "admin");
        Assert.DoesNotContain(Password, stored.PasswordHash);
        Assert.StartsWith("600000.", stored.PasswordHash);
        Assert.Equal(24, stored.PasswordSalt.Length); // 128-bit salt in Base64.
    }

    private ServiceProvider CreateServices() =>
        database.CreateServices("first-run", configure: services => new SecurityModule().Register(services));
}
