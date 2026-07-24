using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Tests;

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
    public void Replacing_one_fragment_changes_only_that_child_and_leaves_every_other_byte_identical(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] baseFcb = File.ReadAllBytes(path);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        IReadOnlyList<string> ids = FcbXml.ListFragmentIds(original);
        Assert.NotEmpty(ids); // every fixture here is entity-library-of-groups shaped

        string targetId = ids[0];

        // A hand-built replacement group, unrelated to the original content - if the assembler
        // spliced the wrong child, or corrupted a sibling, this would show up unmistakably below.
        var replacement = new FcbObject { TypeHash = 0xE0BDB3DB }; // EntityLibraryGroup
        replacement.Values.Add(0xDEADBEEF, [0x01, 0x02, 0x03, 0x04]);
        string replacementXml = FcbXml.ToXml(replacement, FcbClassDefinitions.Empty).IndexXml;

        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string> { [targetId] = replacementXml });
        FcbObject rebuilt = FcbDocument.Deserialize(assembled);

        Assert.Equal(original.Children.Count, rebuilt.Children.Count);

        IReadOnlyList<string> rebuiltIds = FcbXml.ListFragmentIds(rebuilt);
        int targetIndex = ids.ToList().IndexOf(targetId);

        for (int i = 0; i < original.Children.Count; i++)
        {
            if (i == targetIndex)
            {
                Assert.Equal(0xE0BDB3DBu, rebuilt.Children[i].TypeHash);
                Assert.Equal([0x01, 0x02, 0x03, 0x04], rebuilt.Children[i].Values[0xDEADBEEF]);
            }
            else
            {
                AssertSameShape(original.Children[i], rebuilt.Children[i]);
                // The replacement's own id is deterministic from its content/position, so an
                // untouched sibling must keep the exact same id too.
                Assert.Equal(ids[i], rebuiltIds[i]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void A_fragment_id_with_no_matching_child_is_appended_as_a_new_entry(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] baseFcb = File.ReadAllBytes(path);
        FcbObject original = FcbDocument.Deserialize(baseFcb);

        var addition = new FcbObject { TypeHash = 0xE0BDB3DB }; // EntityLibraryGroup
        addition.Values.Add(0xDEADBEEF, [0x2A, 0x00, 0x00, 0x00]);
        string additionXml = FcbXml.ToXml(addition, FcbClassDefinitions.Empty).IndexXml;

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
