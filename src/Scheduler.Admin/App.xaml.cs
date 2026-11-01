using Scheduler.Extensibility.Contracts;
using Scheduler.Admin.Services;
using Scheduler.Application.Configuration;
using Scheduler.Engine.Hosting;

namespace Scheduler.Admin;

public partial class App : Microsoft.Maui.Controls.Application
{
    // Used when the settings cannot be read: the default grace period of 30 seconds plus the margin below.
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(45);

    // Time after the grace period for cancelled runs to record their outcome and be queued again (F-10).
    private const int StopMarginSeconds = 15;

    private readonly StartupState _startup;
    private readonly SchedulerHost _host;
    private readonly ISettingsProvider _settings;
    private readonly IManagedApplication? _maintenanceAgent;
    private bool _allowClose;
    private bool _closePromptOpen;

    public App(StartupState startup, SchedulerHost host, ISettingsProvider settings, IEnumerable<IManagedApplication> managedApplications)
    {
        InitializeComponent();
        _startup = startup;
        _host = host;
        _settings = settings;
        _maintenanceAgent = managedApplications.FirstOrDefault(application => application.DisplayName == "Maintenance Agent");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new MainPage();
        var window = new Window(page) { Title = "Generic Moduler System Scheduler Template" };

        // A MAUI app does not run hosted services, so the scheduler starts when the window opens. Start-up
        // errors are shown on the start-up screen rather than closing the app (Phase 4, Section 3.1.3).
        window.Created += (_, _) =>
        {
            _ = _startup.EnsureStartedAsync();
#if WINDOWS
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                nativeWindow.AppWindow.Closing += (_, e) => OnWindowClosing(nativeWindow, page, e);
            }
#endif
        };

        // Stops in a controlled way when the window is closed (on Windows, the close button or Alt+F4): running
        // executions get ShutdownGraceSeconds and are re-queued if they do not finish (FR-34). The wait is bounded by
        // ShutdownGraceSeconds + 15 seconds, so a hung handler cannot keep the app open (WP-5h).
        window.Destroying += (_, _) =>
        {
            if (_startup.Phase == StartupPhase.Ready)
            {
                Task.Run(() => _host.StopAsync()).Wait(StopTimeout());
            }
        };

        return window;
    }

#if WINDOWS
    private async void OnWindowClosing(Microsoft.UI.Xaml.Window nativeWindow, Page page,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        if (_closePromptOpen)
        {
            return;
        }

        _closePromptOpen = true;
        try
        {
            if (_maintenanceAgent is null || !(await _maintenanceAgent.GetStatusAsync()).IsRunning)
            {
                _allowClose = true;
                nativeWindow.Close();
                return;
            }

            var confirmed = await page.DisplayAlertAsync(
                "Maintenance Agent is running",
                "Closing the Scheduler will also stop the Maintenance Agent so it does not keep running in the background. Continue?",
                "Stop Agent and Close",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            var stopped = await _maintenanceAgent.StopAsync();
            if (!stopped.Succeeded)
            {
                await page.DisplayAlertAsync("Could not stop the Maintenance Agent", stopped.Message, "OK");
                return;
            }

            _allowClose = true;
            nativeWindow.Close();
        }
        finally
        {
            _closePromptOpen = false;
        }
    }
#endif

    private TimeSpan StopTimeout()
    {
        try
        {
            var settings = Task.Run(() => _settings.GetEngineSettingsAsync()).Result;
            return TimeSpan.FromSeconds(settings.ShutdownGraceSeconds + StopMarginSeconds);
        }
        catch (Exception)
        {
            return DefaultStopTimeout;
        }
    }
}
