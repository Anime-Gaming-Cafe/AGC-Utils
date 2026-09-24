namespace AGC_Management.Services;

// Scoped: one dirty-state per Blazor Server circuit. The displayed page registers its save/discard
// logic on load and unregisters on dispose; UnsavedChangesBar is the only thing that reads it.
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

    private bool _rejected;

    /// <summary>Called from inside the save delegate when validation fails, so the changes stay marked as unsaved.</summary>
    public void RejectSave()
    {
        _rejected = true;
    }

    public async Task<bool> SaveAsync()
    {
        _rejected = false;
        if (_save != null) await _save();
        if (_rejected) return false;
        if (!IsDirty) return true;
        IsDirty = false;
        OnChange?.Invoke();
        return true;
    }

    public void Discard()
    {
        _discard?.Invoke();
        if (!IsDirty) return;
        IsDirty = false;
        OnChange?.Invoke();
    }
}
