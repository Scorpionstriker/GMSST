using Scheduler.Extensibility.Contracts;

namespace DemoApps.MaintenanceAgentConnector;

/// <summary>Refuses to restore maintenance_demo while the Maintenance Agent answers on its port (WP-5).</summary>
internal sealed class MaintenanceAgentRestoreGuard(AgentClient agent) : IRestoreGuard
{
    public const string StopFirstMessage = "Stop the Maintenance Agent first, then restore.";

    public async Task<string?> CheckRestoreAsync(IReadOnlyCollection<string> databaseNames, CancellationToken cancellationToken = default)
    {
        if (!databaseNames.Contains(agent.DatabaseName, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return (await agent.GetStatusAsync(cancellationToken)).Running
            ? StopFirstMessage
            : null;
    }
}
