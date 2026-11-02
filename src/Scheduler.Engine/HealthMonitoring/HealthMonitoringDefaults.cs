using Scheduler.Application.Common.Persistence;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.HealthMonitoring;

internal sealed class EmptyResourceHealthReader : IResourceHealthReader
{
    public Task<ResourceHealthSnapshot> GetSnapshotAsync(DateTime executionsSinceUtc, DateTime staleBeforeUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResourceHealthSnapshot(0, 0, 0, 0, 0, []));
}

internal sealed class EmptyUserAccountStore : IUserAccountStore
{
    public Task<bool> AnyUsersAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default) => Task.FromResult<UserAccount?>(null);
    public Task<UserAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<UserAccount?>(null);
    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserAccount>>([]);
    public Task<bool> UsernameExistsAsync(string username, Guid? exceptUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
    public Task CreateAsync(UserAccount user, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("No user account store is configured.");
    public Task UpdateAsync(UserAccount user, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("No user account store is configured.");
    public Task DeleteAsync(UserAccount user, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("No user account store is configured.");
    public Task AddLoginAuditAsync(LoginAuditRecord record, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("No user account store is configured.");
}

internal sealed class UnavailableSchedulerNotifications : ISchedulerNotifications
{
    private static InvalidOperationException NotConfigured() => new("Scheduler notification storage is not configured.");

    public Task<Guid> ScheduleAsync(string source, Guid recipientUserId, string title, string message, DateTime? deliverAtUtc = null,
        CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<IReadOnlyList<NotificationItem>> GetScheduledAsync(string source, CancellationToken cancellationToken = default) =>
        throw NotConfigured();
    public Task<bool> CancelScheduledAsync(string source, Guid notificationId, CancellationToken cancellationToken = default) =>
        throw NotConfigured();
    public Task<IReadOnlyList<NotificationItem>> GetInboxAsync(CancellationToken cancellationToken = default) => throw NotConfigured();
    public Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default) => throw NotConfigured();
}
