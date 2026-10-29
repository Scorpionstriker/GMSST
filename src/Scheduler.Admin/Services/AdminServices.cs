using Microsoft.Extensions.DependencyInjection;

namespace Scheduler.Admin.Services;

public static class AdminServices
{
    /// <summary>Services of the Admin application's pages and layout. They do not depend on .NET MAUI.</summary>
    public static IServiceCollection AddAdminServices(this IServiceCollection services)
    {
        services.AddSingleton<StartupState>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<IPreferenceStore>(_ => new FilePreferenceStore());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IFileSaver>(_ => new DocumentsFileSaver());
        return services;
    }
}
