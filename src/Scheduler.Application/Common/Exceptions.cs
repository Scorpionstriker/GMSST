namespace Scheduler.Application.Common;

/// <summary>Thrown by the data layer when a row changed after it was read (optimistic concurrency, NFR-17).</summary>
public sealed class ConcurrencyConflictException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Thrown by the data layer when a unique constraint is violated, for example a duplicate task name.</summary>
public sealed class UniqueConstraintException(string constraintName, Exception? inner = null)
    : Exception($"Unique constraint {constraintName} was violated.", inner)
{
    public string ConstraintName { get; } = constraintName;
}
