using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Rml;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// `oasisstrings.rml` as a splitting container: one fragment per string, expanded out of the one
/// patch document a mod authors. The gate is the one every other splitter has - every shipped table
/// taken apart and put back together unchanged.
/// </summary>
public class StringTableContainerSplitterTests : IDisposable
{
    private const string Container = @"languages\english\oasisstrings.rml";
    private const string PatchPath = @"languages\english\oasisstrings.fragment.xml";

    private readonly string _sandbox;
    private readonly StringTableContainerSplitter _splitter = new();

    public StringTableContainerSplitterTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Every `oasisstrings.rml` the local game export has, both archives.</summary>
    public static TheoryData<string> CorpusFiles()
    {
        var data = new TheoryData<string>();
        string[] files = [.. Fc2Corpus.Find(OasisStringsPatch.TableFileName)];
        if (files.Length == 0)
        {
            data.Add(string.Empty);
            return data;
        }
        foreach (string file in files)
        {
            data.Add(file);
        }
        return data;
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_holds_string_tables_to_gate_against()
        => Assert.True(
            Fc2Corpus.Find(OasisStringsPatch.TableFileName).Any(),
            Fc2Corpus.MissingMessage(OasisStringsPatch.TableFileName));

    [Theory]
    [InlineData("oasisstrings.rml", true)]
    [InlineData("OASISSTRINGS.RML", true)]
    // The plain-text twin is a stale pre-patch leftover the engine never loads, and the particle
    // libraries are a different container with a different key.
    [InlineData("oasisstrings.xml", false)]
    [InlineData("world1_deploadnewparticles.rml", false)]
    [InlineData("toc.rml", false)]
    public void Only_the_string_table_splits_not_every_rml(string fileName, bool expected)
        => Assert.Equal(expected, ContainerFormats.IsContainerSegment(fileName));

    /// <summary>The bar: rewrite every string with the value it already has, and the table is the
    /// bytes it came from.</summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Every_shipped_table_re_encodes_byte_for_byte(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        Dictionary<string, string> everything = StringTableContainerSplitter
            .Strings(RmlDocument.Deserialize(original))
            .ToDictionary(StringTableContainerSplitter.IdOf, OasisStringsPatch.FragmentToXml);

        Assert.NotEmpty(everything);
        byte[] rebuilt = _splitter.Apply(original, everything);

        int at = Fc2Corpus.FirstDifference(original, rebuilt);
        Assert.True(at < 0, Fc2Corpus.DescribeDifference(path, original, rebuilt));
    }

    /// <summary>A string is addressed by (section, enum), so no two may collide within one table.</summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Every_string_in_a_table_has_its_own_id(string path)
    {
        if (path.Length == 0) return;

        string[] ids = [.. StringTableContainerSplitter
            .Strings(RmlDocument.Deserialize(File.ReadAllBytes(path)))
            .Select(StringTableContainerSplitter.IdOf)];

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>Every extracted string round-trips through the fragment form unchanged - including
    /// the 200 that carry a newline, which is why a value is an attribute and not element text.</summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Every_shipped_string_survives_the_fragment_form(string path)
    {
        if (path.Length == 0) return;

        int withNewlines = 0;
        foreach (OasisStringEdit edit in StringTableContainerSplitter.Strings(RmlDocument.Deserialize(File.ReadAllBytes(path))))
        {
            if (edit.Value.Contains('\n')) withNewlines++;
            Assert.Equal(edit, OasisStringsPatch.FragmentFromXml(OasisStringsPatch.FragmentToXml(edit)));
        }

        Assert.True(withNewlines > 0, $"{Path.GetFileName(path)} carries no multi-line value to gate against.");
    }

    /// <summary>The table is addressable, not browsable: 11,394 rows per language is not a file tree,
    /// so the rows a person sees are the ones a mod overrides.</summary>
    [Fact]
    public void The_table_lists_no_rows_of_its_own()
        => Assert.Empty(_splitter.Open(Table(("Generic", "ACCEPT", "Accept"))).List());

    [Fact]
    public void A_table_nobody_staged_against_is_not_even_decoded()
    {
        byte[] container = Table(("Generic", "ACCEPT", "Accept"));

        Assert.Same(container, _splitter.Apply(container, new Dictionary<string, string>()));
    }

    /// <summary>The number binds, so every spelling of one string is the same fragment.</summary>
    [Fact]
    public void Every_spelling_of_a_string_names_the_same_fragment()
    {
        byte[] container = Table(("Tutorial", "HINT", "Press F"));
        IContainerTree tree = _splitter.Open(container);

        string id = StringTableContainerSplitter.IdOf("Tutorial", "HINT");
        uint number = FragmentId.NumberOf(id)!.Value;
        string expected = tree.Extract(id)!;

        Assert.Equal(expected, tree.Extract($"{number}.xml"));
        Assert.Equal(expected, tree.Extract($"anything_at_all.{number}.xml"));
        Assert.Null(tree.Extract(StringTableContainerSplitter.IdOf("Tutorial", "NOPE")));
    }

    [Fact]
    public void A_fragment_filed_under_the_wrong_id_is_refused()
    {
        byte[] container = Table(("Generic", "ACCEPT", "Accept"));
        string elsewhere = OasisStringsPatch.FragmentToXml(new OasisStringEdit("Tutorial", "HINT", "Press F"));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => _splitter.Apply(
            container,
            new Dictionary<string, string> { [StringTableContainerSplitter.IdOf("Generic", "ACCEPT")] = elsewhere }));

        Assert.Contains("Tutorial;HINT", error.Message);
        Assert.Contains(StringTableContainerSplitter.IdOf("Tutorial", "HINT"), error.Message);
    }

    [Fact]
    public void A_fragment_that_names_no_string_says_so()
    {
        byte[] container = Table(("Generic", "ACCEPT", "Accept"));
        string id = StringTableContainerSplitter.IdOf("Generic", "ACCEPT");

        Assert.Throws<InvalidDataException>(() => _splitter.Apply(
            container, new Dictionary<string, string> { [id] = "<string enum=\"ACCEPT\" value=\"x\" />" }));
        Assert.ThrowsAny<Exception>(() => _splitter.Apply(
            container, new Dictionary<string, string> { [id] = "not xml at all" }));
    }

    /// <summary>An edited string keeps its place; one a mod introduces is appended to its section,
    /// and a section a mod introduces is appended to the table.</summary>
    [Fact]
    public void An_added_string_lands_in_the_section_that_should_hold_it()
    {
        byte[] container = Table(("Generic", "ACCEPT", "Accept"), ("Tutorial", "HINT", "Press F"));

        byte[] built = _splitter.Apply(container, Staged(
            new OasisStringEdit("Generic", "ACCEPT", "Yes"),
            new OasisStringEdit("Generic", "CANCEL", "No"),
            new OasisStringEdit("Bonus", "NEW", "Brand new")));

        Assert.Equal(["Generic", "Tutorial", "Bonus"], SectionNames(built));
        Assert.Equal("Yes", ValueOf(built, "Generic", "ACCEPT"));
        Assert.Equal("No", ValueOf(built, "Generic", "CANCEL"));
        Assert.Equal("Brand new", ValueOf(built, "Bonus", "NEW"));
    }

    [Fact]
    public void Canonicalizing_normalises_formatting_before_a_merge_sees_it()
    {
        string ugly = "<string  section='Generic'   enum='ACCEPT'\n  value='Accept'></string>";

        Assert.Equal(
            OasisStringsPatch.FragmentToXml(new OasisStringEdit("Generic", "ACCEPT", "Accept")),
            _splitter.Canonicalize(StringTableContainerSplitter.IdOf("Generic", "ACCEPT"), ugly));
    }

    /// <summary>Two mods renaming different weapons: different strings, so they never meet.</summary>
    [Fact]
    public void Two_mods_editing_different_strings_both_survive()
    {
        byte[] container = Table(
            ("Items", "dart_rifle", "Dart Rifle"), ("Items", "m79", "M79"), ("Challenges", "mac10", "MAC-10"));

        var conflicts = new ConcurrentQueue<FragmentConflict>();
        byte[] built = _splitter.Apply(container, Resolve(container, conflicts,
            MakeLayer("mod_a", new OasisStringEdit("Items", "dart_rifle", "VSS Vintorez")),
            MakeLayer("mod_b",
                new OasisStringEdit("Items", "m79", "Grenade Launcher"),
                new OasisStringEdit("Challenges", "mac10", "Skorpion"))));

        Assert.Empty(conflicts);
        Assert.Equal("VSS Vintorez", ValueOf(built, "Items", "dart_rifle"));
        Assert.Equal("Grenade Launcher", ValueOf(built, "Items", "m79"));
        Assert.Equal("Skorpion", ValueOf(built, "Challenges", "mac10"));
    }

    /// <summary>Two mods each adding a new string to one section compose too - the case a
    /// section-sized fragment could not express, because both would insert at the same line.</summary>
    [Fact]
    public void Two_mods_adding_different_strings_to_one_section_both_survive()
    {
        byte[] container = Table(("Items", "dart_rifle", "Dart Rifle"));

        var conflicts = new ConcurrentQueue<FragmentConflict>();
        byte[] built = _splitter.Apply(container, Resolve(container, conflicts,
            MakeLayer("mod_a", new OasisStringEdit("Items", "vss", "VSS Vintorez")),
            MakeLayer("mod_b", new OasisStringEdit("Items", "sks", "SKS"))));

        Assert.Empty(conflicts);
        Assert.Equal("VSS Vintorez", ValueOf(built, "Items", "vss"));
        Assert.Equal("SKS", ValueOf(built, "Items", "sks"));
    }

    /// <summary>The same string twice is a real authoring decision, so it is reported rather than
    /// resolved in silence - which is exactly what a whole-file override could not do.</summary>
    [Fact]
    public void Two_mods_renaming_one_string_collide_loudly()
    {
        byte[] container = Table(("Items", "dart_rifle", "Dart Rifle"));
        FolderModLayer modA = MakeLayer("mod_a", new OasisStringEdit("Items", "dart_rifle", "VSS Vintorez"));
        FolderModLayer modB = MakeLayer("mod_b", new OasisStringEdit("Items", "dart_rifle", "Dragunov"));

        Assert.Throws<InvalidDataException>(() => Resolve(container, null, modA, modB));

        var conflicts = new ConcurrentQueue<FragmentConflict>();
        byte[] built = _splitter.Apply(container, Resolve(container, conflicts, modA, modB));

        FragmentConflict reported = Assert.Single(conflicts);
        Assert.Equal("mod_b", reported.WinningLayer);
        Assert.Equal("Dragunov", ValueOf(built, "Items", "dart_rifle"));
    }

    /// <summary>One authored document becomes one override per string it states.</summary>
    [Fact]
    public void A_patch_document_expands_into_one_fragment_per_string()
    {
        FolderModLayer layer = MakeLayer("mod",
            new OasisStringEdit("Items", "dart_rifle", "VSS Vintorez"),
            new OasisStringEdit("Tutorial", "HINT", "Press F"));

        KeyValuePair<uint, IReadOnlyList<FragmentOverride>> staged = Assert.Single(layer.FragmentOverrides);
        Assert.Equal(NameHash.Compute(Container), staged.Key);
        Assert.Equal(2, staged.Value.Count);

        FragmentOverride first = staged.Value.Single(f =>
            FcbFragments.IdComparer.Equals(f.FragmentId, StringTableContainerSplitter.IdOf("Items", "dart_rifle")));
        Assert.Equal(
            OasisStringsPatch.FragmentToXml(new OasisStringEdit("Items", "dart_rifle", "VSS Vintorez")),
            Encoding.UTF8.GetString(layer.Read(first.EntryHash)));
    }

    /// <summary>The old shape is refused outright: it is last-wins against every other localization
    /// mod, which is the failure the split exists to remove.</summary>
    [Fact]
    public void A_whole_file_table_override_is_refused()
    {
        string root = Path.Combine(_sandbox, "legacy");
        string dir = Path.Combine(root, "mods", @"languages\english");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(
            Path.Combine(dir, OasisStringsPatch.TableFileName), Table(("Items", "dart_rifle", "VSS Vintorez")));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => new FolderModLayer(root, "legacy"));

        Assert.Contains("one string at a time", error.Message);
        Assert.Contains(OasisStringsPatch.FileName, error.Message);
    }

    private Dictionary<string, string> Resolve(
        byte[] container, ConcurrentQueue<FragmentConflict>? conflicts, params FolderModLayer[] layers)
        => TestSupport.ResolveFragments(_splitter, container, Container, conflicts, layers);

    /// <summary>A layer shipping one patch document, the way a mod actually does.</summary>
    private FolderModLayer MakeLayer(string name, params OasisStringEdit[] edits)
    {
        string dir = Path.Combine(_sandbox, name, "mods", Path.GetDirectoryName(PatchPath)!);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, OasisStringsPatch.FileName), OasisStringsPatch.Render(edits));
        return new FolderModLayer(Path.Combine(_sandbox, name), name);
    }

    private static Dictionary<string, string> Staged(params OasisStringEdit[] edits)
        => edits.ToDictionary(StringTableContainerSplitter.IdOf, OasisStringsPatch.FragmentToXml);

    /// <summary>A table in the shape the game ships: <c>&lt;stringtable&gt;</c> of sections of
    /// strings, one row per string, grouped into sections in the order they first appear.</summary>
    private static byte[] Table(params (string Section, string Key, string Value)[] rows)
        => RmlDocument.Serialize(new XElement("stringtable",
            new XAttribute("language", "english"),
            rows.GroupBy(r => r.Section, StringComparer.Ordinal)
                .Select(g => new XElement("section",
                    new XAttribute("name", g.Key),
                    g.Select(r => new XElement("string",
                        new XAttribute("enum", r.Key), new XAttribute("value", r.Value)))))));

    private static IEnumerable<string> SectionNames(byte[] table)
        => RmlDocument.Deserialize(table).Elements("section").Select(s => (string)s.Attribute("name")!);

    private static string? ValueOf(byte[] table, string section, string key)
        => StringTableContainerSplitter.Strings(RmlDocument.Deserialize(table))
            .Single(e => e.Section == section && e.Key == key).Value;
}
