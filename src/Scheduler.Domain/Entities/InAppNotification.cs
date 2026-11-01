namespace Scheduler.Domain.Entities;

/// <summary>A durable, in-app message addressed to one scheduler user.</summary>
public sealed class InAppNotification
{
    public const int SourceMaxLength = 100;
    public const int TitleMaxLength = 200;
    public const int MessageMaxLength = 4000;

    private InAppNotification()
    {
        Source = string.Empty;
        Title = string.Empty;
        Message = string.Empty;
    }

    public InAppNotification(string source, Guid recipientUserId, string title, string message, DateTime deliverAtUtc, DateTime nowUtc)
    {
        Source = source.Trim();
        RecipientUserId = recipientUserId;
        Title = title.Trim();
        Message = message.Trim();
        DeliverAtUtc = DateTime.SpecifyKind(deliverAtUtc, DateTimeKind.Utc);
        CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Source { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string Title { get; private set; }
    public string Message { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime DeliverAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    public void MarkRead(DateTime nowUtc) => ReadAtUtc ??= DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
    public void Cancel(DateTime nowUtc) => CancelledAtUtc ??= DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
}
