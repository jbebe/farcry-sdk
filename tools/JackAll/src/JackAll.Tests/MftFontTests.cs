using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Mgb;
using JackAll.Core.Mods;
using JackAll.Tools.Mft;
using JackAll.Tools.Xbt;

namespace JackAll.Tests;

/// <summary>The Magma font: the stock fonts round-trip, and a font file draws into one the game reads.</summary>
public class MftFontTests
{
    /// <summary>One page, Latin, with the page it names beside it.</summary>
    private const string Farcry = "Mft/farcry2_25.mft";
    private const string FarcryPage = "Mft/farcry2_25_0.xbt";

    /// <summary>Three pages, Cyrillic and Greek, its glyphs out of code order.</summary>
    private const string Cyrillic = "Mft/arial_cyrillic_25.mft";

    /// <summary>A variable font: a weight axis and two named styles, and no Greek.</summary>
    private const string Caveat = "Mft/Caveat-VariableFont_wght.ttf";

    private const string FontsPackage = "Mgb/fonts.mgb";
    private const string UiList = "UiDepLoad/_depload.xml";
    private const string Ui = @"ui\localized\pcwidescreen\eng\ui";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Farcry, FarcryPage, Cyrillic, Caveat, FontsPackage, UiList);

    [Theory]
    [InlineData(Farcry)]
    [InlineData(Cyrillic)]
    public void Writes_back_the_shipped_bytes(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        Fixture.AssertSameBytes(path, original, MftFont.Read(original).Write());
    }

    [Theory]
    [InlineData(Farcry)]
    [InlineData(Cyrillic)]
    public void Reads_back_from_its_xml(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        XElement xml = XElement.Parse(MftFont.Read(original).ToXml().ToString());
        Fixture.AssertSameBytes(path, original, MftFont.FromXml(xml).Write());
    }

    /// <summary>The stock font's own page sets text, so the cell a glyph's UVs name is where it is drawn.</summary>
    [Fact]
    public void A_stock_font_sets_text_from_its_page()
    {
        if (Fixture.Read(Farcry) is not { } mft || Fixture.Read(FarcryPage) is not { } xbt) return;

        (byte[] rgba, int width, int height) = XbtPixels.TryDecode(xbt)!.Value;
        (byte[] text, _, _) = MftPreview.Render(MftFont.Read(mft), [FontPage.FromRgba(width, height, rgba)], "Ink", ink: 0xFF000000, paper: 0xFFFFFFFF);

        Assert.Contains(Enumerable.Range(0, text.Length / 4), i => text[i * 4] < 64);
    }

    [Fact]
    public void A_variable_font_lists_its_named_styles_and_axis()
    {
        if (Fixture.Read(Caveat) is not { } file) return;

        IReadOnlyList<FontFace> faces = FontRaster.Faces(file);
        Assert.Equal(["Regular", "Bold"], faces.Select(f => f.Style));
        FontAxis weight = Assert.Single(faces[1].Axes);
        Assert.Equal(("wght", 400f, 700f, 700f), (weight.Tag, weight.Minimum, weight.Maximum, weight.Value));
    }

    /// <summary>Every character the game uses that the font has, in code order after the empty glyph,
    /// each cell on the page, and the font reads back as written.</summary>
    [Fact]
    public void A_built_font_holds_what_the_file_has_and_reads_back()
    {
        if (Fixture.Read(Caveat) is not { } file) return;

        BuiltFont built = Build(file);
        MftFont font = built.Font;

        Assert.Equal(MftFont.MissingCode, font.Glyphs[0].Code);
        Assert.Equal(font.Glyphs.Skip(1).Select(g => g.Code).Order(), font.Glyphs.Skip(1).Select(g => g.Code));
        Assert.Contains(font.Glyphs, g => g.Code == 'A' && g.Width > 0);
        Assert.Contains(0x0391, built.Missing);
        Assert.Equal(MftBuilder.GameCharacters.Sum(r => r.Last - r.First + 1), font.CharacterCount + built.Missing.Count);
        FontPage page = Assert.Single(built.Pages);
        Assert.All(font.Glyphs, g => Assert.True(g.U1 * page.Width <= page.Width + 0.5f && g.V1 * page.Height <= page.Height + 0.5f));
        Assert.Equal(font.Write(), MftFont.Read(font.Write()).Write());
    }

    /// <summary>Left out on request, and so not reported as characters the font lacks; the rest stay.</summary>
    [Fact]
    public void A_font_can_leave_out_cyrillic()
    {
        if (Fixture.Read(Caveat) is not { } file) return;

        BuiltFont all = Build(file);
        BuiltFont latin = MftBuilder.Build(file, FontRaster.Faces(file)[0].Index, 40, "Caveat_40", "Caveat.ttf",
            new Dictionary<string, float> { ["wght"] = 600 }, cyrillic: false);

        Assert.Contains(all.Font.Glyphs, g => g.Code == 0x0416);
        Assert.DoesNotContain(latin.Font.Glyphs, g => g.Code is >= 0x0400 and <= 0x04FF);
        Assert.DoesNotContain(latin.Missing, c => c is >= 0x0400 and <= 0x04FF);
        Assert.Contains(latin.Font.Glyphs, g => g.Code == 0x00F6);
        Assert.True(latin.Font.CharacterCount < all.Font.CharacterCount);
    }

    /// <summary>The font's files, its font and family in the package, and its files in the package's
    /// preload list, each applying to the retail file as the build applies it.</summary>
    [Fact]
    public void A_staged_font_is_registered_where_the_game_looks()
    {
        if (Fixture.Read(Caveat) is not { } file || Fixture.Read(FontsPackage) is not { } package
            || Fixture.Read(UiList) is not { } list) return;

        SortedDictionary<string, byte[]> files = FontLayer.Files(Build(file), path =>
            path == $@"{Ui}\fonts.mgb" ? package : path == $@"{Ui}\_depload.xml" ? list : null);

        string desc = UiDepLoadContainerSplitter.IdOf($@"{Ui}\fonts.mgb.desc");
        Assert.Equivalent(
            new[] { @"ui\fonts\caveat_40.mft", @"ui\fonts\caveat_40_0.xbt", $@"{Ui}\fonts.mgb\_fonts.xml",
                $@"{Ui}\fonts.mgb\_fontfamilies.xml", $@"{Ui}\_depload.xml\{desc}" },
            files.Keys);

        MgbPackage registered = MgbPackage.Read(MgbContainerSplitter.Instance.Apply(package, Fragments(files, $@"{Ui}\fonts.mgb\")));
        Assert.Contains(registered.FontRefs, f => Encoding.Latin1.GetString(f.Name) == "Caveat_40");
        Assert.Equal(MgbPackage.Read(package).FontFamilies.Count + 1, registered.FontFamilies.Count);

        XElement preload = XElement.Parse(UiDepLoadContainerSplitter.Instance.Open(
            UiDepLoadContainerSplitter.Instance.Apply(list, Fragments(files, $@"{Ui}\_depload.xml\"))).Extract(desc)!);
        Assert.Contains(preload.Elements(), e => (string?)e.Attribute("crc_ID") == "3738007791");
    }

    private static BuiltFont Build(byte[] file)
        => MftBuilder.Build(file, FontRaster.Faces(file)[0].Index, 40, "Caveat_40", "Caveat.ttf", new Dictionary<string, float> { ["wght"] = 600 });

    private static Dictionary<string, string> Fragments(SortedDictionary<string, byte[]> files, string container)
        => files.Where(f => f.Key.StartsWith(container, StringComparison.Ordinal))
            .ToDictionary(f => f.Key[container.Length..], f => Encoding.UTF8.GetString(f.Value));
}
