using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Scheduler.Admin.Services;
using Scheduler.Application;
using Scheduler.Application.Configuration;
using Scheduler.Application.Modules;
using Scheduler.Engine;
using Scheduler.Infrastructure;
using Scheduler.Infrastructure.Configuration;

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

        // WP-5a: the settings of this computer (folders, PostgreSQL tools, login bypass, Agent address) live in a user
        // settings file outside the app, which wins over the embedded defaults; GSST_ environment variables win over both,
        // for example GSST_Paths__BackupFolder. The file is created with empty values on first start.
        var machineSettings = new MachineSettingsFile(MachineSettingsFile.DefaultPath);
        try
        {
            machineSettings.EnsureExists();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The app still starts with the embedded defaults; the Configuration page shows where the file should be.
        }

        builder.Configuration
            .AddJsonFile(machineSettings.FilePath, optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("GSST_");
        builder.Services.AddSingleton<IMachineSettingsStore>(machineSettings);
        builder.Services.AddSingleton<IModuleStateStore>(machineSettings);
#if WINDOWS
        builder.Services.AddSingleton<IFolderPicker, Scheduler.Admin.WinUI.WindowsFolderPicker>();
#else
        builder.Services.AddSingleton<IFolderPicker, TypedFolderPicker>();
#endif

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
