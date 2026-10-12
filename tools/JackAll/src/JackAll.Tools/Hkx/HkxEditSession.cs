using JackAll.Tools.World;

namespace JackAll.Tools.Hkx;

/// <summary>
/// An <c>.hkx</c> open for editing: the file as it stands, its undo history, and whether it differs from the bytes
/// last saved. Each step keeps the whole file from before and after it, which a prop's few kilobytes allow.
/// </summary>
public sealed class HkxEditSession
{
    private readonly byte[] _opened;
    private byte[] _saved;

    public HkxEditSession(byte[] bytes)
    {
        _opened = _saved = bytes;
        File = HkxFile.Parse(bytes);
    }

    public HkxFile File { get; private set; }

    public EditHistory History { get; } = new();

    public bool IsDirty { get; private set; }

    /// <summary>
    /// Raised after an edit, undo, redo or save, with <c>true</c> when <see cref="File"/> was parsed anew, so that
    /// every object in it is a new one.
    /// </summary>
    public event Action<bool>? Changed;

    /// <summary>Makes a change as one undoable step. Steps with the same merge key in quick succession fold into one.</summary>
    public void Edit(string label, Action change, string? mergeKey = null)
    {
        byte[] before = File.Write();
        change();
        Commit(label, before, mergeKey);
    }

    /// <summary>Records the changes made to <see cref="File"/> since it was written as <paramref name="before"/> as one step.</summary>
    public void Commit(string label, byte[] before, string? mergeKey = null)
    {
        byte[] after = File.Write();
        if (!after.AsSpan().SequenceEqual(before))
        {
            History.Push(new Step(this, label, before, after, mergeKey));
        }
        Settle(after, reparsed: false);
    }

    public void Undo() => History.Undo();

    public void Redo() => History.Redo();

    /// <summary>
    /// Brings every cache in line with the shapes and returns the bytes to save, which become the new save point.
    /// </summary>
    /// <exception cref="InvalidOperationException">A MOPP that cannot be rebuilt would have to be.</exception>
    public byte[] Save()
    {
        HkxCaches.Refresh(File, HkxFile.Parse(_opened));
        _saved = File.Write();
        Settle(_saved, reparsed: false);
        return _saved;
    }

    private void Load(byte[] bytes)
    {
        File = HkxFile.Parse(bytes);
        Settle(bytes, reparsed: true);
    }

    private void Settle(byte[] bytes, bool reparsed)
    {
        IsDirty = !bytes.AsSpan().SequenceEqual(_saved);
        Changed?.Invoke(reparsed);
    }

    // A change as the whole file before and after it.
    private sealed class Step(HkxEditSession session, string label, byte[] before, byte[] after, string? mergeKey) : IEditStep
    {
        private byte[] _after = after;

        public string Label => label;

        public IReadOnlyCollection<WorldEntity> Entities => [];

        public bool ChangesMembership => false;

        public void Undo() => session.Load(before);

        public void Redo() => session.Load(_after);

        public bool TryMerge(IEditStep next)
        {
            if (mergeKey is null || next is not Step step || step.MergeKey != mergeKey)
            {
                return false;
            }
            _after = step._after;
            return true;
        }

        private string? MergeKey => mergeKey;
    }
}
