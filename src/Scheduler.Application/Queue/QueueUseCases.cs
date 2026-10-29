using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Queue;

/// <summary>Queue page query (FR-59), executed in the database.</summary>
public interface IQueueQueries
{
    Task<PagedResult<QueueListItem>> GetEntriesAsync(IReadOnlyCollection<QueueStatus> statuses, PageRequest page,
        CancellationToken cancellationToken = default);

    Task<QueueCounts> GetCountsAsync(CancellationToken cancellationToken = default);
}

public sealed record QueueListItem(
    Guid Id,
    Guid TaskId,
    string TaskName,
    string TaskType,
    TriggerType TriggerType,
    int Priority,
    DateTime ScheduledTimeUtc,
    DateTime EnqueuedAtUtc,
    QueueStatus Status,
    string? ClaimedByHost,
    DateTime? ClaimedUntilUtc,
    int Attempt);

public sealed record QueueCounts(int Waiting, int Claimed);

/// <summary>Waiting and claimed entries, highest priority first.</summary>
public sealed record GetQueueQuery(IReadOnlyCollection<QueueStatus> Statuses, PageRequest Page);

/// <summary>Cancels a queue entry that is still waiting (FR-24).</summary>
public sealed record CancelQueueEntryCommand(Guid QueueEntryId) : IRequireAdministrator;

internal sealed class GetQueueHandler(IQueueQueries queries) : IQueryHandler<GetQueueQuery, PagedResult<QueueListItem>>
{
    public async Task<Result<PagedResult<QueueListItem>>> HandleAsync(GetQueueQuery query, CancellationToken cancellationToken = default)
    {
        var statuses = query.Statuses.Count == 0 ? [QueueStatus.Waiting, QueueStatus.Claimed] : query.Statuses;
        return Result.Success(await queries.GetEntriesAsync(statuses, query.Page, cancellationToken));
    }
}

internal sealed class CancelQueueEntryHandler(IUnitOfWorkFactory unitOfWorkFactory) : ICommandHandler<CancelQueueEntryCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CancelQueueEntryCommand command, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var entry = await unitOfWork.Queue.GetAsync(command.QueueEntryId, cancellationToken);
        if (entry is null)
        {
            return Error.NotFound("Queue entry");
        }

        if (entry.Status != QueueStatus.Waiting)
        {
            return Error.Conflict("Queue.NotWaiting", "Only waiting entries can be cancelled. This entry has already started or finished.");
        }

        entry.Cancel();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Unit.Value);
    }
}
