using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application;
using Scheduler.Application.Common;
using Scheduler.Domain.Enums;
using Scheduler.Engine;
using Scheduler.Extensibility.Contracts;
using Scheduler.Modules.Security;
using Scheduler.Modules.Security.Authentication;

namespace Scheduler.UnitTests;

/// <summary>WP-4a: the four combinations of the Login and Security module and the developer login bypass.</summary>
public sealed class LoginBypassTests
{
    [Fact]
    public void ModulePresent_BypassOff_RequiresLogin()
    {
        var (services, provider, _) = Build(withModule: true, bypassOn: false);
        var user = provider.GetRequiredService<ICurrentUser>();

        Assert.Equal(AdminAccessMode.RequireLogin, AdminAccess.Evaluate(HasLoginModule(services), new TestBypass().IsOn));
        Assert.False(user.IsAuthenticated);
        Assert.False(user.IsAdministrator);
    }

    [Fact]
    public void ModuleAbsent_BypassOff_IsBlocked()
    {
        var (services, _, bypass) = Build(withModule: false, bypassOn: false);

        Assert.Equal(AdminAccessMode.Blocked, AdminAccess.Evaluate(HasLoginModule(services), bypass.IsOn));
    }

    [Fact]
    public void ModuleAbsent_BypassOn_OpensAsLocalAdministrator()
    {
        var (services, provider, bypass) = Build(withModule: false, bypassOn: true);
        var user = provider.GetRequiredService<ICurrentUser>();

        Assert.Equal(AdminAccessMode.Bypassed, AdminAccess.Evaluate(HasLoginModule(services), bypass.IsOn));
        Assert.True(user.IsAdministrator);
        Assert.Equal(AdminAccess.BypassUsername, user.Username);
    }

    [Fact]
    public void ModulePresent_BypassOn_OpensAsLocalAdministrator_AndReturnsToTheSessionWhenTurnedOff()
    {
        var (services, provider, bypass) = Build(withModule: true, bypassOn: true);
        var user = provider.GetRequiredService<ICurrentUser>();
        var changes = 0;
        user.Changed += (_, _) => changes++;

        Assert.Equal(AdminAccessMode.Bypassed, AdminAccess.Evaluate(HasLoginModule(services), bypass.IsOn));
        Assert.True(user.IsAdministrator);

        bypass.Set(false);
        Assert.Equal(1, changes);
        Assert.Equal(AdminAccessMode.RequireLogin, AdminAccess.Evaluate(HasLoginModule(services), bypass.IsOn));
        Assert.False(user.IsAuthenticated);

        provider.GetRequiredService<UserSession>().SignIn(Guid.NewGuid(), "viewer", UserRole.Viewer, TimeSpan.FromMinutes(5));
        Assert.Equal(2, changes);
        Assert.Equal("viewer", user.Username);
        Assert.False(user.IsAdministrator);
    }

    [Fact]
    public void UseLoginBypass_CalledTwice_WrapsOnce()
    {
        var services = new ServiceCollection().AddSchedulerApplication();
        services.UseLoginBypass().UseLoginBypass();

        Assert.Single(services, d => d.ServiceType == typeof(ICurrentUser));
    }

    private static (IServiceCollection Services, ServiceProvider Provider, TestBypass Bypass) Build(bool withModule, bool bypassOn)
    {
        var bypass = new TestBypass();
        bypass.Set(bypassOn);
        var services = new ServiceCollection();
        services.AddSchedulerApplication();
        services.AddSingleton<ILoginBypass>(bypass);
        services.AddSchedulerModules(withModule ? [new SecurityModule()] : []);
        return (services, services.BuildServiceProvider(), bypass);
    }

    private static bool HasLoginModule(IServiceCollection services) => services.Any(d => d.ServiceType == typeof(IAuthenticationProvider));

    private sealed class TestBypass : ILoginBypass
    {
        public bool IsOn { get; private set; }

        public event EventHandler? Changed;

        public void Set(bool on)
        {
            IsOn = on;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
