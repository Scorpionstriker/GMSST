using Scheduler.Domain.Enums;
using Scheduler.Domain.Rules;

namespace Scheduler.Domain.Entities;

/// <summary>A slot in a host's bounded worker pool, with a heartbeat that shows it is alive (FR-21).</summary>
public sealed class WorkerThread
{
    public const string DefaultPoolName = "DefaultWorkerPool";
    public const int HostNameMaxLength = 100;

    private WorkerThread()
    {
        HostName = string.Empty;
        ThreadPoolName = DefaultPoolName;
    }

    public WorkerThread(string hostName, string threadPoolName, DateTime nowUtc)
    {
        HostName = hostName;
        ThreadPoolName = threadPoolName;
        Status = WorkerStatus.Idle;
        StartedAtUtc = nowUtc;
        LastHeartbeatUtc = nowUtc;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string HostName { get; private set; }
    public string ThreadPoolName { get; private set; }
    public WorkerStatus Status { get; private set; }
    public Guid? CurrentExecutionId { get; private set; }
    public DateTime LastHeartbeatUtc { get; private set; }
    public DateTime StartedAtUtc { get; private set; }

    public void Start(DateTime nowUtc)
    {
        Status = WorkerStatus.Idle;
        CurrentExecutionId = null;
        StartedAtUtc = nowUtc;
        LastHeartbeatUtc = nowUtc;
    }

    public void MarkBusy(Guid executionId, DateTime nowUtc) => Change(WorkerStatus.Busy, executionId, nowUtc);

    public void MarkIdle(DateTime nowUtc) => Change(WorkerStatus.Idle, null, nowUtc);

    public void MarkStopped(DateTime nowUtc) => Change(WorkerStatus.Stopped, null, nowUtc);

    private void Change(WorkerStatus target, Guid? executionId, DateTime nowUtc)
    {
        if (Status != target)
        {
            StatusTransitions.Ensure(StatusTransitions.IsAllowed(Status, target), "worker", Status, target);
        }

        Status = target;
        CurrentExecutionId = executionId;
        LastHeartbeatUtc = nowUtc;
    }
}
