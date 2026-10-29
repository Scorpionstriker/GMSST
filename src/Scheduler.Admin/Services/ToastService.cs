namespace Scheduler.Admin.Services;

public sealed record Toast(Guid Id, string Message, bool IsError);

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

    public void Success(string message) => Add(message, isError: false);

    public void Error(string message) => Add(message, isError: true);

    public void Dismiss(Guid id)
    {
        lock (_gate)
        {
            _toasts.RemoveAll(t => t.Id == id);
        }

        Changed?.Invoke();
    }

    private void Add(string message, bool isError)
    {
        var toast = new Toast(Guid.NewGuid(), message, isError);
        lock (_gate)
        {
            _toasts.Add(toast);
        }

        Changed?.Invoke();
        // Errors stay twice as long, so they can be read.
        _ = Task.Delay(isError ? Lifetime * 2 : Lifetime).ContinueWith(_ => Dismiss(toast.Id), TaskScheduler.Default);
    }
}
