namespace Scheduler.Domain.Enums;

/// <summary>Why a queue entry was created (FR-19).</summary>
public enum TriggerType
{
    Scheduled = 1,
    Manual = 2,
    Retry = 3,
    Dependency = 4,
    CatchUp = 5,
    Event = 6
}
