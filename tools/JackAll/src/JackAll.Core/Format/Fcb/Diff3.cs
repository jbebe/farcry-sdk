using DiffPlex;
using DiffPlex.Chunkers;

namespace JackAll.Core.Format.Fcb;

/// <summary>
/// A 3-way text merge — the core of docs/design/fcb-fragment-overlays.md's Milestone 3. Pure text,
/// no FCB/mod-layer knowledge, so it's independently testable against plain strings; its only real
/// caller feeds it canonicalized fragment XML (see <see cref="FcbXml.CanonicalizeFragment"/>).
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
    /// <paramref name="ours"/> equals <paramref name="ancestor"/> (the common case: exactly one
    /// layer touches this fragment), every change is "theirs-only" and is taken outright with no
    /// conflict, for any input — this is what keeps a single contributing layer's fold a no-op
    /// pass-through, unconditionally.
    /// </remarks>
    public static (string Merged, bool HasConflict) Merge(string ancestor, string ours, string theirs)
    {
        DiffPlex.Model.ThreeWayMergeResult result =
            ThreeWayDiffer.Instance.CreateMerge(ancestor, ours, theirs, ignoreWhiteSpace: false, ignoreCase: false, Chunker);

        // LineChunker strips each line's own terminator on the way in, so rejoining has to supply one
        // back - Environment.NewLine matches what FcbXml.Render's XDocument.ToString() itself emits,
        // which is what keeps a fragment touched by exactly one layer byte-for-byte identical to what
        // it was before Milestone 3 (see the no-op guarantee above), not just line-for-line equal.
        return (string.Join(Environment.NewLine, result.MergedPieces), !result.IsSuccessful);
    }
}
