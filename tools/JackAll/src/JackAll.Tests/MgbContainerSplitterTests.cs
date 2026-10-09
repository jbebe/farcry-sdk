using System.Collections.Concurrent;
using System.Xml.Linq;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Mgb;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>One fragment per area of a Magma package, plus its materials, strings and exports.</summary>
public class MgbContainerSplitterTests : IDisposable
{
    private static readonly MgbContainerSplitter Splitter = MgbContainerSplitter.Instance;

    /// <summary>A full menu page with exports.</summary>
    private const string Options = "Mgb/options.mgb";

    /// <summary>A small menu.</summary>
    private const string Controller = "Mgb/controller.mgb";

    /// <summary>A fonts-only package with no exports table.</summary>
    private const string Fonts = "Mgb/fonts.mgb";

    private const string OptionsPath = @"ui\localized\pcwidescreen\eng\ui\options.mgb";

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Options, Controller, Fonts);

    [Theory]
    [InlineData("hud.mgb", true)]
    [InlineData("hud.mgb.desc", false)]
    public void A_package_is_recognised_by_its_extension(string fileName, bool splits)
        => Assert.Equal(splits, ContainerFormats.IsContainerSegment(fileName));

    /// <summary>Every fragment spliced straight back gives the shipped bytes, so the counts the build
    /// derives - pools and distinct textures - are the ones retail carries.</summary>
    [Theory]
    [InlineData(Options)]
    [InlineData(Controller)]
    [InlineData(Fonts)]
    public void Every_fragment_spliced_back_gives_the_shipped_bytes(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);
        Dictionary<string, string> every = tree.List().ToDictionary(r => r.Id, r => tree.Extract(r.Id)!);

        Fixture.AssertSameBytes(path, original, Splitter.Apply(original, every));
    }

    [Fact]
    public void An_area_is_staged_under_its_name_hash()
    {
        if (Fixture.Read(Controller) is not { } controller) return;

        IReadOnlyList<FcbFragmentInfo> rows = Splitter.Open(controller).List();

        Assert.Contains(rows, r => r.Id == "_materials.xml");
        Assert.Contains(rows, r => r.Id == "_strings.xml");
        Assert.Contains(rows, r => r.Id == "Cursor.3268636600.xml");
        Assert.All(rows.Where(r => !r.Id.StartsWith('_')), r => Assert.NotNull(FragmentId.NumberOf(r.Id)));
    }

    /// <summary>Two mods each adding an element to one page and an export of it: both land, and the
    /// pools grow to hold them.</summary>
    [Fact]
    public void Two_mods_adding_to_one_page_both_land()
    {
        if (Fixture.Read(Options) is not { } options) return;

        IContainerTree tree = Splitter.Open(options);
        string page = PageId(tree);
        FolderModLayer modA = Stage("mod_a", page, WithElement(tree, page, "p_mod_a"));
        FolderModLayer modB = Stage("mod_b", page, WithElement(tree, page, "p_mod_b"));

        byte[] built = Splitter.Apply(options, Resolve(options, null, modA, modB));

        XElement merged = XElement.Parse(Splitter.Open(built).Extract(page)!);
        Assert.Contains(Hashed("p_mod_a"), ElementNames(merged));
        Assert.Contains(Hashed("p_mod_b"), ElementNames(merged));
        MgbPackage package = MgbPackage.Read(built);
        Assert.Equal(MgbPackage.Read(options).PoolCounts[24] + 2, package.PoolCounts[24]);
    }

    [Fact]
    public void Separate_edits_to_one_page_both_apply()
    {
        if (Fixture.Read(Options) is not { } options) return;

        IContainerTree tree = Splitter.Open(options);
        string page = PageId(tree);
        FolderModLayer modA = Stage("mod_a", page, Edited(tree, page, a => a.Element("CHILDREN")!.Elements().First().SetAttributeValue("HIDDEN", "true")));
        FolderModLayer modB = Stage("mod_b", page, Edited(tree, page, a => a.SetAttributeValue("FRAMERATE", "60")));

        var conflicts = new ConcurrentQueue<ModConflict>();
        XElement merged = XElement.Parse(Splitter.Open(Splitter.Apply(options, Resolve(options, conflicts, modA, modB))).Extract(page)!);

        Assert.Empty(conflicts);
        Assert.Equal("true", (string?)merged.Element("CHILDREN")!.Elements().First().Attribute("HIDDEN"));
        Assert.Equal("60", (string?)merged.Attribute("FRAMERATE"));
    }

    /// <summary>Two mods setting one value differently is a real conflict, settled by load order.</summary>
    [Fact]
    public void A_clash_on_one_value_is_reported()
    {
        if (Fixture.Read(Options) is not { } options) return;

        IContainerTree tree = Splitter.Open(options);
        string page = PageId(tree);
        FolderModLayer modA = Stage("mod_a", page, Edited(tree, page, a => a.SetAttributeValue("FRAMERATE", "60")));
        FolderModLayer modB = Stage("mod_b", page, Edited(tree, page, a => a.SetAttributeValue("FRAMERATE", "25")));

        var conflicts = new ConcurrentQueue<ModConflict>();
        XElement merged = XElement.Parse(Splitter.Open(Splitter.Apply(options, Resolve(options, conflicts, modA, modB))).Extract(page)!);

        Assert.Equal("mod_b", Assert.Single(conflicts).WinningLayer);
        Assert.Equal("25", (string?)merged.Attribute("FRAMERATE"));
    }

    [Fact]
    public void A_new_area_and_material_are_added_with_the_counts_they_need()
    {
        if (Fixture.Read(Controller) is not { } controller) return;

        IContainerTree tree = Splitter.Open(controller);
        XElement area = XElement.Parse(tree.Extract(tree.List().First(r => !r.Id.StartsWith('_')).Id)!);
        area.Element("USERDATA")!.SetAttributeValue("name", "a_added");
        XElement materials = XElement.Parse(tree.Extract("_materials.xml")!);
        materials.Add(new XElement("Material", new XAttribute("name", "added"),
            new XAttribute("texture", @"\textures\hud\added.png"), new XAttribute("REGION", "0 0 1 1")));

        byte[] built = Splitter.Apply(controller, new Dictionary<string, string>
        {
            [MgbContainerSplitter.IdOf(area)] = area.ToString(),
            ["_materials.xml"] = materials.ToString(),
        });

        MgbPackage before = MgbPackage.Read(controller);
        MgbPackage after = MgbPackage.Read(built);
        Assert.Equal(before.Areas.Count + 1, after.Areas.Count);
        Assert.Equal(before.MaterialExtra + 1, after.MaterialExtra);
        uint[] needed = MgbPools.Of(after);
        Assert.All(Enumerable.Range(0, MgbPools.Count), i => Assert.True(after.PoolCounts[i] >= needed[i], $"pool {i}"));
        Assert.Contains(Enumerable.Range(0, MgbPools.Count), i => after.PoolCounts[i] > before.PoolCounts[i]);
    }

    [Fact]
    public void An_exports_table_is_added_to_a_package_without_one()
    {
        if (Fixture.Read(Fonts) is not { } fonts || Fixture.Read(Controller) is not { } controller) return;

        string exports = Splitter.Open(controller).Extract("_exports.xml")!;

        byte[] built = Splitter.Apply(fonts, new Dictionary<string, string> { ["_exports.xml"] = exports });

        Assert.Equal(exports, Splitter.Open(built).Extract("_exports.xml"));
    }

    /// <summary>An added area and raised pool counts fit in fragments; a new page size does not.</summary>
    [Fact]
    public void Only_a_change_inside_the_fragments_is_expressible()
    {
        if (Fixture.Read(Controller) is not { } controller) return;

        MgbPackage package = MgbPackage.Read(controller);
        MgbArea added = MgbPackage.Read(controller).Areas[0];
        added.UserData.NameId = MgbTypeTable.Hash("a_added");
        package.Areas.Add(added);
        package.PoolCounts[0]++;
        IContainerTree vanilla = Splitter.Open(controller);

        Assert.True(FragmentDiff.IsExpressible(Splitter.Open(package.Write()), vanilla));
        package.PageWidth++;
        Assert.False(FragmentDiff.IsExpressible(Splitter.Open(package.Write()), vanilla));
    }

    [Fact]
    public void A_fragment_filed_under_the_wrong_area_is_refused()
    {
        if (Fixture.Read(Controller) is not { } controller) return;

        IContainerTree tree = Splitter.Open(controller);
        string id = tree.List().First(r => !r.Id.StartsWith('_')).Id;

        var ex = Assert.Throws<InvalidDataException>(() => Splitter.Apply(controller,
            new Dictionary<string, string> { ["elsewhere.12345.xml"] = tree.Extract(id)! }));
        Assert.Contains(id[..id.IndexOf('.')], ex.Message);
    }

    private static string PageId(IContainerTree tree)
        => tree.List().Where(r => !r.Id.StartsWith('_'))
            .First(r => XElement.Parse(tree.Extract(r.Id)!).Attribute("type")?.Value == "Page").Id;

    /// <summary>A name as a built package renders it, having kept only its hash.</summary>
    private static string Hashed(string name) => $"#{MgbTypeTable.Hash(name):X8}";

    private static IEnumerable<string?> ElementNames(XElement area)
        => area.Element("CHILDREN")!.Elements().Select(e => (string?)e.Element("USERDATA")?.Attribute("name"));

    /// <summary>The area with an empty placeholder named <paramref name="name"/> on the end.</summary>
    private static string WithElement(IContainerTree tree, string id, string name)
        => Edited(tree, id, area => area.Element("CHILDREN")!.Add(XElement.Parse(
            $"""
            <Element slot="77" type="Placeholder" HIDDEN="false" ISDUPLICATABLE="true" MASKMODE="NOMASK">
              <USERDATA name="{name}"><PROPERTIES /></USERDATA>
              <KEYFRAMES />
              <Placeholder />
            </Element>
            """)));

    private static string Edited(IContainerTree tree, string id, Action<XElement> edit)
    {
        XElement area = XElement.Parse(tree.Extract(id)!);
        edit(area);
        return area.ToString();
    }

    private static Dictionary<string, string> Resolve(
        byte[] file, ConcurrentQueue<ModConflict>? conflicts, params FolderModLayer[] layers)
        => TestSupport.ResolveFragments(Splitter, file, OptionsPath, conflicts, layers);

    private FolderModLayer Stage(string name, string id, string xml)
        => TestSupport.StageFragment(_sandbox, name, OptionsPath, id, xml);
}
