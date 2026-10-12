using JackAll.Tools.World;

namespace JackAll.Tools.Hkx;

/// <summary>
/// An <c>.hkx</c> open for editing: the file as it stands, its undo history, and whether it differs from the bytes
/// last saved. Each step keeps the whole file from before and after it, which a prop's few kilobytes allow.
/// </summary>
public sealed class HkxEditSession
{
    private readonly HkxFile _original;
    private byte[] _saved;
    private byte[] _current;

    public HkxEditSession(byte[] bytes)
    {
        _saved = _current = bytes;
        File = HkxFile.Parse(bytes);
        _original = HkxFile.Parse(bytes);
    }

    public HkxFile File { get; private set; }

    public EditHistory History { get; } = new();

    public bool IsDirty { get; private set; }

    /// <summary>The file as it stood after the last edit, undo, redo or revert.</summary>
    public byte[] Bytes => _current;

    /// <summary>
    /// Raised after an edit, undo, redo or save, with <c>true</c> when <see cref="File"/> was parsed anew, so that
    /// every object in it is a new one.
    /// </summary>
    public event Action<bool>? Changed;

    /// <summary>Makes a change as one undoable step. Steps with the same merge key in quick succession fold into one.</summary>
    public void Edit(string label, Action change, string? mergeKey = null)
    {
        change();
        Commit(label, _current, mergeKey);
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

    /// <summary>Puts <see cref="File"/> back to bytes it was written as, parsing them anew.</summary>
    public void Revert(byte[] bytes)
    {
        File = HkxFile.Parse(bytes);
        Settle(bytes, reparsed: true);
    }

    /// <summary>
    /// Hands <paramref name="store"/> the file with every cache brought in line with its shapes; when it takes them,
    /// the file as edited becomes the new save point.
    /// </summary>
    /// <exception cref="InvalidOperationException">A MOPP that cannot be rebuilt would have to be.</exception>
    public bool Save(Func<byte[], bool> store)
    {
        HkxFile refreshed = HkxFile.Parse(_current);
        HkxCaches.Refresh(refreshed, _original);
        if (!store(refreshed.Write()))
        {
            return false;
        }
        _saved = _current;
        Settle(_current, reparsed: false);
        return true;
    }

    private void Settle(byte[] bytes, bool reparsed)
    {
        _current = bytes;
        IsDirty = !bytes.AsSpan().SequenceEqual(_saved);
        Changed?.Invoke(reparsed);
    }

    // A change as the whole file before and after it.
    private sealed class Step(HkxEditSession session, string label, byte[] before, byte[] after, string? mergeKey) : IEditStep
    {
        private byte[] _after = after;

        public string Label => label;

        public void Undo() => session.Revert(before);

        public void Redo() => session.Revert(_after);

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
