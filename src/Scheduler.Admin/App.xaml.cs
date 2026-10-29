using Scheduler.Admin.Services;
using Scheduler.Engine.Hosting;

namespace Scheduler.Admin;

public partial class App : Microsoft.Maui.Controls.Application
{
    // Longer than the default shutdown grace period, so running tasks can finish or be re-queued (FR-34).
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(45);

    private readonly StartupState _startup;
    private readonly SchedulerHost _host;

    public App(StartupState startup, SchedulerHost host)
    {
        InitializeComponent();
        _startup = startup;
        _host = host;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "Generic System Scheduler" };

        // A MAUI app does not run hosted services, so the scheduler starts when the window opens. Start-up
        // errors are shown on the start-up screen rather than closing the app (Phase 4, Section 3.1.3).
        window.Created += (_, _) => _ = _startup.EnsureStartedAsync();

        // Stops in a controlled way: running executions get the shutdown grace period and are re-queued if
        // they do not finish. The wait is bounded so a hung handler cannot keep the app open.
        window.Destroying += (_, _) =>
        {
            if (_startup.Phase == StartupPhase.Ready)
            {
                Task.Run(() => _host.StopAsync()).Wait(StopTimeout);
            }
        };

        return window;
    }
}
