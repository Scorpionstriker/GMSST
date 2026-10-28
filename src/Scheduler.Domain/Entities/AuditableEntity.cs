namespace Scheduler.Domain.Entities;

/// <summary>
/// Base class for entities that carry an identifier, audit fields and soft deletion
/// (ScheduledTask, Schedule, AppUser). FR-04, FR-08.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
