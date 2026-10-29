namespace Scheduler.Admin.Services;

public enum ToastKind
{
    Success,
    Warning,
    Error
}

public sealed record Toast(Guid Id, string Message, ToastKind Kind)
{
    public bool IsError => Kind == ToastKind.Error;
}

/// <summary>Short confirmations and errors shown in the corner of the window after an action (FR-61, FR-62).</summary>
public sealed class ToastService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);
    private readonly List<Toast> _toasts = [];
    private readonly Lock _gate = new();

    public event Action? Changed;

    public IReadOnlyList<Toast> Toasts
    {
        get
        {
            lock (_gate)
            {
                return _toasts.ToList();
            }
        }
    }

    public void Success(string message) => Add(message, ToastKind.Success);

    /// <summary>The action worked, but something needs attention, for example a backup that could not be verified (WP-5f).</summary>
    public void Warning(string message) => Add(message, ToastKind.Warning);

    public void Error(string message) => Add(message, ToastKind.Error);

    public void Dismiss(Guid id)
    {
        lock (_gate)
        {
            _toasts.RemoveAll(t => t.Id == id);
        }

        Changed?.Invoke();
    }

    private void Add(string message, ToastKind kind)
    {
        var toast = new Toast(Guid.NewGuid(), message, kind);
        lock (_gate)
        {
            _toasts.Add(toast);
        }

        Changed?.Invoke();
        // Errors and warnings stay twice as long, so they can be read.
        _ = Task.Delay(kind == ToastKind.Success ? Lifetime : Lifetime * 2).ContinueWith(_ => Dismiss(toast.Id), TaskScheduler.Default);
    }
}
