using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;
using JackAll.Core.Mods;
using System.Xml.Linq;

namespace JackAll.Tests;

/// <summary>
/// One fragment per mission of a world or map descriptor (`&lt;world&gt;.game.xml`). This is the file
/// every mod that adds a mission or a mission layer has to touch, so without a split they all
/// last-wins over each other on one shared file.
/// </summary>
public class WorldDescriptorSplitterTests
{
    private static readonly WorldDescriptorContainerSplitter Splitter = WorldDescriptorContainerSplitter.Instance;

    /// <summary>A compiled map descriptor: six missions and three sections.</summary>
    public const string Compiled = "WorldDescriptor/mp_17_dunes.game.xml";

    /// <summary>The multiplayer template, the one descriptor that ships as plain text.</summary>
    public const string PlainText = "WorldDescriptor/tmpla.game.xml";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Compiled, PlainText);

    /// <summary>A descriptor this cannot round-trip is refused outright, so it keeps the whole-file
    /// override rather than being half-split.</summary>
    [Fact]
    public void A_plain_text_descriptor_is_declined_rather_than_mangled()
    {
        if (Fixture.Read(PlainText) is not { } plain) return;

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Splitter.Open(plain));

        Assert.Contains("plain XML", error.Message);
    }

    /// <summary>
    /// Every mission extracted and spliced straight back reproduces the file it came from, byte for
    /// byte. It also exercises the rebuilt layer index, since that is regenerated on every apply rather
    /// than carried by a fragment.
    /// </summary>
    [Fact]
    public void Every_mission_extracts_and_splices_back_unchanged()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);
        Dictionary<string, string> everyMission = tree.List().ToDictionary(
            r => r.Id, r => tree.Extract(r.Id)!, FcbFragments.IdComparer);

        byte[] rebuilt = Splitter.Apply(original, everyMission);

        Fixture.AssertSameBytes(Compiled, original, rebuilt);
    }

    [Fact]
    public void Replacing_one_mission_changes_only_that_mission()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);
        IReadOnlyList<FcbFragmentInfo> rows = tree.List();

        string targetId = rows[0].Id;
        XElement edited = XElement.Parse(tree.Extract(targetId)!);
        edited.SetAttributeValue("State", "7");

        IContainerTree after = Splitter.Open(
            Splitter.Apply(original, new Dictionary<string, string> { [targetId] = edited.ToString() }));

        Assert.Equal(rows.Count, after.List().Count);
        Assert.Contains("State=\"7\"", after.Extract(targetId));
        foreach (FcbFragmentInfo row in rows.Skip(1))
        {
            Assert.Equal(tree.Extract(row.Id), after.Extract(row.Id));
        }
    }

    /// <summary>What an outpost mod does: a mission nobody shipped, with its own layer. The flat
    /// index has to gain that layer too, since the engine reads the index and not the mission.</summary>
    [Fact]
    public void A_new_mission_is_added_and_reaches_the_layer_index()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);

        const string name = "Missions/Outposts/Test/Zone_01";
        const string layerPath = @"missions\outposts\test\zone_01";
        string mission =
            $"""<Mission Name="{name}" State="1"><Layers><Layer Name="Zone_01" PathId="{layerPath}" """
            + """MissionLayer="1" MissionLayerActiveDflt="1" /></Layers></Mission>""";

        byte[] applied = Splitter.Apply(
            original,
            new Dictionary<string, string> { [WorldDescriptorContainerSplitter.IdOf(name)] = mission });

        Assert.Equal(tree.List().Count + 1, Splitter.Open(applied).List().Count);

        // The index is derived, so the added layer must show up there exactly once.
        XElement root = RmlDocument.Deserialize(applied);
        XElement index = root.Element("MissionsDef")!.Element("MissionLayers")!;
        Assert.Equal(1, index.Elements("Layer").Count(l => (string?)l.Attribute("PathId") == layerPath));

        // One layer per mission. Counted off the missions themselves rather than off List(), which
        // also carries the descriptor's sections.
        Assert.Equal(
            root.Element("MissionsDef")!.Element("Missions")!.Elements("Mission").Count(),
            index.Elements("Layer").Count());
    }

    [Fact]
    public void A_mission_staged_under_another_missions_name_is_refused()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Splitter.Apply(
            original,
            new Dictionary<string, string> { ["Missions\\Not\\ThisOne.xml"] = tree.Extract(tree.List()[0].Id)! }));

        Assert.Contains("ThisOne", error.Message);
    }

    /// <summary>Two mods adding different missions to one world both survive, which is the entire
    /// point of splitting this file.</summary>
    [Fact]
    public void Two_mods_adding_different_missions_both_survive()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        Dictionary<string, string> staged = [];
        foreach (string name in (string[])["Missions/Outposts/ModA/One", "Missions/Outposts/ModB/Two"])
        {
            staged[WorldDescriptorContainerSplitter.IdOf(name)] =
                $"""<Mission Name="{name}" State="1"><Layers><Layer Name="L" PathId="{name.ToLowerInvariant()}" /></Layers></Mission>""";
        }

        IContainerTree after = Splitter.Open(Splitter.Apply(original, staged));

        Assert.All(staged.Keys, id => Assert.NotNull(after.Extract(id)));
    }

    /// <summary>A descriptor nobody edited compares equal to itself, and one mission's change shows
    /// up as exactly that mission differing.</summary>
    [Fact]
    public void The_skeleton_hides_mission_content_but_not_a_missing_mission()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);
        IReadOnlyList<FcbFragmentInfo> rows = tree.List();

        var ids = new HashSet<string>(rows.Select(r => r.Id), FcbFragments.IdComparer);
        string shape = tree.Skeleton(ids.Contains)!;

        // Editing a mission's content leaves the shape alone.
        XElement edited = XElement.Parse(tree.Extract(rows[0].Id)!);
        edited.SetAttributeValue("State", "5");
        IContainerTree changed = Splitter.Open(
            Splitter.Apply(original, new Dictionary<string, string> { [rows[0].Id] = edited.ToString() }));
        Assert.Equal(shape, changed.Skeleton(ids.Contains));

        // Dropping one from the comparison does not.
        Assert.NotEqual(shape, tree.Skeleton(id => ids.Contains(id) && !FcbFragments.IdComparer.Equals(id, rows[0].Id)));
    }

    /// <summary>The same missions applied in a different order have to give the same bytes: a build
    /// regenerates the patch from scratch every time, so an assembly that followed the caller's
    /// enumeration order would rewrite the archive on every build.</summary>
    [Fact]
    public void The_order_missions_are_staged_in_does_not_reach_the_bytes()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);

        Dictionary<string, string> forward = new(FcbFragments.IdComparer);
        foreach (FcbFragmentInfo row in tree.List().Take(8))
        {
            XElement edited = XElement.Parse(tree.Extract(row.Id)!);
            edited.SetAttributeValue("State", "3");
            forward[row.Id] = edited.ToString();
        }

        var backward = new Dictionary<string, string>(forward.Reverse(), FcbFragments.IdComparer);

        Assert.Equal(Splitter.Apply(original, forward), Splitter.Apply(original, backward));
    }

    /// <summary>Every section splices straight back unchanged, as every mission does.</summary>
    [Fact]
    public void Every_section_extracts_and_splices_back_unchanged()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);
        Dictionary<string, string> staged = tree.List().Select(r => r.Id).Where(id => id.StartsWith('_'))
            .ToDictionary(id => id, id => tree.Extract(id)!, FcbFragments.IdComparer);

        Assert.NotEmpty(staged);
        Fixture.AssertSameBytes(Compiled, original, Splitter.Apply(original, staged));
    }

    /// <summary>
    /// The descriptor's non-mission parts are their own override units. This is the edit that used to
    /// cost a mod the whole file: Scubrah's Patch raises the shadow radius and view distance in
    /// Environment, which no mission fragment could carry.
    /// </summary>
    [Fact]
    public void An_environment_edit_is_a_fragment_of_its_own()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);
        const string id = "_environment.xml";
        XElement edited = XElement.Parse(tree.Extract(id)!);
        edited.Element("Shadow")!.SetAttributeValue("DynamicShadowRadius", "1000");

        byte[] applied = Splitter.Apply(
            original, new Dictionary<string, string> { [id] = edited.ToString() });

        XElement root = RmlDocument.Deserialize(applied);
        Assert.Equal(
            "1000",
            (string?)root.Element("Environment")!.Element("Shadow")!.Attribute("DynamicShadowRadius"));

        // The missions it sits beside are untouched, which is the whole point of splitting it out.
        Assert.Equal(tree.List().Count, Splitter.Open(applied).List().Count);
    }

    /// <summary>
    /// A section change no longer moves the skeleton, so an importer can now express it per fragment
    /// instead of falling back to a whole-file override of the descriptor.
    /// </summary>
    [Fact]
    public void A_section_edit_leaves_the_skeleton_alone()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);
        const string id = "_environment.xml";

        var ids = new HashSet<string>(tree.List().Select(r => r.Id), FcbFragments.IdComparer);
        XElement edited = XElement.Parse(tree.Extract(id)!);
        edited.SetAttributeValue("ModAdded", "1");

        IContainerTree changed = Splitter.Open(
            Splitter.Apply(original, new Dictionary<string, string> { [id] = edited.ToString() }));

        Assert.Equal(tree.Skeleton(ids.Contains), changed.Skeleton(ids.Contains));
    }

    /// <summary>A section staged under the wrong id is refused rather than written somewhere odd.</summary>
    [Fact]
    public void A_section_staged_under_another_sections_name_is_refused()
    {
        if (Fixture.Read(Compiled) is not { } original) return;

        string environment = Splitter.Open(original).Extract("_environment.xml")!;

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Splitter.Apply(
            original, new Dictionary<string, string> { ["_grids.xml"] = environment }));

        Assert.Contains("_environment.xml", error.Message);
    }
}
