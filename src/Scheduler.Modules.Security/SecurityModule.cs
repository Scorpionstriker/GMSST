using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduler.Extensibility.Contracts;
using Scheduler.Modules.Security.Authentication;

namespace Scheduler.Modules.Security;

/// <summary>
/// The Login and Security module (FR-44 to FR-53): local accounts, sign-in with lockout, the session with its idle
/// time-out, and the Login, Users and Change password pages. It replaces the host's local administrator with the
/// signed-in user. Remove this module from the module list (and its project reference) to run without sign-in.
/// </summary>
public sealed class SecurityModule : ISchedulerModule
{
    public const string LoginRoute = "/login";

    public string Name => "Login and Security";

    public string Description => "Local user accounts, sign-in with lockout, roles and session time-out.";

    public IReadOnlyList<ModuleMenuItem> MenuItems { get; } =
    [
        new("Users", "/users", "Administration", AdministratorOnly: true),
        new("Change password", "/account/password", "Account")
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
