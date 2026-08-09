using System.Xml.Linq;
using JackAll.Tools.Format.Mgb;

namespace JackAll.Core.Tests;

/// <summary>
/// The correctness gate for the <c>.mgb</c> XML interchange format.
/// </summary>
/// <remarks>
/// <c>Encode(Decode(x)) == x</c> is the whole contract: the XML is only useful as an editing
/// surface if building it back reproduces the file that produced it. Because both directions are
/// driven by the same <c>Serialize</c> descriptions as the binary codec, a field that reads and
/// writes correctly in binary but is unrepresentable in text shows up here and nowhere else -
/// float bit patterns, non-text string bytes, and null-versus-zero being the ones that bite.
///
/// Shares the corpus, and the skip-when-absent behaviour, with <see cref="MgbRoundTripTests"/>.
/// </remarks>
public sealed class MgbXmlTests
{
    private static readonly string CorpusDirectory =
        Path.Combine(TestSupport.RepositoryRoot, "tmp", "menu");

    public static TheoryData<string> CorpusFiles() => MgbRoundTripTests.CorpusFiles();

    private static byte[] Corpus(string fileName) =>
        File.ReadAllBytes(Path.Combine(CorpusDirectory, fileName));

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Round_trips_every_corpus_file_through_xml_byte_for_byte(string fileName)
    {
        if (fileName.Length == 0)
        {
            return;
        }

        byte[] original = Corpus(fileName);
        string xml = MgbXml.Decode(original);
        byte[] rebuilt = MgbXml.Encode(xml);

        Assert.Equal(original.Length, rebuilt.Length);
        Assert.True(original.AsSpan().SequenceEqual(rebuilt), $"{fileName} differs after an XML round trip");
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Exports_a_readable_document_rooted_at_MagmaPackage(string fileName)
    {
        if (fileName.Length == 0)
        {
            return;
        }

        XElement root = XDocument.Parse(MgbXml.Decode(Corpus(fileName))).Root!;

        Assert.Equal("MagmaPackage", root.Name.LocalName);
        Assert.NotNull(root.Element("TYPES"));
        Assert.NotNull(root.Element("CHILDREN"));

        // Counts are never stored: the areas the document declares are exactly its child elements.
        MgbPackage package = MgbPackage.Read(Corpus(fileName));
        Assert.Equal(package.Areas.Count, root.Element("CHILDREN")!.Elements("Area").Count());
        Assert.Equal(package.Types.RawIds.Count, root.Element("TYPES")!.Elements("TYPE").Count());
    }

    [Fact]
    public void Resolves_names_only_when_they_re_hash_to_the_stored_value()
    {
        // Every name the exporter writes must survive being hashed again - that verification is
        // what makes substituting a recovered name safe rather than a guess.
        XElement root = XDocument.Parse(MgbXml.Decode(Corpus("options.mgb"))).Root!;

        MgbPackage package = MgbPackage.Read(Corpus("options.mgb"));
        HashSet<uint> real = [.. package.Areas.Select(a => a.UserData.NameId)];

        int resolved = 0;
        foreach (XElement area in root.Element("CHILDREN")!.Elements("Area"))
        {
            string value = (string)area.Element("USERDATA")!.Attribute("name")!;
            if (value.StartsWith('#'))
            {
                Assert.Equal(9, value.Length);
                continue;
            }

            // A name is only ever written after re-hashing, so hashing it again has to land on a
            // hash the package really contains - never on a near-miss the lookup merely offered.
            Assert.Contains(MgbTypeTable.Hash(value), real);
            resolved++;
        }
        Assert.True(resolved > 0, "no area name in options.mgb resolved, so the lookup path is untested");
    }

    [Fact]
    public void Renders_known_enum_values_as_names_and_leaves_unknown_ones_as_numbers()
    {
        Assert.Equal("NOMASK", RoundTripEnum(0));
        Assert.Equal("USEMASK_INVERTED", RoundTripEnum(3));

        // MASKMODE keeps only the low 3 bits in the engine, so a stored word carrying more than
        // that is outside the table. It has to stay a number, or the high bits would be lost.
        Assert.Equal("9", RoundTripEnum(9));

        static string RoundTripEnum(uint value)
        {
            var element = new XElement("E");
            var codec = new MgbXmlWriteCodec(element);
            codec.EnumU32("MASKMODE", ref value, MgbEnums.MaskMode);
            return (string)element.Attribute("MASKMODE")!;
        }
    }

    [Theory]
    [InlineData(0x00000000u)] // +0
    [InlineData(0x3F800000u)] // 1
    [InlineData(0xBF800000u)] // -1
    [InlineData(0x80000000u)] // -0, which "0" would not reproduce
    [InlineData(0x00000001u)] // smallest denormal
    [InlineData(0x7F800000u)] // +Infinity
    [InlineData(0x7FC00001u)] // NaN with a payload, which decimal text cannot carry
    [InlineData(0xFFFFFFFFu)]
    public void Preserves_every_float_bit_pattern(uint bits)
    {
        var element = new XElement("E");
        uint written = bits;
        new MgbXmlWriteCodec(element).F32Bits("V", ref written);

        uint read = 0;
        new MgbXmlReadCodec(element).F32Bits("V", ref read);

        Assert.Equal(bits, read);
    }

    [Fact]
    public void Preserves_string_bytes_that_are_not_representable_as_xml_text()
    {
        // A control byte cannot live in an attribute (normalisation would rewrite it), so the
        // encoder has to fall back rather than silently mangle it.
        byte[] awkward = [0x41, 0x00, 0x09, 0xFF, 0x42];
        var element = new XElement("E");
        byte[] written = awkward;
        new MgbXmlWriteCodec(element).AnsiString("V", ref written);

        Assert.StartsWith("base64:", (string)element.Attribute("V")!);

        byte[] read = [];
        new MgbXmlReadCodec(element).AnsiString("V", ref read);
        Assert.Equal(awkward, read);
    }

    [Fact]
    public void Keeps_astral_text_readable_but_falls_back_for_a_lone_surrogate()
    {
        byte[] pair = MgbText.ToUtf16("ok \U0001F600");
        var readable = new XElement("E");
        byte[] written = pair;
        new MgbXmlWriteCodec(readable).Utf16String("V", ref written);
        Assert.DoesNotContain("base64:", (string)readable.Attribute("V")!);

        byte[] back = [];
        new MgbXmlReadCodec(readable).Utf16String("V", ref back);
        Assert.Equal(pair, back);

        // A high surrogate with nothing after it is not text at all - decoding replaces it, so the
        // re-encode check has to catch it even though the character screen lets surrogates past.
        byte[] lone = [0x3D, 0xD8];
        var escaped = new XElement("E");
        written = lone;
        new MgbXmlWriteCodec(escaped).Utf16String("V", ref written);
        Assert.StartsWith("base64:", (string)escaped.Attribute("V")!);

        new MgbXmlReadCodec(escaped).Utf16String("V", ref back);
        Assert.Equal(lone, back);
    }

    [Fact]
    public void Preserves_text_that_would_collide_with_the_base64_escape()
    {
        byte[] literal = MgbText.ToAnsi("base64:not actually encoded");
        var element = new XElement("E");
        byte[] written = literal;
        new MgbXmlWriteCodec(element).AnsiString("V", ref written);

        byte[] read = [];
        new MgbXmlReadCodec(element).AnsiString("V", ref read);
        Assert.Equal(literal, read);
    }

    [Fact]
    public void Distinguishes_an_absent_optional_from_one_holding_zero()
    {
        var absent = new XElement("E");
        uint? none = null;
        new MgbXmlWriteCodec(absent).OptionalU32("SLIDERLINK", ref none);
        Assert.Null(absent.Attribute("SLIDERLINK"));

        var zero = new XElement("E");
        uint? explicitZero = 0;
        new MgbXmlWriteCodec(zero).OptionalU32("SLIDERLINK", ref explicitZero);
        Assert.Equal("0", (string)zero.Attribute("SLIDERLINK")!);

        uint? readBack = 7;
        new MgbXmlReadCodec(absent).OptionalU32("SLIDERLINK", ref readBack);
        Assert.Null(readBack);

        new MgbXmlReadCodec(zero).OptionalU32("SLIDERLINK", ref readBack);
        Assert.Equal(0u, readBack);
    }

    [Fact]
    public void Names_the_missing_field_when_one_is_misspelled()
    {
        // The reader must survive its own unwinding: an earlier revision validated leftovers from
        // Dispose, so this reported a list-length mismatch while the real error was in flight.
        string xml = MgbXml.Decode(Corpus("options.mgb")).Replace("HIDDEN=", "HIDEN=");

        MgbFormatException error = Assert.Throws<MgbFormatException>(() => MgbXml.Encode(xml));
        Assert.Contains("HIDDEN", error.Message);
        Assert.Contains("missing", error.Message);
    }

    [Fact]
    public void Rejects_an_attribute_the_format_does_not_define()
    {
        string xml = MgbXml.Decode(Corpus("options.mgb"))
            .Replace("<TYPES>", "<TYPES stowaway=\"1\">");

        MgbFormatException error = Assert.Throws<MgbFormatException>(() => MgbXml.Encode(xml));
        Assert.Contains("stowaway", error.Message);
    }

    [Fact]
    public void Rejects_an_element_the_format_does_not_define()
    {
        string xml = MgbXml.Decode(Corpus("options.mgb"))
            .Replace("</MagmaPackage>", "<STOWAWAY /></MagmaPackage>");

        MgbFormatException error = Assert.Throws<MgbFormatException>(() => MgbXml.Encode(xml));
        Assert.Contains("STOWAWAY", error.Message);
    }

    [Fact]
    public void Rejects_a_document_that_is_not_a_package()
    {
        MgbFormatException error = Assert.Throws<MgbFormatException>(
            () => MgbXml.FromXml("<object type=\"EntityLibrary\" />"));
        Assert.Contains("MagmaPackage", error.Message);
    }

    /// <summary>
    /// FCSE's settings-page package is committed as both XML and binary. This is the one mgb test
    /// that needs no game corpus, so it runs on a fresh checkout - and it is the only thing keeping
    /// the two committed artifacts from drifting apart if someone edits one and forgets the other.
    /// </summary>
    [Theory]
    [InlineData("fcse", 1024, 768)]              // the `pc` UI set
    [InlineData("fcse_widescreen", 1280, 800)]   // the `pcwidescreen` set
    public void The_committed_fcse_page_package_builds_from_its_committed_xml(
        string stem, ushort pageWidth, ushort pageHeight)
    {
        string assets = Path.Combine(TestSupport.RepositoryRoot, "tools", "FCSE", "assets");
        string xmlPath = Path.Combine(assets, $"{stem}.mgb.xml");
        string mgbPath = Path.Combine(assets, $"{stem}.mgb");
        Assert.True(File.Exists(xmlPath), $"missing {xmlPath}");
        Assert.True(File.Exists(mgbPath), $"missing {mgbPath}");

        byte[] built = MgbXml.Encode(File.ReadAllText(xmlPath));
        Assert.True(built.AsSpan().SequenceEqual(File.ReadAllBytes(mgbPath)),
            $"{stem}.mgb is not what {stem}.mgb.xml builds - regenerate with `jackall mgb encode`");

        // The page is only reachable if CUIPageBase::Init can resolve it: it hashes the page name
        // and looks that up in the GenericObjectTable every loaded package registers.
        MgbPackage package = MgbPackage.Read(built);
        MgbArea page = Assert.Single(package.Areas);
        Assert.Equal("Page", page.TypeName);

        // Each variant must carry the geometry of the UI set it was built from, or the page will
        // be laid out for the wrong aspect - which is exactly the bug this pair exists to avoid.
        Assert.Equal(pageWidth, package.PageWidth);
        Assert.Equal(pageHeight, package.PageHeight);

        // Both chrome materials are declared locally. They cannot be reached cross-package, and a
        // material missing here renders as an untextured white quad over the whole page.
        Assert.Equal(2, package.Materials.Count);

        MgbGenericObjectTable table = Assert.IsType<MgbGenericObjectTable>(package.GenericObjectTable);
        MgbGenericObject entry = Assert.Single(
            table.Objects, o => o.NameId == MgbTypeTable.Hash("FCSE_PAGE"));
        Assert.Equal(
            [MgbTypeTable.Hash("fcse"), MgbTypeTable.Hash("FCSE_PAGE")],
            entry.Link.Ids);

        // Every SETTING_/FCSE_SLOT_ link must name an element that exists. A dangling one is a
        // perfectly valid package that renders a page with no controls on it.
        var elementNames = page.Elements.Select(e => e.UserData.NameId).ToHashSet();
        var linked = page.UserData.Properties.Where(p => p.Link is not null).ToList();
        Assert.Contains(linked, p => p.Key == MgbTypeTable.Hash("SETTING_LABEL_LIST"));
        foreach (MgbProperty property in linked)
        {
            Assert.Equal(MgbTypeTable.Hash("FCSE_PAGE"), property.Link!.Ids[1]);
            Assert.Contains(property.Link.Ids[2], elementNames);
        }

        // Two banks of the same size, one cell of each kind at every row position: FCSE_SLOT_nn is
        // the value spinner (a checkbox or an N-option dropdown) and FCSE_SLIDER_nn is the slider.
        // A row's type is not known until a plugin registers its settings, so both have to exist
        // everywhere and FCSE binds whichever the row turns out to need.
        //
        // The row count is capped by the nav list's own viewport (common.mgb 36150990 declares 20
        // visible rows); the controls do not scroll with the list, so more would misalign.
        static HashSet<uint> Bank(string prefix) =>
            Enumerable.Range(1, 20).Select(i => MgbTypeTable.Hash($"{prefix}{i:00}")).ToHashSet();

        HashSet<uint> valueBank = Bank("FCSE_SLOT_");
        HashSet<uint> sliderBank = Bank("FCSE_SLIDER_");

        Assert.Equal(20, linked.Count(p => valueBank.Contains(p.Key)));
        Assert.Equal(20, linked.Count(p => sliderBank.Contains(p.Key)));
        Assert.Equal(41, linked.Count); // the two banks plus SETTING_LABEL_LIST, and nothing else

        // Both banks must be authored visible. Authoring the slider bank hidden and revealing only
        // the bound cells was tried and does not work: HIDDEN and Element::SetVisible are different
        // bits of the element's flag byte (bit 1 and bit 0), and magma's draw collection skips
        // anything with bit 1 set - so a "shown" cell was still not drawn, and the engine
        // dereferenced a null the frame after. There is no data-only way to author a cell that code
        // can reveal later, which is what this assert is really pinning down.
        var cellElements = linked.Where(p => sliderBank.Contains(p.Key) || valueBank.Contains(p.Key))
                                 .Select(p => p.Link!.Ids[2])
                                 .ToHashSet();
        foreach (MgbElement element in page.Elements.Where(e => cellElements.Contains(e.UserData.NameId)))
        {
            Assert.False(element.Hidden, "a control cell is authored hidden");
        }
    }

    [Fact]
    public void An_edit_made_in_xml_survives_the_rebuild()
    {
        byte[] original = Corpus("options.mgb");
        MgbPackage before = MgbPackage.Read(original);

        string xml = MgbXml.Decode(original);
        XDocument document = XDocument.Parse(xml);
        XElement page = document.Root!;
        page.SetAttributeValue("PAGESIZE.w", (before.PageWidth + 3).ToString());

        MgbPackage after = MgbPackage.Read(MgbXml.Encode(document.ToString()));

        Assert.Equal(before.PageWidth + 3, after.PageWidth);
        Assert.Equal(before.Areas.Count, after.Areas.Count);
        Assert.Equal(before.PageHeight, after.PageHeight);
    }
}
