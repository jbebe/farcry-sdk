namespace JackAll.App;

/// <summary>A fixed tab that holds edits of its own until the user saves them.</summary>
public interface ISavableTab
{
    bool IsDirty { get; }

    event Action? DirtyChanged;

    /// <summary>Stages the unsaved edits; the text is why they could not be, or null.</summary>
    Task<string?> SaveAsync();
}
