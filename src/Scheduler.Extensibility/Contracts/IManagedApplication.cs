namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// An application a module can launch and stop from the Modules page, for example the separate Maintenance Agent.
/// A module registers its implementation in <see cref="ISchedulerModule.Register"/>; the Modules page shows the
/// Launch and Stop buttons on the row of the module named <see cref="ModuleName"/>. What launching means is up to the
/// module: start a separate program, or start the module's own scheduled work.
/// </summary>
public interface IManagedApplication
{
    /// <summary>The <see cref="ISchedulerModule.Name"/> of the module this application belongs to.</summary>
    string ModuleName { get; }

    /// <summary>Name shown with the buttons, for example "Maintenance Agent".</summary>
    string DisplayName { get; }

    Task<ManagedApplicationStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Launches the application. Returns when it runs or has failed to start.</summary>
    Task<ManagedApplicationResult> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the application. Returns when it has stopped or has failed to stop.</summary>
    Task<ManagedApplicationResult> StopAsync(CancellationToken cancellationToken = default);
}

public enum ManagedApplicationState
{
    Stopped = 0,
    Running = 1
}

/// <param name="State">Whether the application runs.</param>
/// <param name="Message">One sentence for the Modules page, for example "Running on port 5087."</param>
public sealed record ManagedApplicationStatus(ManagedApplicationState State, string Message)
{
    public bool IsRunning => State == ManagedApplicationState.Running;
}

/// <param name="Succeeded">Whether the launch or stop worked.</param>
/// <param name="Message">What happened, or why it failed, shown to the user.</param>
public sealed record ManagedApplicationResult(bool Succeeded, string Message)
{
    public static ManagedApplicationResult Success(string message) => new(true, message);

    public static ManagedApplicationResult Failure(string message) => new(false, message);
}
