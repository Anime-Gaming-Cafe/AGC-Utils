namespace AGC_Management.Services;

// Scoped: one dirty-state per Blazor Server circuit. The currently displayed page registers its
// own save/discard logic on load and unregisters on dispose; UnsavedChangesBar (hosted once in
// MainLayout) is the only thing that renders against it, so pages never need to know about each other.
public sealed class UnsavedChangesTracker
{
    public bool IsDirty { get; private set; }

    public event Action? OnChange;

    private Func<Task>? _save;
    private Action? _discard;

    public void Register(Func<Task> save, Action discard)
    {
        _save = save;
        _discard = discard;
        if (IsDirty)
        {
            IsDirty = false;
            OnChange?.Invoke();
        }
    }

    public void Unregister()
    {
        _save = null;
        _discard = null;
        if (IsDirty)
        {
            IsDirty = false;
            OnChange?.Invoke();
        }
    }

    public void MarkDirty()
    {
        if (IsDirty) return;
        IsDirty = true;
        OnChange?.Invoke();
    }

    public async Task SaveAsync()
    {
        if (_save != null) await _save();
        if (!IsDirty) return;
        IsDirty = false;
        OnChange?.Invoke();
    }

    public void Discard()
    {
        _discard?.Invoke();
        if (!IsDirty) return;
        IsDirty = false;
        OnChange?.Invoke();
    }
}
