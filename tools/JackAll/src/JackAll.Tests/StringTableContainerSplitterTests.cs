using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// `oasisstrings.rml` as a splitting container: one fragment per <c>&lt;section&gt;</c>, so a mod
/// renaming a weapon stages 1.7 KB instead of overriding a 946 KB table. The gate is the one every
/// other splitter has - every shipped table taken apart and put back together unchanged.
/// </summary>
public class StringTableContainerSplitterTests : IDisposable
{
    private const string Container = @"languages\english\oasisstrings.rml";

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
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "StringTable");
        string[] files = Directory.Exists(root)
            ? Directory.GetFiles(root, "*.rml", SearchOption.AllDirectories)
            : [];

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

    /// <summary>The bar: extract every section, apply them all back, and the table is the bytes it
    /// came from.</summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Every_shipped_table_re_encodes_byte_for_byte(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);

        Dictionary<string, string> everything = tree.List()
            .ToDictionary(f => f.Id, f => tree.Extract(f.Id)!);

        Assert.NotEmpty(everything);
        Assert.DoesNotContain(everything.Values, x => x is null);
        Assert.Equal(original, _splitter.Apply(original, everything));
    }

    /// <summary>A section name is the id, so two sections may never hash alike within one table.</summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Every_section_in_a_table_has_its_own_id(string path)
    {
        if (path.Length == 0) return;

        IReadOnlyList<string> ids = [.. _splitter.Open(File.ReadAllBytes(path)).List().Select(f => f.Id)];

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>Staging nothing decodes nothing: an untouched table is passed straight through.</summary>
    [Fact]
    public void A_table_nobody_staged_against_is_not_even_decoded()
    {
        byte[] container = Table(("Generic", "ACCEPT", "Accept"));

        Assert.Same(container, _splitter.Apply(container, new Dictionary<string, string>()));
    }

    /// <summary>The number binds, so every spelling of one section is the same fragment.</summary>
    [Fact]
    public void Every_spelling_of_a_section_names_the_same_fragment()
    {
        byte[] container = Table(("Tutorial", "HINT", "Press F"));
        IContainerTree tree = _splitter.Open(container);

        uint hash = NameHash.Compute("Tutorial");
        string expected = tree.Extract(StringTableContainerSplitter.IdOf("Tutorial"))!;

        Assert.Equal(expected, tree.Extract($"{hash}.xml"));
        Assert.Equal(expected, tree.Extract("Tutorial.xml"));
        Assert.Equal(expected, tree.Extract($"anything_at_all.{hash}.xml"));
        Assert.Null(tree.Extract("NotASection.xml"));
    }

    [Fact]
    public void A_fragment_filed_under_the_wrong_id_is_refused()
    {
        byte[] container = Table(("Generic", "ACCEPT", "Accept"));
        string tutorial = Section("Tutorial", ("HINT", "Press F"));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => _splitter.Apply(
            container, new Dictionary<string, string> { [StringTableContainerSplitter.IdOf("Generic")] = tutorial }));

        Assert.Contains("Tutorial", error.Message);
        Assert.Contains(StringTableContainerSplitter.IdOf("Tutorial"), error.Message);
    }

    [Fact]
    public void A_fragment_that_is_not_a_section_names_the_problem()
    {
        byte[] container = Table(("Generic", "ACCEPT", "Accept"));

        Assert.Throws<InvalidDataException>(() => _splitter.Apply(
            container, new Dictionary<string, string> { ["Generic.xml"] = "<stringtable />" }));
        Assert.ThrowsAny<Exception>(() => _splitter.Apply(
            container, new Dictionary<string, string> { ["Generic.xml"] = "not xml at all" }));
    }

    /// <summary>An overridden section keeps its place; a new one is appended.</summary>
    [Fact]
    public void An_added_section_lands_after_the_ones_that_were_there()
    {
        byte[] container = Table(("Generic", "ACCEPT", "Accept"), ("Tutorial", "HINT", "Press F"));

        byte[] built = _splitter.Apply(container, new Dictionary<string, string>
        {
            [StringTableContainerSplitter.IdOf("Zulu")] = Section("Zulu", ("Z", "z")),
            [StringTableContainerSplitter.IdOf("Alpha")] = Section("Alpha", ("A", "a")),
            [StringTableContainerSplitter.IdOf("Generic")] = Section("Generic", ("ACCEPT", "Yes")),
        });

        Assert.Equal(
            ["Generic", "Tutorial", "Alpha", "Zulu"],
            RmlDocument.Deserialize(built).Elements("section").Select(s => (string)s.Attribute("name")!));
        Assert.Equal("Yes", ValueOf(built, "Generic", "ACCEPT"));
    }

    [Fact]
    public void Canonicalizing_normalises_formatting_before_a_merge_sees_it()
    {
        string ugly = "<section name='Generic'><string enum='ACCEPT' value='Accept'></string></section>";

        Assert.Equal(Section("Generic", ("ACCEPT", "Accept")), _splitter.Canonicalize(ugly));
    }

    /// <summary>Two mods renaming different weapons: disjoint sections, so they simply compose.</summary>
    [Fact]
    public void Two_mods_editing_different_sections_both_survive()
    {
        byte[] container = Table(("Items", "dart_rifle", "Dart Rifle"), ("Challenges", "m79", "M79"));

        byte[] built = _splitter.Apply(container, Resolve(container,
            MakeLayer("mod_a", Section("Items", ("dart_rifle", "VSS Vintorez"))),
            MakeLayer("mod_b", Section("Challenges", ("m79", "Grenade Launcher")))));

        Assert.Equal("VSS Vintorez", ValueOf(built, "Items", "dart_rifle"));
        Assert.Equal("Grenade Launcher", ValueOf(built, "Challenges", "m79"));
    }

    /// <summary>The case the split exists for: one section, two mods, different strings in it. A
    /// fragment is rendered one string per line, so Diff3 merges them instead of picking a winner.</summary>
    [Fact]
    public void Two_mods_editing_different_strings_in_one_section_are_merged()
    {
        byte[] container = Table(("Items", "dart_rifle", "Dart Rifle"), ("Items", "m79", "M79"), ("Items", "mac10", "MAC-10"));

        var conflicts = new ConcurrentQueue<FragmentConflict>();
        byte[] built = _splitter.Apply(container, Resolve(container, conflicts,
            MakeLayer("mod_a", Section("Items",
                ("dart_rifle", "VSS Vintorez"), ("m79", "M79"), ("mac10", "MAC-10"))),
            MakeLayer("mod_b", Section("Items",
                ("dart_rifle", "Dart Rifle"), ("m79", "M79"), ("mac10", "Skorpion")))));

        Assert.Empty(conflicts);
        Assert.Equal("VSS Vintorez", ValueOf(built, "Items", "dart_rifle"));
        Assert.Equal("Skorpion", ValueOf(built, "Items", "mac10"));
    }

    /// <summary>The same string twice is a real authoring decision, so it is reported rather than
    /// resolved in silence - which is exactly what a whole-file override could not do.</summary>
    [Fact]
    public void Two_mods_renaming_one_string_collide_loudly()
    {
        byte[] container = Table(("Items", "dart_rifle", "Dart Rifle"));
        FolderModLayer modA = MakeLayer("mod_a", Section("Items", ("dart_rifle", "VSS Vintorez")));
        FolderModLayer modB = MakeLayer("mod_b", Section("Items", ("dart_rifle", "Dragunov")));

        Assert.Throws<InvalidDataException>(() => Resolve(container, modA, modB));

        var conflicts = new ConcurrentQueue<FragmentConflict>();
        byte[] built = _splitter.Apply(container, Resolve(container, conflicts, modA, modB));

        FragmentConflict reported = Assert.Single(conflicts);
        Assert.Equal("mod_b", reported.WinningLayer);
        Assert.Equal("Dragunov", ValueOf(built, "Items", "dart_rifle"));
    }

    /// <summary>A staged section addresses like any other fragment: container path, then the id.</summary>
    [Fact]
    public void A_staged_section_classifies_against_its_container()
    {
        string root = Path.Combine(_sandbox, "layer");
        string dir = Path.Combine(root, "mods", Container);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, StringTableContainerSplitter.IdOf("Items")),
            Section("Items", ("dart_rifle", "VSS Vintorez")));

        var layer = new FolderModLayer(root, "layer");

        KeyValuePair<uint, IReadOnlyList<FragmentOverride>> staged = Assert.Single(layer.FragmentOverrides);
        Assert.Equal(NameHash.Compute(Container), staged.Key);

        // A staged path is normalised to lower case on the way in, which is why the one comparer
        // every override index is keyed by ignores case.
        Assert.Equal(
            StringTableContainerSplitter.IdOf("Items"),
            Assert.Single(staged.Value).FragmentId,
            FcbFragments.IdComparer);
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
            Path.Combine(dir, StringTableContainerSplitter.FileName),
            Table(("Items", "dart_rifle", "VSS Vintorez")));

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => new FolderModLayer(root, "legacy"));

        Assert.Contains("one file per section", error.Message);
        Assert.Contains("rml fragments", error.Message);
    }

    private Dictionary<string, string> Resolve(byte[] container, params FolderModLayer[] layers)
        => Resolve(container, null, layers);

    private Dictionary<string, string> Resolve(
        byte[] container, ConcurrentQueue<FragmentConflict>? conflicts, params FolderModLayer[] layers)
    {
        IContainerTree tree = _splitter.Open(container);
        return FragmentMerge.BuildOverrideIndex(layers)[NameHash.Compute(Container)]
            .ToDictionary(kv => kv.Key, kv => FragmentMerge.Resolve(_splitter, tree, kv.Key, kv.Value, conflicts));
    }

    private FolderModLayer MakeLayer(string name, string sectionXml)
    {
        string dir = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(dir);
        var layer = new FolderModLayer(dir, name);

        string id = StringTableContainerSplitter.IdOf((string)XDocument.Parse(sectionXml).Root!.Attribute("name")!);
        string staged = $@"{Container}\{id}";
        layer.Stage(NameHash.Compute(staged), staged, "xml", Encoding.UTF8.GetBytes(sectionXml));
        return layer;
    }

    /// <summary>A table in the shape the game ships: <c>&lt;stringtable&gt;</c> of sections of
    /// strings, one row per string, grouped into sections in the order they first appear.</summary>
    private static byte[] Table(params (string Section, string Enum, string Value)[] rows)
        => RmlDocument.Serialize(new XElement("stringtable",
            new XAttribute("language", "english"),
            rows.GroupBy(r => r.Section, StringComparer.Ordinal)
                .Select(g => SectionElement(g.Key, [.. g.Select(r => (r.Enum, r.Value))]))));

    private static string Section(string name, params (string Enum, string Value)[] strings)
        => StringTableContainerSplitter.Instance.Canonicalize(SectionElement(name, strings).ToString());

    private static XElement SectionElement(string name, (string Enum, string Value)[] strings)
        => new("section",
            new XAttribute("name", name),
            strings.Select(s => new XElement("string",
                new XAttribute("enum", s.Enum), new XAttribute("value", s.Value))));

    private static string? ValueOf(byte[] table, string section, string key)
        => RmlDocument.Deserialize(table)
            .Elements("section").Single(s => (string?)s.Attribute("name") == section)
            .Elements("string").Single(s => (string?)s.Attribute("enum") == key)
            .Attribute("value")?.Value;
}
