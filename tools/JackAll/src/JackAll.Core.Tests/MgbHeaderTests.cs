using JackAll.Core.Format;
using JackAll.Tools.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// Checks <see cref="MgbHeader"/> against a real .mgb file pulled straight out of the checked-in
/// patch fixture - the same file (<c>options.mgb</c>) whose hex was hand-decoded while reverse
/// engineering the format (see reverse/dunia/mgb_format.md), so the expected values here are the
/// ones that investigation already confirmed byte-for-byte, not just "whatever the code produces."
/// </summary>
public class MgbHeaderTests
{
    private const string FixturesDir = "Fixtures/Patch";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Decodes_a_real_mgb_files_header_and_type_table()
    {
        string fatPath = Path.Combine(FixturesDir, "patch.fat");
        if (!File.Exists(fatPath)) return;

        using var archive = DuniaArchive.Open(fatPath);
        uint hash = NameHash.Compute(@"ui\localized\pc\eng\ui\options.mgb");
        Assert.True(archive.TryGetEntry(hash, out var entry), "options.mgb not found in the patch fixture.");
        byte[] content = archive.Read(entry);

        MgbHeader header = MgbHeader.Decode(content);

        Assert.Equal(MgbHeader.ExpectedVersion, header.Version);
        Assert.Equal(166, header.Types.Count); // count byte 0xA7 (167) - 1, confirmed against the raw hex
        Assert.Equal(0x2A7, header.HeaderLength);

        // The format-level investigation matched 91/128 non-zero entries in this exact file (a first
        // pass against hand-guessed candidates); a second pass (2026-07-31), cross-referencing real
        // RTTI class names recovered from Dunia.dll against every entry in this file's own type table,
        // brought MgbTypeTable's own count up to 84/128. A third pass (2026-08-02) added 14 more
        // classes confirmed via a live Register() hook in an earlier session but never actually added
        // to MgbTypeTable's dictionary until now (a previous-session omission) - 8 of those 14 turned
        // out to be present in this exact file's own type table, bringing the count to 92/128.
        int resolved = header.Types.Count(t => t.Name is not null);
        Assert.Equal(92, resolved);

        // Every widget class this file's own UI actually uses (per its .mgb.desc sibling: a
        // brightness slider page) should be nameable.
        string?[] names = header.Types.Select(t => t.Name).ToArray();
        Assert.Contains("RectShape", names);
        Assert.Contains("Text", names);
        Assert.Contains("Page", names);
        Assert.Contains("Area", names);
    }

    [Fact]
    public void Rejects_a_file_without_the_MAGMA_magic()
    {
        byte[] notMgb = "<package></package>"u8.ToArray();
        Assert.Throws<InvalidDataException>(() => MgbHeader.Decode(notMgb));
    }
}
