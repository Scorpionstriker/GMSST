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

    public App(StartupState startup, SchedulerHost host, ISettingsProvider settings)
    {
        InitializeComponent();
        _startup = startup;
        _host = host;
        _settings = settings;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "Generic System Scheduler" };

        // A MAUI app does not run hosted services, so the scheduler starts when the window opens. Start-up
        // errors are shown on the start-up screen rather than closing the app (Phase 4, Section 3.1.3).
        window.Created += (_, _) => _ = _startup.EnsureStartedAsync();

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
