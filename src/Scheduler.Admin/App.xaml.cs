using Scheduler.Engine;

namespace Scheduler.Admin
{
    public partial class App : Application
    {
        private readonly SchedulerEngine _engine;

        public App(SchedulerEngine engine)
        {
            InitializeComponent();
            _engine = engine;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new MainPage()) { Title = "Generic System Scheduler" };

            // A MAUI app does not start hosted services by itself, so the engine starts when the window
            // opens and stops in a controlled way when it closes (Phase 4, Section 3.1.3; FR-34).
            window.Created += async (_, _) => await _engine.StartAsync(CancellationToken.None);
            window.Destroying += async (_, _) => await _engine.StopAsync(CancellationToken.None);

            return window;
        }
    }
}
