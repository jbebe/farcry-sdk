using JackAll.Core.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// Checks <see cref="MgbBody"/> against the same real <c>options.mgb</c> used by
/// <see cref="MgbHeaderTests"/>. <see cref="MgbTypeTable"/> doesn't yet name every class this file's
/// type table references (see reverse/dunia/mgb_format.md - 42/166 confirmed, not the full ~91/128
/// the RE investigation matched against a larger unverified candidate list), so a full parse of this
/// file is expected to stop partway through its 38 top-level areas rather than reach end of file -
/// that's <see cref="MgbBody.ParsePackage"/>'s documented graceful-degradation behavior, not a bug.
/// What this test actually checks: everything decoded before that point is byte-correct, cross-checked
/// against real values - the <c>PAGESIZE</c>/material texture names below are independently confirmed
/// by hand against this exact file's own <c>.mgb.desc</c> sidecar (its <c>&lt;CTextureResource&gt;</c>
/// dependency list names the same two textures).
/// </summary>
[Trait("Category", "RequiresFixture")]
public class MgbBodyTests
{
    private const string FixturesDir = "Fixtures/Patch";

    [Fact]
    public void Parses_the_package_preamble_of_a_real_mgb_file_and_stops_cleanly_at_the_first_unknown_class()
    {
        string fatPath = Path.Combine(FixturesDir, "patch.fat");
        if (!File.Exists(fatPath)) return;

        using var archive = DuniaArchive.Open(fatPath);
        uint hash = NameHash.Compute(@"ui\localized\pc\eng\ui\options.mgb");
        Assert.True(archive.TryGetEntry(hash, out var entry));
        byte[] content = archive.Read(entry);

        MgbHeader header = MgbHeader.Decode(content);

        // Never throws - a real file with type-table gaps degrades to a partial tree instead.
        MgbNode root = MgbBody.ParsePackage(content, header);

        Assert.Equal("Package", root.Kind);
        Assert.Equal("1024 x 768", Field(root, "PageSize"));
        Assert.Equal("(32, 24)", Field(root, "DisplayOffset"));

        MgbNode materials = Child(root, "Materials");
        Assert.Equal(2, materials.Children.Count);
        Assert.Equal(@"\textures\common\option_sketch.png", Field(materials.Children[0], "Texture"));
        Assert.Equal(@"\textures\common\brightness_lines.png", Field(materials.Children[1], "Texture"));

        // Confirms this stopped for the expected reason (an unnamed class, not a real parse error) -
        // and that it got at least a few real areas in before stopping.
        string stopReason = Field(root, "StoppedDecoding");
        Assert.Contains("unrecognized class", stopReason);
        MgbNode areas = Child(root, "Areas");
        Assert.True(areas.Children.Count >= 1, "Expected at least one area to decode before the gap.");
    }

    private static string Field(MgbNode node, string label)
        => node.Fields.First(f => f.Label == label).Value;

    private static MgbNode Child(MgbNode node, string kind)
        => node.Children.First(c => c.Kind == kind);
}
