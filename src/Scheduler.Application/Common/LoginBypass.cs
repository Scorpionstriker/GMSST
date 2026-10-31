using Microsoft.Extensions.DependencyInjection;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Common;

/// <summary>
/// The developer login bypass, Security:AllowLoginBypass (WP-4a, FR-44). It is off by default and can be switched on
/// only in the user settings file of this computer or by a Developer on the Configuration page.
/// </summary>
public interface ILoginBypass
{
    bool IsOn { get; }

    /// <summary>Raised when the setting changes, for example after the settings file was saved.</summary>
    event EventHandler? Changed;
}

/// <summary>The bypass when no configuration is available, for example in tests: always off.</summary>
internal sealed class NoLoginBypass : ILoginBypass
{
    public bool IsOn => false;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}

/// <summary>What the Admin application shows before its pages (WP-4a).</summary>
public enum AdminAccessMode
{
    /// <summary>A login module is installed and the bypass is off: sign in first.</summary>
    RequireLogin,

    /// <summary>No login module is installed and the bypass is off: the pages stay closed.</summary>
    Blocked,

    /// <summary>The bypass is on: the pages open as the local Developer, with a red banner.</summary>
    Bypassed
}

public static class AdminAccess
{
    public const string BlockedMessage =
        "No login module is installed. Install the Login and Security module, or turn on Security:AllowLoginBypass for development.";

    public const string BypassBanner = "Login bypassed (developer mode). Anyone using this computer has full access.";

    public const string BypassUsername = "Local developer (login bypassed)";

    public static AdminAccessMode Evaluate(bool hasLoginModule, bool bypassOn) =>
        bypassOn ? AdminAccessMode.Bypassed : hasLoginModule ? AdminAccessMode.RequireLogin : AdminAccessMode.Blocked;

    /// <summary>
    /// Wraps the registered <see cref="ICurrentUser"/> (the local Developer, or the Login and Security module's
    /// session) so that it acts as the local Developer while the bypass is on. Call it after the modules are
    /// registered; the engine's AddSchedulerModules does this.
    /// </summary>
    public static IServiceCollection UseLoginBypass(this IServiceCollection services)
    {
        var registered = services.LastOrDefault(d => d.ServiceType == typeof(ICurrentUser));
        if (registered is null || registered.ImplementationType == typeof(LoginBypassCurrentUser))
        {
            return services;
        }

        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(ICurrentUser))
            {
                services.RemoveAt(i);
            }
        }

        services.AddSingleton(sp => new LoginBypassCurrentUser(Create(sp, registered), sp.GetService<ILoginBypass>() ?? new NoLoginBypass()));
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<LoginBypassCurrentUser>());
        return services;
    }

    private static ICurrentUser Create(IServiceProvider services, ServiceDescriptor descriptor) =>
        (ICurrentUser)(descriptor.ImplementationInstance
                       ?? descriptor.ImplementationFactory?.Invoke(services)
                       ?? ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!));
}

/// <summary>
/// The current user with the bypass applied: while the bypass is on it is the local Developer, otherwise it is
/// the wrapped user (WP-4a). <see cref="Changed"/> is raised when either the user or the bypass changes.
/// </summary>
public sealed class LoginBypassCurrentUser : ICurrentUser
{
    private readonly ICurrentUser _inner;
    private readonly ILoginBypass _bypass;

    public LoginBypassCurrentUser(ICurrentUser inner, ILoginBypass bypass)
    {
        _inner = inner;
        _bypass = bypass;
        _inner.Changed += (_, e) => Changed?.Invoke(this, e);
        _bypass.Changed += (_, e) => Changed?.Invoke(this, e);
    }

    public bool IsBypassed => _bypass.IsOn;
    public bool IsAuthenticated => IsBypassed || _inner.IsAuthenticated;
    public Guid? UserId => IsBypassed ? null : _inner.UserId;
    public string? Username => IsBypassed ? AdminAccess.BypassUsername : _inner.Username;
    public UserRole? Role => IsBypassed ? UserRole.Developer : _inner.Role;

    public void RegisterActivity()
    {
        if (!IsBypassed)
        {
            _inner.RegisterActivity();
        }
    }

    public event EventHandler? Changed;
}
