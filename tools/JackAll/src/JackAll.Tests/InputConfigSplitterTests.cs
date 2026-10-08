using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>One fragment per named section of the control config files, merged as a list.</summary>
public class InputConfigSplitterTests : IDisposable
{
    private static readonly InputConfigContainerSplitter Splitter = InputConfigContainerSplitter.Instance;

    /// <summary>The patch's action maps: 42 of them, beside root-level imports and a config block.</summary>
    public const string ActionMaps = "InputConfig/inputactionmapcommon.xml";

    /// <summary>The control list: six categories.</summary>
    public const string Controls = "InputConfig/defaultusercontrols.xml";

    /// <summary>The console's one action map, the only shipped section holding the same binding twice.</summary>
    public const string Console = "InputConfig/inputactionmapconsole.xml";

    private const string ActionMapsPath = @"config\inputactionmapcommon.xml";
    private const string ControlsPath = @"config\defaultusercontrols.xml";
    private const string ConsolePath = @"config\inputactionmapconsole.xml";

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(ActionMaps, Controls, Console);

    [Theory]
    [InlineData("defaultusercontrols.xml", true)]
    [InlineData("inputactionmapsingle.xml", true)]
    [InlineData("gamemodesconfig.xml", false)]
    public void Both_files_and_every_action_map_are_recognised(string fileName, bool splits)
        => Assert.Equal(splits, ContainerFormats.IsContainerSegment(fileName));

    [Fact]
    public void Each_named_section_is_one_fragment()
    {
        if (Fixture.Read(ActionMaps) is not { } maps || Fixture.Read(Controls) is not { } controls) return;

        IReadOnlyList<FcbFragmentInfo> mapRows = Splitter.Open(maps).List();
        Assert.Equal(42, mapRows.Count);
        Assert.Contains(mapRows, r => r.Id == "common_gameplay.xml");
        Assert.Equal(6, Splitter.Open(controls).List().Count);
    }

    /// <summary>Every section spliced straight back reads the same, with the root's own imports and
    /// config untouched and the file still in the encoding and line endings it shipped with.</summary>
    [Fact]
    public void Every_section_spliced_back_keeps_the_file()
    {
        if (Fixture.Read(ActionMaps) is not { } maps) return;

        IContainerTree tree = Splitter.Open(maps);
        Dictionary<string, string> every = tree.List().ToDictionary(r => r.Id, r => tree.Extract(r.Id)!);

        byte[] rebuilt = Splitter.Apply(maps, every);

        IContainerTree again = Splitter.Open(rebuilt);
        Assert.All(every, kv => Assert.Equal(kv.Value, again.Extract(kv.Key)));
        Assert.Equal(tree.Skeleton(_ => true), again.Skeleton(_ => true));
        string text = Encoding.Latin1.GetString(rebuilt);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"iso-8859-1\"?>", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(text.Count(c => c == '\n'), text.Split("\r\n").Length - 1);
    }

    [Fact]
    public void Two_mods_adding_to_one_action_map_both_land()
    {
        if (Fixture.Read(ActionMaps) is not { } maps) return;

        const string id = "common_in_vehicle.xml";
        FolderModLayer modA = Stage("mod_a", ActionMapsPath, id, WithChild(maps, id, Binding("kb:v", "active_camerathird")));
        FolderModLayer modB = Stage("mod_b", ActionMapsPath, id, WithChild(maps, id, Binding("kb:b", "horn")));

        XElement merged = SectionOf(Splitter.Apply(maps, Resolve(maps, ActionMapsPath, null, modA, modB)), id);

        Assert.Contains(merged.Elements("Binding"), b => (string?)b.Attribute("signal") == "active_camerathird");
        Assert.Contains(merged.Elements("Binding"), b => (string?)b.Attribute("signal") == "horn");
    }

    /// <summary>Two mods rebinding one control differently is a real conflict, settled by load order,
    /// and only that control is settled: the losing mod's other additions stay.</summary>
    [Fact]
    public void A_clash_on_one_control_keeps_both_mods_other_edits()
    {
        if (Fixture.Read(Controls) is not { } controls) return;

        const string id = "CATEGORY_VEHICLES.xml";
        XElement vanilla = SectionOf(controls, id);
        XElement fromA = Rebind(vanilla, "toggle_headlights", "kb:h");
        fromA.Add(new XElement("Control", new XAttribute("name", "horn"), new XAttribute("key1", "kb:b")));
        FolderModLayer modA = Stage("mod_a", ControlsPath, id, fromA.ToString());
        FolderModLayer modB = Stage("mod_b", ControlsPath, id, Rebind(vanilla, "toggle_headlights", "kb:j").ToString());

        var conflicts = new ConcurrentQueue<ModConflict>();
        XElement merged = SectionOf(Splitter.Apply(controls, Resolve(controls, ControlsPath, conflicts, modA, modB)), id);

        Assert.Equal("mod_b", Assert.Single(conflicts).WinningLayer);
        Assert.Equal("kb:j", (string?)ControlNamed(merged, "toggle_headlights").Attribute("key1"));
        ControlNamed(merged, "horn");
    }

    [Fact]
    public void A_repeated_binding_survives_a_merge_of_its_action_map()
    {
        if (Fixture.Read(Console) is not { } console) return;

        const string id = "console.xml";
        FolderModLayer modA = Stage("mod_a", ConsolePath, id, WithChild(console, id, Binding("kb:f11", "console_a")));
        FolderModLayer modB = Stage("mod_b", ConsolePath, id, WithChild(console, id, Binding("kb:f12", "console_b")));

        XElement merged = SectionOf(Splitter.Apply(console, Resolve(console, ConsolePath, null, modA, modB)), id);

        XElement vanilla = SectionOf(console, id);
        Assert.Equal(2, MinusBindings(vanilla));
        Assert.Equal(2, MinusBindings(merged));
        Assert.Equal(vanilla.Elements().Count() + 2, merged.Elements().Count());
    }

    private static int MinusBindings(XElement section)
        => section.Elements("Binding").Count(b => (string?)b.Attribute("input") == "kb:-");

    internal static XElement Binding(string input, string signal)
        => new("Binding", new XAttribute("input", input), new XAttribute("action", "press"), new XAttribute("signal", signal));

    private static XElement ControlNamed(XElement category, string name)
        => category.Elements("Control").Single(c => (string?)c.Attribute("name") == name);

    private static XElement Rebind(XElement category, string control, string key)
    {
        var copy = new XElement(category);
        ControlNamed(copy, control).SetAttributeValue("key1", key);
        return copy;
    }

    private static XElement SectionOf(byte[] file, string id) => XElement.Parse(Splitter.Open(file).Extract(id)!);

    internal static string WithChild(byte[] file, string id, XElement child)
    {
        XElement section = SectionOf(file, id);
        section.Add(child);
        return section.ToString();
    }

    private static Dictionary<string, string> Resolve(
        byte[] file, string containerPath, ConcurrentQueue<ModConflict>? conflicts, params FolderModLayer[] layers)
        => TestSupport.ResolveFragments(Splitter, file, containerPath, conflicts, layers);

    private FolderModLayer Stage(string name, string containerPath, string id, string xml)
        => TestSupport.StageFragment(_sandbox, name, containerPath, id, xml);
}
