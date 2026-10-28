using Microsoft.Extensions.Logging;
using Scheduler.Application;
using Scheduler.Engine;
using Scheduler.Infrastructure;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Admin
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // Composition root: the core layers, then the modules (FR-41, FR-42).
            // Database settings use the defaults in DatabaseOptions (localhost, gmss, gmss_app) until
            // appsettings.json loading is added; the password comes from GMSS_DB_PASSWORD (NFR-15).
            builder.Services
                .AddSchedulerApplication()
                .AddSchedulerInfrastructure(new DatabaseOptions())
                .AddSchedulerEngine()
                .AddSchedulerModules(ModuleCatalog.Modules);

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
