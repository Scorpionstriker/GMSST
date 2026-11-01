using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Entities;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Infrastructure.Persistence.Repositories;

internal sealed class SchedulerNotificationStore(
    IDbContextFactory<SchedulerDbContext> contextFactory,
    ICurrentUser currentUser,
    TimeProvider clock) : ISchedulerNotifications
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Guid> ScheduleAsync(string source, Guid recipientUserId, string title, string message,
        DateTime? deliverAtUtc = null, CancellationToken cancellationToken = default)
    {
        source = Required(source, nameof(source), InAppNotification.SourceMaxLength);
        title = Required(title, nameof(title), InAppNotification.TitleMaxLength);
        message = Required(message, nameof(message), InAppNotification.MessageMaxLength);
        var now = Now;
        var delivery = deliverAtUtc ?? now;
        if (delivery.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The delivery time must be UTC.", nameof(deliverAtUtc));
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.AppUsers.AnyAsync(user => user.Id == recipientUserId, cancellationToken))
        {
            throw new ArgumentException("The recipient account does not exist.", nameof(recipientUserId));
        }

        var notification = new InAppNotification(source, recipientUserId, title, message, delivery, now);
        db.Add(notification);
        await db.SaveChangesAsync(cancellationToken);
        return notification.Id;
    }

    public async Task<IReadOnlyList<NotificationItem>> GetScheduledAsync(string source, CancellationToken cancellationToken = default)
    {
        source = Required(source, nameof(source), InAppNotification.SourceMaxLength);
        var now = Now;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<InAppNotification>().AsNoTracking()
            .Where(item => item.Source == source && item.CancelledAtUtc == null && item.DeliverAtUtc > now)
            .OrderBy(item => item.DeliverAtUtc)
            .Select(item => new NotificationItem(item.Id, item.Source, item.Title, item.Message, item.CreatedAtUtc,
                item.DeliverAtUtc, item.ReadAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CancelScheduledAsync(string source, Guid notificationId, CancellationToken cancellationToken = default)
    {
        source = Required(source, nameof(source), InAppNotification.SourceMaxLength);
        var now = Now;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var notification = await db.Set<InAppNotification>().SingleOrDefaultAsync(item =>
            item.Id == notificationId && item.Source == source && item.CancelledAtUtc == null && item.DeliverAtUtc > now,
            cancellationToken);
        if (notification is null)
        {
            return false;
        }

        notification.Cancel(now);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<NotificationItem>> GetInboxAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("Sign in to view notifications.");
        var now = Now;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<InAppNotification>().AsNoTracking()
            .Where(item => item.RecipientUserId == userId && item.CancelledAtUtc == null && item.DeliverAtUtc <= now)
            .OrderByDescending(item => item.DeliverAtUtc)
            .Select(item => new NotificationItem(item.Id, item.Source, item.Title, item.Message, item.CreatedAtUtc,
                item.DeliverAtUtc, item.ReadAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("Sign in to manage notifications.");
        var now = Now;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var notification = await db.Set<InAppNotification>().SingleOrDefaultAsync(item =>
            item.Id == notificationId && item.RecipientUserId == userId && item.CancelledAtUtc == null && item.DeliverAtUtc <= now,
            cancellationToken);
        if (notification is null)
        {
            return false;
        }

        notification.MarkRead(now);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string Required(string value, string parameter, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        value = value.Trim();
        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"The value must not exceed {maximumLength} characters.", parameter);
        }

        return value;
    }
}
