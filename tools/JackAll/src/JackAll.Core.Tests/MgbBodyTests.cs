using JackAll.Core.Format;
using JackAll.Tools.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// Checks <see cref="MgbBody"/> against the same real <c>options.mgb</c> used by
/// <see cref="MgbHeaderTests"/>. <see cref="MgbTypeTable"/> doesn't yet name every class this file's
/// type table references (92/128 non-zero entries confirmed as of 2026-08-02 - see
/// docs/docs/file-formats/mgb.md), so a full parse of this file is expected to stop partway through
/// its 38 top-level areas rather than reach end of file -
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

        // Confirms this stopped for an expected, honest reason (not a real parse error) - and that it
        // got at least a few real areas in before stopping. The exact stop reason shifts as
        // MgbTypeTable's/MgbBody's coverage improves (currently an unrecognized UserData property type
        // tag deep inside top-level area index 7 - see ParseTypedTopLevelArea's remarks in MgbBody.cs
        // for why area 7 is even reachable now) - not asserted verbatim since it's expected to keep
        // moving forward as more of the format gets decoded.
        string stopReason = Field(root, "StoppedDecoding");
        Assert.False(string.IsNullOrEmpty(stopReason));
        MgbNode areas = Child(root, "Areas");
        Assert.True(areas.Children.Count >= 1, "Expected at least one area to decode before the gap.");
    }

    /// <summary>
    /// Locks in four 2026-08-02 fixes together: (1) the top-level area list dispatches through
    /// <c>Factory::MakeArea</c>, not the broad <c>Factory::MakeElement</c>; (2) a body type-id byte
    /// <c>B</c> maps to type-table entry <c>B-1</c> (previously documented as confirmed but never
    /// actually implemented); (3) <c>Area</c>/<c>Element</c>'s own base call reads a full <c>UserData</c>
    /// record (<c>NamedObject</c> + property list), not a bare <c>NamedObject</c> as previously assumed;
    /// (4) <c>MakeArea</c>'s ancestor-walk has a generic fallback branch (matching a universal root
    /// marker) in addition to its <c>Page</c>/<c>CheckBox</c>/<c>Button</c>/<c>Cursor</c> categories -
    /// a real file's own top-level area list includes a <c>Placeholder</c> byte (confirmed
    /// <c>Widget</c>-derived, not <c>Area</c>-derived) that only decodes if treated as this fallback
    /// (plain <c>Area</c> shape). Area 0 resolves to <c>Cursor</c> - matching the doc's independently
    /// live-observed real byte value (68 -&gt; Cursor) - and its two no-op child elements
    /// (<c>Placeholder</c>/<c>Handler</c>) decode without desyncing. Cross-validated: this exact area
    /// structure (including the same NameHash/TicksDenominator/StaticBox/Hotspot values) reproduces
    /// byte-for-byte in a completely different <c>options.mgb</c> build pulled from <c>tmp/menu/</c>
    /// (1280x800 PageSize vs this fixture's 1024x768) - strong evidence this is genuine shared template
    /// content, not a parser artifact.
    /// </summary>
    [Fact]
    public void Resolves_the_first_top_level_area_as_Cursor_via_the_MakeArea_dispatch()
    {
        string fatPath = Path.Combine(FixturesDir, "patch.fat");
        if (!File.Exists(fatPath)) return;

        using var archive = DuniaArchive.Open(fatPath);
        uint hash = NameHash.Compute(@"ui\localized\pc\eng\ui\options.mgb");
        Assert.True(archive.TryGetEntry(hash, out var entry));
        byte[] content = archive.Read(entry);

        MgbHeader header = MgbHeader.Decode(content);
        MgbNode root = MgbBody.ParsePackage(content, header);

        MgbNode areas = Child(root, "Areas");
        MgbNode cursor = areas.Children[0];
        Assert.Equal("Cursor", cursor.Kind);
        Assert.Equal("0xC2D36FB8", Field(cursor, "NameHash"));
        Assert.Equal("(none)", Field(cursor, "Action"));
        Assert.Equal("30", Field(cursor, "TicksDenominator"));

        MgbNode elements = Child(cursor, "Elements");
        Assert.Equal(["Placeholder", "Handler"], elements.Children.Select(e => e.Kind));

        // Areas 1-6 are the file's own genuine empty/reserved top-level slots (type-id byte 0), not a
        // decoding gap - confirmed identical across two independent real files (see remarks above).
        Assert.Equal(7, areas.Children.Count);
        Assert.All(areas.Children.Skip(1), a => Assert.Equal("(empty)", a.Kind));
    }

    private static string Field(MgbNode node, string label)
        => node.Fields.First(f => f.Label == label).Value;

    private static MgbNode Child(MgbNode node, string kind)
        => node.Children.First(c => c.Kind == kind);
}
