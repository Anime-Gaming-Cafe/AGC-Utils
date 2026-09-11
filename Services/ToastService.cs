namespace AGC_Management.Services;

// Scoped: one toast list per Blazor Server circuit, not shared across users.
public sealed class ToastService
{
    public sealed record ToastMessage(Guid Id, string Message, string Tone);

    private readonly List<ToastMessage> _toasts = [];

    public IReadOnlyList<ToastMessage> Toasts => _toasts;

    public event Action? OnChange;

    public void Notify(string message, string tone = "ok")
    {
        var toast = new ToastMessage(Guid.NewGuid(), message, tone);
        _toasts.Add(toast);
        OnChange?.Invoke();
        _ = AutoDismissAsync(toast.Id);
    }

    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0)
            OnChange?.Invoke();
    }

    private async Task AutoDismissAsync(Guid id)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        Dismiss(id);
    }
}
