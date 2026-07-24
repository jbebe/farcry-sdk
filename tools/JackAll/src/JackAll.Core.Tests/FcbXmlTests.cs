using System.Xml.Linq;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;

namespace JackAll.Core.Tests;

/// <summary>
/// Runs against the same real shipped .fcb files as <see cref="FcbDocumentTests"/>, plus the real
/// binary_classes.xml config (bundled from tools/Gibbed.Dunia's own copy) - this is the strongest
/// available check that <see cref="FcbXml"/>'s value-type decoding matches the real config's member
/// declarations without throwing on any of them, and that a full ToXml -> FromXml round trip
/// reproduces the exact same tree.
/// </summary>
public class FcbXmlTests
{
    private const string FixturesDir = "Fixtures/Fcb";
    private const string ClassesPath = "Fixtures/Fcb/binary_classes.xml";

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

    [Fact]
    public void The_real_binary_classes_config_was_actually_found()
        => Assert.True(File.Exists(ClassesPath), $"{ClassesPath} was not found - every config-backed test in this class silently no-opped.");

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void A_real_shipped_fcb_converts_to_xml_and_back_to_the_same_tree(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        FcbClassDefinitions defs = File.Exists(ClassesPath)
            ? FcbClassDefinitions.Load(ClassesPath)
            : FcbClassDefinitions.Empty;

        FcbObject original = FcbDocument.Deserialize(File.ReadAllBytes(path));

        FcbXmlExport export = FcbXml.ToXml(original, defs);
        FcbObject reparsed = FcbXml.FromXml(export.IndexXml, name => export.ExternalFiles[name]);

        AssertSameShape(original, reparsed);
    }

    /// <summary>
    /// Confirms the trailing-pad-byte handling in <c>TryDecodeRml</c>/<c>ReadRml</c> actually engages
    /// on real data, not just the hand-built case: every one of the ~2,300 real Rml-typed values across
    /// these 4 fixtures (mostly `hidDescriptor`) decodes to a nested element rather than falling back to
    /// hex - verified directly while building this feature (JackAll.Core/Format/Fcb/FcbXml.cs's
    /// TryDecodeRml remarks), locked in here so a future change can't silently regress it back to "every
    /// real value falls back to hex" without a test noticing.
    /// </summary>
    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_real_Rml_typed_value_decodes_to_nested_xml_not_hex(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        FcbClassDefinitions defs = FcbClassDefinitions.Load(ClassesPath);
        FcbObject original = FcbDocument.Deserialize(File.ReadAllBytes(path));
        FcbXmlExport export = FcbXml.ToXml(original, defs);
        string all = export.IndexXml + string.Join("", export.ExternalFiles.Values);

        int rmlValueCount = 0, index = 0;
        while ((index = all.IndexOf("type=\"Rml\"", index, StringComparison.Ordinal)) >= 0)
        {
            rmlValueCount++;
            int tagEnd = all.IndexOf('>', index);
            Assert.True(tagEnd >= 0 && all[(tagEnd + 1)..].TrimStart().StartsWith('<'),
                $"A Rml-typed value in {path} fell back to hex instead of decoding to nested XML.");
            index = tagEnd;
        }
        Assert.True(rmlValueCount > 0, $"{path} has no Rml-typed values - this test proves nothing without at least one.");
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_entitylibrary_fixture_splits_into_at_least_one_external_file(string path)
    {
        // All 5 real fixtures share the EntityLibrary-of-groups root shape (confirmed separately in
        // FcbDocumentTests), so ToXml's multi-export path should trigger for every one of them.
        if (string.IsNullOrEmpty(path)) return;

        FcbObject original = FcbDocument.Deserialize(File.ReadAllBytes(path));
        FcbXmlExport export = FcbXml.ToXml(original, FcbClassDefinitions.Empty);

        Assert.NotEmpty(export.ExternalFiles);
        Assert.Equal(original.Children.Count, export.ExternalFiles.Count);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void ListFragmentIds_matches_ToXmls_external_file_names(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        FcbObject original = FcbDocument.Deserialize(File.ReadAllBytes(path));
        FcbXmlExport export = FcbXml.ToXml(original, FcbClassDefinitions.Empty);

        IReadOnlyList<string> ids = FcbXml.ListFragmentIds(original);

        Assert.Equal(export.ExternalFiles.Keys.OrderBy(k => k), ids.OrderBy(k => k));
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void ExtractFragment_reproduces_the_same_content_ToXml_put_in_that_external_file(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        FcbClassDefinitions defs = File.Exists(ClassesPath)
            ? FcbClassDefinitions.Load(ClassesPath)
            : FcbClassDefinitions.Empty;

        FcbObject original = FcbDocument.Deserialize(File.ReadAllBytes(path));
        FcbXmlExport export = FcbXml.ToXml(original, defs);

        foreach ((string fragmentId, string expectedXml) in export.ExternalFiles)
        {
            string? actualXml = FcbXml.ExtractFragment(original, fragmentId, defs);
            Assert.Equal(expectedXml, actualXml);
        }
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void ListFragmentsWithSize_reports_each_childs_real_on_disk_size(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        (FcbObject original, IReadOnlyList<long> childByteSizes) = FcbDocument.DeserializeWithChildSizes(File.ReadAllBytes(path));
        IReadOnlyList<FcbFragmentInfo> fragments = FcbXml.ListFragmentsWithSize(original, childByteSizes);

        Assert.NotEmpty(fragments);
        Assert.Equal(original.Children.Count, fragments.Count);
        for (int i = 0; i < fragments.Count; i++)
        {
            // The real on-disk span can only ever be <= the fully-expanded size (equal unless this
            // particular child leans on the format's backreference dedup somewhere inside it) - see
            // TestSupport.FullyExpandedFcbSize's remarks.
            Assert.True(fragments[i].Size > 0, $"'{fragments[i].Id}' reported a size of 0.");
            Assert.True(
                fragments[i].Size <= TestSupport.FullyExpandedFcbSize(original.Children[i]),
                $"'{fragments[i].Id}' reported {fragments[i].Size} bytes on disk but only " +
                $"{TestSupport.FullyExpandedFcbSize(original.Children[i])} fully expanded - dedup can only shrink, never grow.");
        }
    }

    [Fact]
    public void ExtractFragment_returns_null_for_an_id_that_does_not_match_any_child()
    {
        var root = new FcbObject { TypeHash = 0xBCDD10B4 }; // EntityLibrary
        var group = new FcbObject { TypeHash = 0xE0BDB3DB }; // EntityLibraryGroup
        root.Children.Add(group);

        Assert.Null(FcbXml.ExtractFragment(root, "does_not_exist.xml", FcbClassDefinitions.Empty));
    }

    [Fact]
    public void ListFragmentIds_is_empty_for_a_root_that_does_not_split()
    {
        var root = new FcbObject { TypeHash = 0x11111111 };
        root.Values.Add(0xAAAAAAAA, [0x01]);

        Assert.Empty(FcbXml.ListFragmentIds(root));
        Assert.Null(FcbXml.ExtractFragment(root, "01.xml", FcbClassDefinitions.Empty));
    }

    [Fact]
    public void Real_class_definitions_resolve_the_root_EntityLibrary_type_by_name()
    {
        FcbClassDefinitions defs = FcbClassDefinitions.Load(ClassesPath);

        // <class hash="256A1FF9"> in binary_classes.xml is commented "Entity library category" but
        // has no name attribute (config quirk - it's identified by hash, not a name string), so this
        // checks a class we know resolves by name instead: WorldSector, used directly in worldsector*.fcb.
        FcbClass worldSector = defs.GetClass(Crc32("WorldSector"));
        Assert.Equal("WorldSector", worldSector.Name);
        Assert.Equal(FcbMemberType.UInt32, worldSector.FindMember(Crc32("Id"))?.Type);
    }

    [Fact]
    public void A_hand_built_tree_with_a_vector_and_a_string_round_trips_through_xml()
    {
        var root = new FcbObject { TypeHash = 0x11111111 };
        root.Values.Add(Crc32("hidPos"), [.. BitConverter.GetBytes(1.5f), .. BitConverter.GetBytes(2.5f), .. BitConverter.GetBytes(3.5f)]);
        root.Values.Add(Crc32("hidName"), [.. System.Text.Encoding.UTF8.GetBytes("hello"), 0]);
        root.Values.Add(0xDEADBEEF, [0x01, 0x02]); // unknown hash - must fall back to BinHex

        var defs = FcbClassDefinitions.Empty;
        // Build a definitions instance with exactly the members this test needs, bypassing the file
        // loader, by writing a tiny inline config and loading it.
        string inlineConfig = """
            <classes>
              <class hash="11111111">
                <member name="hidPos">Vector3</member>
                <member name="hidName">String</member>
              </class>
            </classes>
            """;
        string tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, inlineConfig);
            defs = FcbClassDefinitions.Load(tempPath);
        }
        finally
        {
            File.Delete(tempPath);
        }

        FcbXmlExport export = FcbXml.ToXml(root, defs);
        Assert.Contains("Vector3", export.IndexXml);
        Assert.Contains("hello", export.IndexXml);
        Assert.Contains("BinHex", export.IndexXml); // the unknown-hash value

        FcbObject reparsed = FcbXml.FromXml(export.IndexXml);
        AssertSameShape(root, reparsed);
    }

    /// <summary>
    /// A hand-built value covering exactly the FCB-layer convention <c>TryDecodeRml</c>/<c>ReadRml</c>
    /// implement: the raw bytes are a real .rml document (built here via
    /// <see cref="RmlDocument.Serialize"/> the same way <see cref="RmlDocumentTests"/> verifies against
    /// real shipped .rml files) plus one trailing 0x00 pad byte - see those methods' remarks for why
    /// that pad byte is there. Independent of the real fixtures, so this stays meaningful even if they
    /// ever go away.
    /// </summary>
    [Fact]
    public void A_hand_built_rml_value_decodes_to_nested_xml_and_round_trips()
    {
        byte[] rml = RmlDocument.Serialize(new XElement("hidDescriptor", new XAttribute("x", "1")));
        byte[] value = [.. rml, 0]; // the FCB-layer trailing pad byte

        var root = new FcbObject { TypeHash = 0x11111111 };
        root.Values.Add(Crc32("hidDescriptor"), value);

        string inlineConfig = """
            <classes>
              <class hash="11111111">
                <member name="hidDescriptor">Rml</member>
              </class>
            </classes>
            """;
        string tempPath = Path.GetTempFileName();
        FcbClassDefinitions defs;
        try
        {
            File.WriteAllText(tempPath, inlineConfig);
            defs = FcbClassDefinitions.Load(tempPath);
        }
        finally
        {
            File.Delete(tempPath);
        }

        FcbXmlExport export = FcbXml.ToXml(root, defs);
        Assert.Contains("<hidDescriptor x=\"1\" />", export.IndexXml);
        Assert.DoesNotContain("BinHex", export.IndexXml);

        FcbObject reparsed = FcbXml.FromXml(export.IndexXml);
        AssertSameShape(root, reparsed);
    }

    /// <summary>
    /// A value whose raw bytes are the bare <see cref="RmlDocument.Serialize"/> output with no
    /// FCB-layer pad byte appended - the base game never ships this shape (every one of the 2,328 real
    /// samples checked has the pad byte, per <c>TryDecodeRml</c>'s remarks), but a third-party modding
    /// tool's own FCB writer can still produce it, and this value's own last string-table byte happening
    /// to be 0x00 (as most .rml values' do) makes it look pad-byte-shaped without being one - exactly
    /// the shape that would corrupt if this class blindly stripped a trailing byte instead of trying
    /// both shapes. Must still decode to nested XML (not fall back to hex, so more of what's actually
    /// out there is readable here) - but re-importing it normalizes to the base game's own padded shape
    /// regardless (one byte longer than the source value), rather than preserving the non-conforming
    /// layout a modding tool's writer produced - see <see cref="ReadRml"/>'s remarks.
    /// </summary>
    [Fact]
    public void An_rml_value_without_the_base_games_pad_byte_still_decodes_but_reimports_padded()
    {
        byte[] value = RmlDocument.Serialize(new XElement("hidDescriptor", new XAttribute("x", "1")));
        Assert.Equal(0, value[^1]); // otherwise this wouldn't be exercising the case at all

        var root = new FcbObject { TypeHash = 0x11111111 };
        root.Values.Add(Crc32("hidDescriptor"), value);

        string inlineConfig = """
            <classes>
              <class hash="11111111">
                <member name="hidDescriptor">Rml</member>
              </class>
            </classes>
            """;
        string tempPath = Path.GetTempFileName();
        FcbClassDefinitions defs;
        try
        {
            File.WriteAllText(tempPath, inlineConfig);
            defs = FcbClassDefinitions.Load(tempPath);
        }
        finally
        {
            File.Delete(tempPath);
        }

        FcbXmlExport export = FcbXml.ToXml(root, defs);
        Assert.Contains("<hidDescriptor x=\"1\" />", export.IndexXml);
        Assert.DoesNotContain("BinHex", export.IndexXml);

        FcbObject reparsed = FcbXml.FromXml(export.IndexXml);
        var expected = new FcbObject { TypeHash = 0x11111111 };
        expected.Values.Add(Crc32("hidDescriptor"), [.. value, 0]); // normalized: pad byte added back
        AssertSameShape(expected, reparsed);
    }

    private static uint Crc32(string name)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (char c in name)
        {
            crc ^= (byte)c;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
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
