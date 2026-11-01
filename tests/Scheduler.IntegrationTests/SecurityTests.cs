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
        await EnsureDeveloperAsync(services);
        var auth = services.GetRequiredService<IAuthenticationProvider>();
        var users = services.GetRequiredService<UserManagement>();
        await SignInAsDeveloperAsync(services);
        Assert.True((await users.CreateUserAsync("viewer1", Password, Password, UserRole.Viewer)).Succeeded);

        var unknown = await auth.SignInAsync("nobody", "whatever");
        var wrong = await auth.SignInAsync("viewer1", "wrong password 1");
        Assert.Equal(unknown.Message, wrong.Message);

        for (var i = 0; i < 4; i++)
        {
            await auth.SignInAsync("viewer1", "wrong password 1");
        }

        // WP-4b: a locked account gets the same message as a wrong password.
        var locked = await auth.SignInAsync("viewer1", Password);
        Assert.False(locked.Succeeded);
        Assert.Equal(LocalAuthenticationProvider.GenericFailureMessage, locked.Message);

        await using var db = database.CreateContext();
        var audits = await db.LoginAudits.Where(a => a.UsernameAttempted == "viewer1").ToListAsync();
        Assert.Equal(6, audits.Count);
        Assert.Contains(audits, a => a.Reason == "LockedOut");
        Assert.Contains(await db.LoginAudits.ToListAsync(), a => a is { UsernameAttempted: "nobody", UserId: null, Reason: "InvalidCredentials" });
    }

    [SkippableFact]
    public async Task LastDeveloper_CannotBeDemoted()
    {
        RequiresDatabase.Check();
        var services = CreateServices();
        await EnsureDeveloperAsync(services);
        await SignInAsDeveloperAsync(services);
        var users = services.GetRequiredService<UserManagement>();
        var self = (await users.ListAsync()).Single(u => u.Username == "admin");

        Assert.False((await users.UpdateUserAsync(self.Id, UserRole.Administrator, true)).Succeeded);
    }

    [SkippableFact]
    public async Task ViewerSession_CannotManageUsers_ButCanChangeOwnPassword()
    {
        RequiresDatabase.Check();
        var services = CreateServices();
        await EnsureDeveloperAsync(services);
        await SignInAsDeveloperAsync(services);
        var users = services.GetRequiredService<UserManagement>();
        await users.CreateUserAsync("viewer2", Password, Password, UserRole.Viewer);

        Assert.True((await services.GetRequiredService<IAuthenticationProvider>().SignInAsync("viewer2", Password)).Succeeded);
        Assert.False((await users.CreateUserAsync("viewer3", Password, Password, UserRole.Viewer)).Succeeded);
        Assert.False((await users.ChangeOwnPasswordAsync("wrong", "New password 77", "New password 77")).Succeeded);
        Assert.True((await users.ChangeOwnPasswordAsync(Password, "New password 77", "New password 77")).Succeeded);
    }

    [SkippableFact]
    public async Task Administrator_CanManageSchedulerUsers_ButNotDeveloperAccounts()
    {
        RequiresDatabase.Check();
        var services = CreateServices();
        await EnsureDeveloperAsync(services);
        await SignInAsDeveloperAsync(services);
        var users = services.GetRequiredService<UserManagement>();
        Assert.True((await users.CreateUserAsync("admin1", Password, Password, UserRole.Administrator)).Succeeded);

        Assert.True((await services.GetRequiredService<IAuthenticationProvider>().SignInAsync("admin1", Password)).Succeeded);
        var current = services.GetRequiredService<ICurrentUser>();
        Assert.True(current.IsAdministrator);
        Assert.False(current.IsDeveloper);
        Assert.True((await users.CreateUserAsync("viewer3", Password, Password, UserRole.Viewer)).Succeeded);
        Assert.False((await users.CreateUserAsync("dev2", Password, Password, UserRole.Developer)).Succeeded);

        var developer = (await users.ListAsync()).Single(u => u.Role == UserRole.Developer);
        Assert.False((await users.UpdateUserAsync(developer.Id, UserRole.Administrator, true)).Succeeded);
        Assert.False((await users.ResetPasswordAsync(developer.Id, "New password 77", "New password 77")).Succeeded);
    }

    [SkippableFact]
    public async Task UserChanges_AreAudited_AndShownInTheSecurityReport()
    {
        RequiresDatabase.Check();
        var services = CreateServices();
        await EnsureDeveloperAsync(services);
        await SignInAsDeveloperAsync(services);
        var users = services.GetRequiredService<UserManagement>();
        Assert.True((await users.CreateUserAsync("audited", Password, Password, UserRole.Viewer)).Succeeded);
        var created = (await users.ListAsync()).Single(u => u.Username == "audited");
        Assert.True((await users.UpdateUserAsync(created.Id, UserRole.Administrator, false)).Succeeded);
        Assert.True((await users.ResetPasswordAsync(created.Id, "Another password 9", "Another password 9")).Succeeded);

        var report = await services.GetRequiredService<Scheduler.Application.Common.IQueryHandler<Scheduler.Application.Reports.GenerateReportQuery, Scheduler.Application.Reports.ReportResult>>()
            .HandleAsync(new Scheduler.Application.Reports.GenerateReportQuery(Scheduler.Application.Reports.ReportType.SecurityAudit, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), null, null));
        var events = report.Value!.Rows.Where(r => r[1] == "Login and Security" && r[2] == "admin"
                                                    && r[4]?.Contains("'audited'") == true)
            .Select(r => r[3]).ToList();
        Assert.Contains("UserCreated", events);
        Assert.Contains("RoleChanged", events);
        Assert.Contains("UserDeactivated", events);
        Assert.Contains("PasswordReset", events);

        await using var db = database.CreateContext();
        Assert.DoesNotContain(await db.ExecutionLogs.Where(l => l.Source == "Security").Select(l => l.Message).ToListAsync(),
            m => m.Contains("Another password 9") || m.Contains(Password));
    }

    [SkippableFact]
    public async Task EditingAUserThatChangedSinceItWasOpened_IsRefused()
    {
        RequiresDatabase.Check();
        var services = CreateServices();
        await EnsureDeveloperAsync(services);
        await SignInAsDeveloperAsync(services);
        var users = services.GetRequiredService<UserManagement>();
        await users.CreateUserAsync("stale", Password, Password, UserRole.Viewer);
        var opened = (await users.ListAsync()).Single(u => u.Username == "stale");

        // Someone else changes the account (here: a failed sign-in) after the page was opened.
        await services.GetRequiredService<IAuthenticationProvider>().SignInAsync("stale", "wrong password 1");
        await SignInAsDeveloperAsync(services);

        var result = await users.UpdateUserAsync(opened.Id, UserRole.Administrator, true, opened.Version);
        Assert.False(result.Succeeded);
        Assert.StartsWith("Someone else changed this user", result.Error);

        var fresh = (await users.ListAsync()).Single(u => u.Username == "stale");
        Assert.True((await users.UpdateUserAsync(fresh.Id, UserRole.Administrator, true, fresh.Version)).Succeeded);
    }

    private ServiceProvider CreateServices() =>
        database.CreateServices("security", configure: services => new SecurityModule().Register(services));

    private static async Task EnsureDeveloperAsync(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManagement>();
        if (await users.NeedsFirstDeveloperAsync())
        {
            await users.CreateFirstDeveloperAsync("admin", Password, Password);
        }
    }

    private static async Task SignInAsDeveloperAsync(IServiceProvider services) =>
        Assert.True((await services.GetRequiredService<IAuthenticationProvider>().SignInAsync("admin", Password)).Succeeded);
}

/// <summary>FR-50: runs in its own empty database, because it needs a database without users.</summary>
public sealed class FirstRunTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string Password = "Correct horse 42";

    [SkippableFact]
    public async Task FirstDeveloper_CanBeCreatedOnlyOnce_AndThenSignIn()
    {
        RequiresDatabase.Check();
        var services = CreateServices();
        var users = services.GetRequiredService<UserManagement>();

        Assert.True(await users.NeedsFirstDeveloperAsync());
        Assert.False((await users.CreateFirstDeveloperAsync("admin", "short1", "short1")).Succeeded);
        Assert.True((await users.CreateFirstDeveloperAsync("admin", Password, Password)).Succeeded);
        Assert.False((await users.CreateFirstDeveloperAsync("other", Password, Password)).Succeeded);

        var result = await services.GetRequiredService<IAuthenticationProvider>().SignInAsync("ADMIN", Password);
        Assert.True(result.Succeeded);
        var current = services.GetRequiredService<ICurrentUser>();
        Assert.True(current.IsAdministrator);
        Assert.True(current.IsDeveloper);
        Assert.Equal("admin", current.Username);

        await using var db = database.CreateContext();
        var stored = await db.AppUsers.SingleAsync(u => u.Username == "admin");
        Assert.Equal(UserRole.Developer, stored.Role);
        Assert.DoesNotContain(Password, stored.PasswordHash);
        Assert.StartsWith("600000.", stored.PasswordHash);
        Assert.Equal(24, stored.PasswordSalt.Length); // 128-bit salt in Base64.
    }

    private ServiceProvider CreateServices() =>
        database.CreateServices("first-run", configure: services => new SecurityModule().Register(services));
}
