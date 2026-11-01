using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Configuration;
using Scheduler.Extensibility.Contracts;

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
        services.AddSingleton<IApplicationControl, MauiApplicationControl>();
        services.AddSingleton<IFileSaver>(sp => new DocumentsFileSaver(sp.GetRequiredService<IMachineSettingsStore>()));
        services.AddSingleton<PrintService>();
        return services;
    }
}
