using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// Runs against the same real shipped, splitting .fcb fixtures as <see cref="FcbXmlTests"/> — the
/// strongest available check that splicing a fragment override back into a container reproduces
/// exactly the container the game would have if you replaced that one child by hand and recompiled.
/// </summary>
[Trait("Category", "RequiresFixture")]
public class FcbAssemblerTests
{
    private const string FixturesDir = "Fixtures/Fcb";

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        if (!Directory.Exists(FixturesDir))
        {
            data.Add(string.Empty);
            return data;
        }
        foreach (string file in Directory.EnumerateFiles(FixturesDir, "*.fcb"))
        {
            data.Add(file);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Applying_no_overrides_returns_the_exact_same_bytes_unchanged(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] baseFcb = File.ReadAllBytes(path);
        byte[] result = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>());

        Assert.Same(baseFcb, result); // no decode/encode round trip at all - not just byte-equal
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Replacing_one_archetype_changes_only_that_fragment_and_leaves_every_other_one_identical(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] baseFcb = File.ReadAllBytes(path);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        IReadOnlyList<FcbFragment> fragments = FcbFragments.List(original);
        Assert.NotEmpty(fragments); // every fixture here is an entity library full of archetypes

        // A replacement prototype that keeps the vanilla identity (same hidName on its Entity child,
        // which is what the id derives from) but otherwise unrelated content - if the assembler
        // spliced the wrong node, or corrupted a sibling, this shows up unmistakably below.
        string targetId = fragments[0].Id;
        var replacement = new FcbObject { TypeHash = WorldHashes.EntityPrototype };
        replacement.Values.Add(0xDEADBEEF, [0x01, 0x02, 0x03, 0x04]);
        var entity = new FcbObject { TypeHash = WorldHashes.Entity };
        entity.Values.Add(
            WorldHashes.HidName,
            fragments[0].Node.Children.First(c => c.TypeHash == WorldHashes.Entity).Values[WorldHashes.HidName]);
        replacement.Children.Add(entity);
        string replacementXml = FcbXml.ToXml(replacement, FcbClassDefinitions.Empty);

        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string> { [targetId] = replacementXml });
        FcbObject rebuilt = FcbDocument.Deserialize(assembled);

        Assert.Equal(original.Children.Count, rebuilt.Children.Count);
        IReadOnlyList<FcbFragment> rebuiltFragments = FcbFragments.List(rebuilt);
        Assert.Equal(fragments.Count, rebuiltFragments.Count);

        for (int i = 0; i < fragments.Count; i++)
        {
            Assert.Equal(fragments[i].Id, rebuiltFragments[i].Id);
            if (FcbFragments.IdComparer.Equals(fragments[i].Id, targetId))
            {
                AssertSameShape(replacement, rebuiltFragments[i].Node);
            }
            else
            {
                AssertSameShape(fragments[i].Node, rebuiltFragments[i].Node);
            }
        }
    }

    /// <summary>The pre-deep-fragment id space is gone: a whole-group <c>NN_Name.xml</c> override
    /// staged by an older version replaces nothing and lands as new content instead.</summary>
    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void A_pre_deep_group_id_is_not_an_alias_and_appends_as_new_content(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] baseFcb = File.ReadAllBytes(path);
        FcbObject original = FcbDocument.Deserialize(baseFcb);

        string groupId = TestSupport.PreDeepGroupId(original, 0);

        var replacement = new FcbObject { TypeHash = 0xE0BDB3DB }; // EntityLibraryGroup
        replacement.Values.Add(0xDEADBEEF, [0x01, 0x02, 0x03, 0x04]);
        string replacementXml = FcbXml.ToXml(replacement, FcbClassDefinitions.Empty);

        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string> { [groupId] = replacementXml });
        FcbObject rebuilt = FcbDocument.Deserialize(assembled);

        Assert.Equal(original.Children.Count + 1, rebuilt.Children.Count);
        for (int i = 0; i < original.Children.Count; i++)
        {
            AssertSameShape(original.Children[i], rebuilt.Children[i]);
        }
        AssertSameShape(replacement, rebuilt.Children[^1]);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void A_plain_fragment_id_with_no_match_is_appended_at_the_root(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] baseFcb = File.ReadAllBytes(path);
        FcbObject original = FcbDocument.Deserialize(baseFcb);

        var addition = new FcbObject { TypeHash = 0xE0BDB3DB }; // EntityLibraryGroup
        addition.Values.Add(0xDEADBEEF, [0x2A, 0x00, 0x00, 0x00]);
        string additionXml = FcbXml.ToXml(addition, FcbClassDefinitions.Empty);

        byte[] assembled = FcbAssembler.Apply(
            baseFcb, new Dictionary<string, string> { ["99999_does_not_exist.xml"] = additionXml });
        FcbObject rebuilt = FcbDocument.Deserialize(assembled);

        // Every original child survives untouched, plus exactly one new one at the end.
        Assert.Equal(original.Children.Count + 1, rebuilt.Children.Count);
        for (int i = 0; i < original.Children.Count; i++)
        {
            AssertSameShape(original.Children[i], rebuilt.Children[i]);
        }
        FcbObject added = rebuilt.Children[^1];
        Assert.Equal(0xE0BDB3DBu, added.TypeHash);
        Assert.Equal([0x2A, 0x00, 0x00, 0x00], added.Values[0xDEADBEEF]);
    }

    /// <summary>A brand-new archetype (a path-shaped id matching nothing) joins the library's last
    /// group, not the root — the shape-defined append parent (<see cref="FcbFragments.AppendTarget"/>).</summary>
    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void A_new_archetype_id_is_appended_into_the_last_group(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] baseFcb = File.ReadAllBytes(path);
        FcbObject original = FcbDocument.Deserialize(baseFcb);

        var addition = new FcbObject { TypeHash = WorldHashes.EntityPrototype };
        addition.Values.Add(0xDEADBEEF, [0x2A, 0x00, 0x00, 0x00]);
        string additionXml = FcbXml.ToXml(addition, FcbClassDefinitions.Empty);

        byte[] assembled = FcbAssembler.Apply(
            baseFcb, new Dictionary<string, string> { [@"mymod\Weapons\BrandNew.xml"] = additionXml });
        FcbObject rebuilt = FcbDocument.Deserialize(assembled);

        Assert.Equal(original.Children.Count, rebuilt.Children.Count);
        FcbObject added = rebuilt.Children[^1].Children[^1];
        Assert.Equal([0x2A, 0x00, 0x00, 0x00], added.Values[0xDEADBEEF]);
        Assert.Equal(original.Children[^1].Children.Count + 1, rebuilt.Children[^1].Children.Count);
    }

    private static void AssertSameShape(FcbObject expected, FcbObject actual)
    {
        Assert.Equal(expected.TypeHash, actual.TypeHash);
        Assert.Equal(expected.Values.Keys.OrderBy(k => k), actual.Values.Keys.OrderBy(k => k));
        foreach (uint key in expected.Values.Keys)
        {
            Assert.Equal(expected.Values[key], actual.Values[key]);
        }

        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (int i = 0; i < expected.Children.Count; i++)
        {
            AssertSameShape(expected.Children[i], actual.Children[i]);
        }
    }
}
