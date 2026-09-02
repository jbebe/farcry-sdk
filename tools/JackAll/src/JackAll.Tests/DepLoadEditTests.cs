using JackAll.Core.Format;

namespace JackAll.Tests;

/// <summary>
/// Adding an entry has to leave the rest of the file alone. The slices shift, so "alone" means every
/// other parent still lists the same children - which is checked against real files rather than a
/// hand-built one, because the shipped layout is the thing an edit can silently scramble.
/// </summary>
public class DepLoadEditTests
{
    private const uint Animation = 0xB0604725;
    private const uint DartRifle = 115510436;

    public static TheoryData<string> CorpusFiles() => DepLoadDocumentTests.CorpusFiles();

    private static Dictionary<uint, uint[]> ChildrenByParent(DepLoadFile file)
        => file.Parents.ToDictionary(p => p.Hash, p => p.Children.Select(c => c.Hash).ToArray());

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Adding_a_clip_disturbs_nothing_else_and_still_encodes(string path)
    {
        if (path.Length == 0) return;

        DepLoadFile before = DepLoadDocument.Decode(File.ReadAllBytes(path));
        const uint newClip = 0x11641D75;

        DepLoadFile after = DepLoadDocument.Decode(
            DepLoadDocument.Encode(DepLoadEdit.AddChild(before, DartRifle, newClip, Animation)));

        Assert.Empty(DepLoadValidate.Problems(after));

        Dictionary<uint, uint[]> was = ChildrenByParent(before);
        foreach ((uint parent, uint[] children) in ChildrenByParent(after))
        {
            uint[] expected = parent == DartRifle ? [.. was.GetValueOrDefault(parent, []), newClip] : was[parent];
            Assert.Equal(expected, children);
        }

        Assert.Contains(after.Parents, p => p.Hash == DartRifle && p.Children.Any(c => c.Hash == newClip));
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Adding_the_same_clip_twice_lists_it_once(string path)
    {
        if (path.Length == 0) return;

        DepLoadFile file = DepLoadDocument.Decode(File.ReadAllBytes(path));
        const uint newClip = 0x11641D75;

        DepLoadFile once = DepLoadEdit.AddChild(file, DartRifle, newClip, Animation);
        DepLoadFile twice = DepLoadEdit.AddChild(once, DartRifle, newClip, Animation);

        Assert.Equal(DepLoadDocument.Encode(once), DepLoadDocument.Encode(twice));
    }

    [Fact]
    public void A_new_parent_lands_in_sorted_position()
    {
        var file = new DepLoadFile([
            new DepLoadParent(0x10, 0, [new DepLoadChild(0xA1, Animation)]),
            new DepLoadParent(0x30, 1, [new DepLoadChild(0xA2, Animation)]),
        ]);

        DepLoadFile added = DepLoadEdit.AddChild(file, 0x20, 0xA3, Animation);
        DepLoadFile written = DepLoadDocument.Decode(DepLoadDocument.Encode(added));

        Assert.Equal([0x10u, 0x20u, 0x30u], written.Parents.Select(p => p.Hash));
        Assert.Empty(DepLoadValidate.Problems(written));
    }

    /// <summary>
    /// A parent hash above int.MaxValue has to sort after a smaller one. Comparing these signed is the
    /// documented way to produce a file that loads and then misbehaves.
    /// </summary>
    [Fact]
    public void Parents_sort_unsigned()
    {
        var file = new DepLoadFile([
            new DepLoadParent(0xF0000000, 0, [new DepLoadChild(0xA1, Animation)]),
            new DepLoadParent(0x00000010, 1, [new DepLoadChild(0xA2, Animation)]),
        ]);

        DepLoadFile written = DepLoadDocument.Decode(DepLoadDocument.Encode(file));

        Assert.Equal([0x10u, 0xF0000000u], written.Parents.Select(p => p.Hash));
    }

    [Fact]
    public void Out_of_order_parents_are_reported_rather_than_passed_over()
    {
        var file = new DepLoadFile([
            new DepLoadParent(0x30, 0, []),
            new DepLoadParent(0x10, 0, []),
        ]);

        Assert.Single(DepLoadValidate.Problems(file));
    }
}
