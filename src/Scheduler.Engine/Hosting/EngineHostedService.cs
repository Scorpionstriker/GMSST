using Microsoft.Extensions.Hosting;

namespace Scheduler.Engine.Hosting;

/// <summary>Runs <see cref="SchedulerHost"/> inside a .NET generic host, for console hosts such as the Maintenance Agent.</summary>
public sealed class EngineHostedService(SchedulerHost host) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => host.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => host.StopAsync(cancellationToken);
}
