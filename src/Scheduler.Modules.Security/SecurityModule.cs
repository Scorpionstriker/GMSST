using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduler.Extensibility.Contracts;
using Scheduler.Modules.Security.Authentication;

namespace Scheduler.Modules.Security;

/// <summary>
/// The Login and Security module (FR-44 to FR-53): local accounts, sign-in with lockout, the session with its idle
/// time-out, and the Login, Users and Account pages. It replaces the host's local administrator with the
/// signed-in user. Remove this module from the module list (and its project reference) to run without sign-in.
/// </summary>
public sealed class SecurityModule : ISchedulerModule
{
    public const string LoginRoute = "/login";

    public string Name => "Login and Security";

    public string Description => "Local user accounts, sign-in with lockout, roles, session time-out, registration and password recovery.";

    /// <summary>
    /// Sign-in cannot be switched off on the Modules page: that would let anyone in. To run without sign-in, remove the
    /// module from the module list or use the developer login bypass.
    /// </summary>
    public bool CanBeDisabled => false;

    public IReadOnlyList<ModuleMenuItem> MenuItems { get; } =
    [
        new("Users", "/users", "Administration", AdministratorOnly: true),
        new("Account", "/account/password", "Account")
    ];

    public void Register(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<UserSession>();
        services.Replace(ServiceDescriptor.Singleton<ICurrentUser>(sp => sp.GetRequiredService<UserSession>()));
        services.AddSingleton<IAuthenticationProvider, LocalAuthenticationProvider>();
        services.AddSingleton<UserManagement>();
    }
}
