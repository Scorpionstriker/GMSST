namespace Scheduler.Extensibility.Contracts;

/// <summary>Durable in-app notifications for the scheduler and its integrated modules.</summary>
public interface ISchedulerNotifications
{
    /// <summary>
    /// Creates an immediate notification when <paramref name="deliverAtUtc"/> is omitted, or a one-time scheduled
    /// notification when it is supplied. Modules should use a stable source name to manage their own pending messages.
    /// </summary>
    Task<Guid> ScheduleAsync(string source, Guid recipientUserId, string title, string message, DateTime? deliverAtUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists this source's future notifications; it cannot inspect another application's notifications.</summary>
    Task<IReadOnlyList<NotificationItem>> GetScheduledAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>Cancels one of this source's notifications only while it is still scheduled for the future.</summary>
    Task<bool> CancelScheduledAsync(string source, Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>Lists delivered notifications for the signed-in user.</summary>
    Task<IReadOnlyList<NotificationItem>> GetInboxAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks one of the signed-in user's delivered notifications as read.</summary>
    Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
}

public sealed record NotificationItem(Guid Id, string Source, string Title, string Message, DateTime CreatedAtUtc,
    DateTime DeliverAtUtc, DateTime? ReadAtUtc);
