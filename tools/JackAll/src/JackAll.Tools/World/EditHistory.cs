namespace JackAll.Tools.World;

/// <summary>One undoable change to a loaded world, already applied when it is pushed.</summary>
public interface IEditStep
{
    string Label { get; }

    /// <summary>The entities the step touches.</summary>
    IReadOnlyCollection<WorldEntity> Entities { get; }

    /// <summary>Whether undoing or redoing it adds or removes entities, rather than changing them.</summary>
    bool ChangesMembership { get; }

    void Undo();

    void Redo();

    /// <summary>Folds <paramref name="next"/> into this step when both are one continuous edit, such as
    /// keystrokes into the same field.</summary>
    bool TryMerge(IEditStep next);
}

/// <summary>The Map tab's undo and redo stacks for one loaded world.</summary>
/// <param name="now">The clock merging is timed against.</param>
public sealed class EditHistory(Func<DateTime>? now = null)
{
    /// <summary>How soon after the last step a mergeable one must follow to merge into it, so two
    /// separate goes at the same field stay two steps.</summary>
    public static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(1.5);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly Stack<IEditStep> _undo = [];
    private readonly Stack<IEditStep> _redo = [];
    private DateTime _lastPush = DateTime.MinValue;

    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => _undo.TryPeek(out IEditStep? step) ? step.Label : null;

    public string? RedoLabel => _redo.TryPeek(out IEditStep? step) ? step.Label : null;

    public void Push(IEditStep step)
    {
        DateTime now = _now();
        bool recent = now - _lastPush <= MergeWindow;
        _lastPush = now;
        if (!recent || _redo.Count > 0 || !_undo.TryPeek(out IEditStep? top) || !top.TryMerge(step))
        {
            _undo.Push(step);
        }
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>Undoes the latest step and returns it, or null when there is none.</summary>
    public IEditStep? Undo() => Move(_undo, _redo, step => step.Undo());

    public IEditStep? Redo() => Move(_redo, _undo, step => step.Redo());

    private IEditStep? Move(Stack<IEditStep> from, Stack<IEditStep> to, Action<IEditStep> apply)
    {
        if (!from.TryPop(out IEditStep? step))
        {
            return null;
        }
        apply(step);
        to.Push(step);
        _lastPush = DateTime.MinValue;
        Changed?.Invoke();
        return step;
    }
}
