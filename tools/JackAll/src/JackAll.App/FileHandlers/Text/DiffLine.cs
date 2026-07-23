namespace JackAll.App.FileHandlers.Text;

public enum DiffLineKind
{
    Unchanged,
    Added,
    Removed,

    /// <summary>A synthetic marker line standing in for a run of unchanged lines that got trimmed -
    /// see <see cref="DiffTextBuilder"/>.</summary>
    Gap,
}

public readonly record struct DiffLine(string Text, DiffLineKind Kind);
