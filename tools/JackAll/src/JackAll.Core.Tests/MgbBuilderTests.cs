using JackAll.Tools.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// Checks the write side added for the FCSE "Mod Configuration Menu" goal:
/// <see cref="MgbFileBuilder"/> (build a brand-new Page+CheckBox-rows file from scratch) and
/// <see cref="MgbPageEditor"/> (splice a new Button into an existing Page's children). Both are
/// validated purely against this project's own, independently-written read path
/// (<see cref="MgbHeader.Decode"/>/<see cref="MgbBody.ParsePackage(byte[], MgbHeader, List{MgbAreaLocation}?)"/>)
/// - real shipped `.mgb` files can't currently be parsed deep enough to reach an actual `Page` node
/// (see docs/docs/file-formats/mgb.md's Unknowns - a separately tracked, unrelated gap), so splicing
/// against JackAll's own generated content is the honest end-to-end check available today.
/// </summary>
public class MgbBuilderTests
{
    [Fact]
    public void BuildModsPage_round_trips_through_the_existing_reader()
    {
        MgbFileBuilder.ModCheckBoxRow[] rows =
        [
            new("Enable toxic-behavior flagging", true),
            new("Enable exemplary-behavior flagging", false),
            new("Show companion-site login prompt", true),
        ];

        byte[] content = MgbFileBuilder.BuildModsPage("FCSE_ModsPage", (1024, 768), rows);

        MgbHeader header = MgbHeader.Decode(content);
        Assert.Equal(3, header.Types.Count);
        Assert.Contains(header.Types, t => t.Name == "Page");
        Assert.Contains(header.Types, t => t.Name == "CheckBox");
        Assert.Contains(header.Types, t => t.Name == "Button");

        MgbNode root = MgbBody.ParsePackage(content, header);
        Assert.DoesNotContain(root.Fields, f => f.Label == "StoppedDecoding");
        Assert.Equal($"{content.Length - header.HeaderLength:N0} (file has {content.Length - header.HeaderLength:N0} body bytes total)",
            root.Fields.First(f => f.Label == "BytesConsumed").Value);
        Assert.Equal("1024 x 768", root.Fields.First(f => f.Label == "PageSize").Value);

        MgbNode areas = root.Children.First(c => c.Kind == "Areas");
        MgbNode page = Assert.Single(areas.Children);
        Assert.Equal("Page", page.Kind);
        Assert.Equal($"0x{MgbTypeTable.ComputeHash("FCSE_ModsPage"):X8}", page.Fields.First(f => f.Label == "NameHash").Value);

        MgbNode elements = page.Children.First(c => c.Kind == "Elements");
        Assert.Equal(3, elements.Children.Count);
        Assert.All(elements.Children, e => Assert.Equal("CheckBox", e.Kind));
        for (int i = 0; i < rows.Length; i++)
        {
            Assert.Equal($"0x{rows[i].NameHash:X8}", elements.Children[i].Fields.First(f => f.Label == "NameHash").Value);
        }
    }

    [Fact]
    public void BuildModsPage_with_zero_rows_still_parses_as_an_empty_page()
    {
        byte[] content = MgbFileBuilder.BuildModsPage("FCSE_EmptyPage", (800, 600), []);
        MgbHeader header = MgbHeader.Decode(content);
        MgbNode root = MgbBody.ParsePackage(content, header);
        Assert.DoesNotContain(root.Fields, f => f.Label == "StoppedDecoding");

        MgbNode page = root.Children.First(c => c.Kind == "Areas").Children.Single();
        Assert.Equal("Page", page.Kind);
        Assert.DoesNotContain(page.Children, c => c.Kind == "Elements"); // ParseArea only emits this child when there's at least one element
    }

    [Fact]
    public void AddButtonToTopLevelPage_splices_a_new_last_child_and_bumps_elementCount()
    {
        MgbFileBuilder.ModCheckBoxRow[] rows = [new("Row A", true), new("Row B", false)];
        byte[] original = MgbFileBuilder.BuildModsPage("FCSE_ModsPage", (1024, 768), rows);

        byte[] edited = MgbPageEditor.AddButtonToTopLevelPage(original, topLevelPageIndex: 0, "Back",
            new MgbBox(0, 700, 200, 732));

        Assert.True(edited.Length > original.Length);

        MgbHeader header = MgbHeader.Decode(edited);
        MgbNode root = MgbBody.ParsePackage(edited, header);
        Assert.DoesNotContain(root.Fields, f => f.Label == "StoppedDecoding");

        MgbNode page = root.Children.First(c => c.Kind == "Areas").Children.Single();
        MgbNode elements = page.Children.First(c => c.Kind == "Elements");
        Assert.Equal(3, elements.Children.Count);
        Assert.Equal("CheckBox", elements.Children[0].Kind);
        Assert.Equal("CheckBox", elements.Children[1].Kind);
        Assert.Equal("Button", elements.Children[2].Kind);
        Assert.Equal($"0x{MgbTypeTable.ComputeHash("Back"):X8}", elements.Children[2].Fields.First(f => f.Label == "NameHash").Value);

        // Everything before the splice point (header, config block, the two original CheckBox rows'
        // own bytes) is untouched - a real byte-preserving edit, not a full re-serialize.
        int firstDivergence = Enumerable.Range(0, original.Length).First(i => original[i] != edited[i]);
        Assert.True(firstDivergence > header.HeaderLength, "The entire header should be untouched by the splice.");
    }

    [Fact]
    public void AddButtonToTopLevelPage_can_be_applied_twice()
    {
        byte[] original = MgbFileBuilder.BuildModsPage("FCSE_ModsPage", (1024, 768), [new("Row A", true)]);
        byte[] once = MgbPageEditor.AddButtonToTopLevelPage(original, 0, "Back", new MgbBox(0, 700, 200, 732));
        byte[] twice = MgbPageEditor.AddButtonToTopLevelPage(once, 0, "Apply", new MgbBox(0, 740, 200, 772));

        MgbHeader header = MgbHeader.Decode(twice);
        MgbNode root = MgbBody.ParsePackage(twice, header);
        Assert.DoesNotContain(root.Fields, f => f.Label == "StoppedDecoding");

        MgbNode page = root.Children.First(c => c.Kind == "Areas").Children.Single();
        MgbNode elements = page.Children.First(c => c.Kind == "Elements");
        Assert.Equal(["CheckBox", "Button", "Button"], elements.Children.Select(e => e.Kind));
    }

    [Fact]
    public void AddButtonToTopLevelPage_rejects_a_file_whose_type_table_lacks_Button()
    {
        // Hand-build a minimal file whose type table only has "Page"/"CheckBox" - mirrors
        // MgbSyntheticTests' style, deliberately without "Button" - via BuildHeader plus an inlined
        // empty-Page Area shape (NameHash, 0 UserData props, no action, 0/0 ticks, 0 children, a
        // StaticBox), rather than reaching into MgbFileBuilder's own internals for one negative test.
        (byte[] header, IReadOnlyDictionary<string, byte> typeIds) = MgbFileBuilder.BuildHeader(["Page", "CheckBox"]);

        var w = new MgbWriter();
        w.WriteBytes(header);
        for (int i = 0; i < 65; i++) w.WriteValue(0);
        w.WriteInt(0xAAAAAAAA); w.WriteInt(0);
        w.WriteU16(100); w.WriteU16(100);
        w.WriteU16(0); w.WriteU16(0);
        w.WriteInt(0); w.WriteInt(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);
        w.WriteInt(1); // areaCount
        w.WriteByte(typeIds["Page"]);
        w.WriteInt(0xBBBBBBBB); w.WriteInt(0); // Page's own NamedObject + 0 UserData props
        w.WriteBool(false);                    // no attached action
        w.WriteValue(0); w.WriteValue(0);      // ticksDenom, durationMult
        w.WriteValue(0);                        // elementCount = 0
        w.WriteU16(0); w.WriteU16(0); w.WriteU16(100); w.WriteU16(100); // StaticBox
        w.WriteInt(0); w.WriteBool(false);      // Page's own tagCount, GlobalSelectionMode
        w.WriteBool(false); w.WriteBool(false); w.WriteInt(0); // package trailer

        byte[] content = w.ToArray();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MgbPageEditor.AddButtonToTopLevelPage(content, 0, "Back", new MgbBox(0, 0, 10, 10)));
        Assert.Contains("Button", ex.Message);
    }
}
