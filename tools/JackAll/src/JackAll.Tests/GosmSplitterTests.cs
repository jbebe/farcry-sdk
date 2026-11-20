using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>One fragment per root group, state and track of a pawn state machine, merged child by child.</summary>
public class GosmSplitterTests : IDisposable
{
    private static readonly GosmContainerSplitter Splitter = GosmContainerSplitter.Instance;

    /// <summary>The weapons state machine: 35 groups and 70 states, CRLF, no declaration.</summary>
    private const string Weapons = "StateMachine/weapons.gosm.xml";

    private const string WeaponsPath = @"scripts\engine\objects\pawn\statemachine\weapons.gosm.xml";
    private const string States = "::Pawn Weapons/Weapon Mechanics/States/";
    private static readonly string Switch = GosmContainerSplitter.IdOf(States + "AllowWeaponSwitch");

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_was_actually_found() => Fixture.AssertPresent(Weapons);

    [Theory]
    [InlineData("weapons.gosm.xml", true)]
    [InlineData("Main_Avatar.GOSM.xml", true)]
    [InlineData("weapons.xml", false)]
    public void The_file_is_recognised_by_name(string fileName, bool splits)
        => Assert.Equal(splits, ContainerFormats.IsContainerSegment(fileName));

    [Fact]
    public void Each_group_and_state_is_one_fragment()
    {
        if (Fixture.Read(Weapons) is not { } weapons) return;

        IContainerTree tree = Splitter.Open(weapons);

        Assert.Equal(105, tree.List().Count);
        Assert.StartsWith("<Group FullName=\"" + States + "AllowWeaponSwitch\"", tree.Extract(Switch), StringComparison.Ordinal);
    }

    /// <summary>Every fragment spliced straight back reads the same, with the file's CRLF line endings
    /// kept and no declaration added.</summary>
    [Fact]
    public void Every_fragment_spliced_back_keeps_the_file()
    {
        if (Fixture.Read(Weapons) is not { } weapons) return;

        IContainerTree tree = Splitter.Open(weapons);
        Dictionary<string, string> every = tree.List().ToDictionary(r => r.Id, r => tree.Extract(r.Id)!);

        byte[] rebuilt = Splitter.Apply(weapons, every);

        IContainerTree again = Splitter.Open(rebuilt);
        Assert.All(every, kv => Assert.Equal(kv.Value, again.Extract(kv.Key)));
        Assert.Equal(tree.Skeleton(_ => true), again.Skeleton(_ => true));
        string text = Encoding.UTF8.GetString(rebuilt);
        Assert.StartsWith("<BlackBox.GOStateMachine", text, StringComparison.Ordinal);
        Assert.Equal(text.Count(c => c == '\n'), text.Split("\r\n").Length - 1);
    }

    /// <summary>UFCP's two prepare states and another mod's own state all join the weapon switch group.</summary>
    [Fact]
    public void Two_mods_adding_to_one_group_both_land()
    {
        if (Fixture.Read(Weapons) is not { } weapons) return;

        FolderModLayer modA = Stage("mod_a", WithStateRefs(weapons, "Preparing_Weapon", "Preparing_Weapon_CanIronsight"));
        FolderModLayer modB = Stage("mod_b", WithStateRefs(weapons, "HolsterWeapons"));

        var conflicts = new ConcurrentQueue<ModConflict>();
        XElement merged = GroupOf(Splitter.Apply(weapons, Resolve(weapons, conflicts, modA, modB)));

        Assert.Empty(conflicts);
        string[] paths = [.. merged.Elements("StateRef").Select(r => (string)r.Attribute("Path")!)];
        Assert.Contains(States + "Preparing_Weapon", paths);
        Assert.Contains(States + "Preparing_Weapon_CanIronsight", paths);
        Assert.Contains(States + "HolsterWeapons", paths);
        Assert.Equal(7 + 3, paths.Length);
    }

    [Fact]
    public void One_event_changed_two_ways_is_a_conflict_settled_by_load_order()
    {
        if (Fixture.Read(Weapons) is not { } weapons) return;

        FolderModLayer modA = Stage("mod_a", WithEventSignal(weapons, "select_secondary_a"));
        FolderModLayer modB = Stage("mod_b", WithEventSignal(weapons, "select_secondary_b"));

        var conflicts = new ConcurrentQueue<ModConflict>();
        XElement merged = GroupOf(Splitter.Apply(weapons, Resolve(weapons, conflicts, modA, modB)));

        Assert.Equal("mod_b", Assert.Single(conflicts).WinningLayer);
        Assert.Equal("select_secondary_b", (string?)FirstEvent(merged).Attribute("Signal"));
    }

    [Fact]
    public void A_new_state_is_added_at_the_root()
    {
        if (Fixture.Read(Weapons) is not { } weapons) return;

        var state = new XElement("State", new XAttribute("FullName", States + "ModState"), new XAttribute("Type", "CGOState"));

        byte[] built = Splitter.Apply(weapons, new Dictionary<string, string>
        {
            [GosmContainerSplitter.IdOf(States + "ModState")] = state.ToString(),
        });

        Assert.Equal(States + "ModState", (string?)XElement.Parse(Encoding.UTF8.GetString(built)).Elements().Last().Attribute("FullName"));
    }

    [Fact]
    public void A_group_staged_under_another_name_is_refused()
    {
        if (Fixture.Read(Weapons) is not { } weapons) return;

        var wrong = new Dictionary<string, string> { [GosmContainerSplitter.IdOf(States + "xIdle")] = GroupOf(weapons).ToString() };

        Assert.Throws<InvalidDataException>(() => Splitter.Apply(weapons, wrong));
    }

    private static XElement GroupOf(byte[] file) => XElement.Parse(Splitter.Open(file).Extract(Switch)!);

    private static XElement FirstEvent(XElement group) => group.Elements("Event").First();

    private static string WithStateRefs(byte[] file, params string[] states)
    {
        XElement group = GroupOf(file);
        XElement last = group.Elements("StateRef").Last();
        last.AddAfterSelf(states.Select(s => new XElement("StateRef", new XAttribute("Path", States + s))));
        return group.ToString();
    }

    private static string WithEventSignal(byte[] file, string signal)
    {
        XElement group = GroupOf(file);
        FirstEvent(group).SetAttributeValue("Signal", signal);
        return group.ToString();
    }

    private static Dictionary<string, string> Resolve(
        byte[] file, ConcurrentQueue<ModConflict>? conflicts, params FolderModLayer[] layers)
        => TestSupport.ResolveFragments(Splitter, file, WeaponsPath, conflicts, layers);

    private FolderModLayer Stage(string name, string xml) => TestSupport.StageFragment(_sandbox, name, WeaponsPath, Switch, xml);
}
