using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Scheduler.Admin.Services;
using Scheduler.Application;
using Scheduler.Engine;
using Scheduler.Infrastructure;

namespace Scheduler.Admin;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

        // appsettings.json is embedded in the app, so the settings travel with it. It holds no password:
        // the database password comes only from the GMSS_DB_PASSWORD environment variable (NFR-15).
        using (var settings = typeof(MauiProgram).Assembly.GetManifestResourceStream("Scheduler.Admin.appsettings.json"))
        {
            if (settings is not null)
            {
                builder.Configuration.AddJsonStream(settings);
            }
        }

        builder.Services.AddMauiBlazorWebView();

        // Composition root (Phase 4, Table 3.1): the core layers, then the modules (FR-41, FR-42).
        var engineOptions = builder.Configuration.GetSection(EngineOptions.SectionName).Get<EngineOptions>() ?? new EngineOptions();
        builder.Services
            .AddSchedulerApplication()
            .AddSchedulerInfrastructure(builder.Configuration)
            .AddSchedulerEngine(engineOptions)
            .AddSchedulerModules(ModuleCatalog.Modules)
            .AddAdminServices();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
