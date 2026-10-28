using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>A UI dependency list, one fragment per package or sound bank, merged as a list.</summary>
public class UiDepLoadContainerSplitterTests : IDisposable
{
    private static readonly UiDepLoadContainerSplitter Splitter = UiDepLoadContainerSplitter.Instance;

    /// <summary>The widescreen English list: 50 packages and 3 sound banks.</summary>
    private const string List = "UiDepLoad/_depload.xml";

    private const string ListPath = @"ui\localized\pcwidescreen\eng\ui\_depload.xml";
    private const string FontsDesc = @"ui\localized\pcwidescreen\eng\ui\fonts.mgb.desc";

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(List);

    [Theory]
    [InlineData("_depload.xml", true)]
    [InlineData("world1_depload.xml", false)]
    public void Only_a_ui_list_is_recognised(string fileName, bool splits)
        => Assert.Equal(splits, ContainerFormats.IsContainerSegment(fileName));

    [Fact]
    public void Each_resource_is_one_fragment_numbered_by_its_crc()
    {
        if (Fixture.Read(List) is not { } list) return;

        IReadOnlyList<FcbFragmentInfo> rows = Splitter.Open(list).List();

        Assert.Equal(53, rows.Count);
        Assert.Contains(rows, r => r.Id == "fonts.mgb.desc.2312048719.xml");
        Assert.Equal(2312048719u, NameHash.Compute(FontsDesc));
    }

    [Fact]
    public void Every_fragment_spliced_back_gives_the_shipped_bytes()
    {
        if (Fixture.Read(List) is not { } list) return;

        IContainerTree tree = Splitter.Open(list);
        Dictionary<string, string> every = tree.List().ToDictionary(r => r.Id, r => tree.Extract(r.Id)!);

        Fixture.AssertSameBytes(List, list, Splitter.Apply(list, every));
    }

    [Fact]
    public void Two_mods_adding_to_one_package_both_land()
    {
        if (Fixture.Read(List) is not { } list) return;

        string id = UiDepLoadContainerSplitter.IdOf(FontsDesc);
        FolderModLayer modA = Stage("mod_a", id, WithFile(list, id, @"ui\fonts\a_30.mft"));
        FolderModLayer modB = Stage("mod_b", id, WithFile(list, id, @"ui\fonts\b_30.mft"));

        XElement merged = XElement.Parse(Splitter.Open(Splitter.Apply(list,
            TestSupport.ResolveFragments(Splitter, list, ListPath, null, modA, modB))).Extract(id)!);

        Assert.Contains(merged.Elements(), e => (string?)e.Attribute("ID") == @"ui\fonts\a_30.mft");
        Assert.Contains(merged.Elements(), e => (string?)e.Attribute("ID") == @"ui\fonts\b_30.mft");
    }

    [Fact]
    public void A_fragment_filed_under_another_resource_is_refused()
    {
        if (Fixture.Read(List) is not { } list) return;

        string xml = Splitter.Open(list).Extract(UiDepLoadContainerSplitter.IdOf(FontsDesc))!;

        var ex = Assert.Throws<InvalidDataException>(() => Splitter.Apply(list,
            new Dictionary<string, string> { ["hud.mgb.desc.12345.xml"] = xml }));
        Assert.Contains(UiDepLoadContainerSplitter.IdOf(FontsDesc), ex.Message);
    }

    private static string WithFile(byte[] list, string id, string file)
    {
        XElement resource = XElement.Parse(Splitter.Open(list).Extract(id)!);
        resource.Add(new XElement("CMagmaUIResource", new XAttribute("ID", file), new XAttribute("crc_ID", NameHash.Compute(file))));
        return resource.ToString();
    }

    private FolderModLayer Stage(string name, string id, string xml)
        => TestSupport.StageFragment(_sandbox, name, ListPath, id, xml);
}
