using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Tests;

/// <summary>
/// The correctness surface of docs/design/fcb-fragment-overlays.md's Milestone 3, tested at the
/// plain-string level — no FCB/XML involved, so these are fast and precise about the merge
/// algorithm's own contract, independent of how GameVfs/PatchBuilder use it.
/// </summary>
/// <remarks>
/// Lines are joined with <see cref="Environment.NewLine"/>, not a hardcoded "\n" - <see cref="Diff3.Merge"/>
/// rejoins with <see cref="Environment.NewLine"/> too (matching what <c>FcbXml.Render</c> itself emits),
/// so the no-op/verbatim cases below need matching input formatting to assert byte-for-byte equality.
/// </remarks>
public class Diff3Tests
{
    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    private static readonly string Ancestor = Lines("line1", "line2", "line3", "line4");

    [Fact]
    public void Non_overlapping_edits_from_both_sides_both_land_in_the_merged_result()
    {
        string ours = Lines("line1", "line2-OURS", "line3", "line4");
        string theirs = Lines("line1", "line2", "line3", "line4-THEIRS");

        (string merged, bool hasConflict) = Diff3.Merge(Ancestor, ours, theirs);

        Assert.False(hasConflict);
        Assert.Contains("line2-OURS", merged);
        Assert.Contains("line4-THEIRS", merged);
    }

    [Fact]
    public void Both_sides_making_the_identical_edit_merges_cleanly_to_that_edit()
    {
        string edited = Lines("line1", "line2", "line3-SAME", "line4");

        (string merged, bool hasConflict) = Diff3.Merge(Ancestor, edited, edited);

        Assert.False(hasConflict);
        Assert.Equal(edited, merged);
    }

    [Fact]
    public void Overlapping_edits_to_the_same_line_with_different_content_conflict()
    {
        string ours = Lines("line1", "line2", "line3-FROM-OURS", "line4");
        string theirs = Lines("line1", "line2", "line3-FROM-THEIRS", "line4");

        (string merged, bool hasConflict) = Diff3.Merge(Ancestor, ours, theirs);

        Assert.True(hasConflict);
        Assert.Contains("<<<<<<<", merged);
        Assert.Contains("=======", merged);
        Assert.Contains(">>>>>>>", merged);
        Assert.Contains("line3-FROM-OURS", merged);
        Assert.Contains("line3-FROM-THEIRS", merged);
    }

    [Fact]
    public void Ours_unchanged_from_ancestor_means_theirs_wins_outright_with_no_conflict()
    {
        // The exact case GameVfs/PatchBuilder rely on for a fragment touched by exactly one layer:
        // the running "ours" result starts equal to the ancestor, so this fold must always be a
        // no-op pass-through onto whatever "theirs" is, for any input.
        string theirs = Lines("line1", "line2-CHANGED", "line3", "line4-ALSO-CHANGED");

        (string merged, bool hasConflict) = Diff3.Merge(Ancestor, Ancestor, theirs);

        Assert.False(hasConflict);
        Assert.Equal(theirs, merged);
    }

    [Fact]
    public void Both_sides_unchanged_returns_the_ancestor_verbatim()
    {
        (string merged, bool hasConflict) = Diff3.Merge(Ancestor, Ancestor, Ancestor);

        Assert.False(hasConflict);
        Assert.Equal(Ancestor, merged);
    }
}
