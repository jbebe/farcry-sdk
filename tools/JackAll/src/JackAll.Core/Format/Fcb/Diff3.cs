using DiffPlex;
using DiffPlex.Chunkers;

namespace JackAll.Core.Format.Fcb;

/// <summary>
/// A 3-way text merge. Pure text, no FCB/mod-layer knowledge, so it's independently testable against
/// plain strings; its caller feeds it the canonicalized fragments of a text format (see
/// <see cref="Mods.IContainerSplitter.Merge"/>).
/// </summary>
public static class Diff3
{
    private static readonly LineChunker Chunker = new();

    /// <summary>
    /// Merges <paramref name="ours"/> and <paramref name="theirs"/>, both relative to their shared
    /// <paramref name="ancestor"/>, the way `git merge-file`/diff3 do: a region changed by only one
    /// side is taken outright, a region changed identically by both is taken once, and a region
    /// changed differently by both sides is a conflict — <see cref="HasConflict"/> is set and the
    /// merged text carries `&lt;&lt;&lt;&lt;&lt;&lt;&lt;`/`=======`/`&gt;&gt;&gt;&gt;&gt;&gt;&gt;`
    /// markers around both versions (git-diff-shaped, per the design doc) rather than silently
    /// picking a side.
    /// </summary>
    /// <remarks>
    /// Delegates to DiffPlex's own <see cref="ThreeWayDiffer"/> rather than a hand-rolled diff3 —
    /// it already implements exactly this algorithm, including the conflict-marker format. When
    /// <paramref name="ours"/> equals <paramref name="ancestor"/>, every change is "theirs-only" and
    /// is taken outright with no conflict, for any input.
    /// </remarks>
    public static (string Merged, bool HasConflict) Merge(string ancestor, string ours, string theirs)
    {
        DiffPlex.Model.ThreeWayMergeResult result =
            ThreeWayDiffer.Instance.CreateMerge(ancestor, ours, theirs, ignoreWhiteSpace: false, ignoreCase: false, Chunker);

        // LineChunker drops each line's terminator; rejoin with the Environment.NewLine FragmentXml writes.
        return (string.Join(Environment.NewLine, result.MergedPieces), !result.IsSuccessful);
    }
}
