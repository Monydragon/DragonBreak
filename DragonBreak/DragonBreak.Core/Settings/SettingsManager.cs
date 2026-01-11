#nullable enable
using System;

namespace DragonBreak.Core.Settings;

public sealed class SettingsManager
{
    private readonly SettingsStore _store;

    public GameSettings Current { get; private set; }

    /// <summary>
    /// Settings being edited in UI. Null when not editing.
    /// </summary>
    public GameSettings? Pending { get; private set; }

    public event Action<GameSettings>? SettingsApplied;

    public SettingsManager(SettingsStore store)
    {
        _store = store;
        Current = _store.LoadOrDefault();
    }

    public void BeginEdit()
    {
        Pending = Current;
    }

    public void CancelEdit()
    {
        Pending = null;
    }

    public void SetPending(GameSettings pending)
    {
        Pending = (pending ?? GameSettings.Default).Validate();
    }

    public void ApplyPending()
    {
        if (Pending == null)
            return;

        Current = Pending.Validate();
        Pending = null;

        _store.Save(Current);
        SettingsApplied?.Invoke(Current);
    }

    public void SaveCurrent()
    {
        _store.Save(Current);
    }

    /// <summary>
    /// Update the current settings without raising <see cref="SettingsApplied"/>.
    /// Intended for passive changes like user window resizing.
    /// </summary>
    public void UpdateCurrent(GameSettings settings, bool save = true)
    {
        Current = (settings ?? GameSettings.Default).Validate();
        if (save)
            _store.Save(Current);
    }
}
