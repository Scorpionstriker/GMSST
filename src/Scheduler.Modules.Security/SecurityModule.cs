using Microsoft.Extensions.DependencyInjection;
using Scheduler.Extensibility.Contracts;
using Scheduler.Modules.Security.Authentication;

namespace Scheduler.Modules.Security;

/// <summary>
/// Registers the local authentication provider and contributes the Users page (FR-44, FR-51).
/// Remove this module's registration and project reference to run the Admin application without login.
/// </summary>
public sealed class SecurityModule : ISchedulerModule
{
    public string Name => "Login and Security";

    public IReadOnlyList<ModuleMenuItem> MenuItems { get; } =
    [
        new("Users", "/users", AdministratorOnly: true)
    ];

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<PasswordHasher>();
        services.AddScoped<IAuthenticationProvider, LocalAuthenticationProvider>();
    }
}
