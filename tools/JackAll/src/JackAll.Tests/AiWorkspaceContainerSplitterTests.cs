using System.Xml.Linq;
using JackAll.Core.Format.Ai;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>One fragment per instance of a brain workspace's source.</summary>
public class AiWorkspaceContainerSplitterTests
{
    private static readonly AiWorkspaceContainerSplitter Splitter = AiWorkspaceContainerSplitter.Instance;

    private const string Stoopid = AiWorkspaceTests.Stoopid;
    private const string Merc = AiWorkspaceTests.Merc;

    private const string FollowTarget = "::BrainStoopidSoldier/StoopidBrain/FollowTarget";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Stoopid, Merc);

    [Theory]
    [InlineData("mercbrain.ai.rml", true)]
    [InlineData("mercbrain.source.xml", false)]
    public void A_brain_is_recognised_by_its_extension(string fileName, bool splits)
        => Assert.Equal(splits, ContainerFormats.IsContainerSegment(fileName));

    [Fact]
    public void An_instance_is_staged_under_its_name_hash()
    {
        const string name = "::MercBrain/MercBrain/MercBrainCombat/LauncherInHand";
        var task = new XElement("Task", new XAttribute("Name", name));

        Assert.Equal($"LauncherInHand.{FcbClassDefinitions.Crc32Ascii(name)}.xml", AiWorkspaceContainerSplitter.IdOf(task));
    }

    /// <summary>Every instance spliced straight back loads as the shipped repository does.</summary>
    [Theory]
    [InlineData(Stoopid)]
    [InlineData(Merc)]
    public void Every_fragment_spliced_back_loads_the_same(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);
        Dictionary<string, string> every = tree.List().ToDictionary(r => r.Id, r => tree.Extract(r.Id)!);

        Assert.Null(PackedOf(original).FirstDifference(PackedOf(Splitter.Apply(original, every))));
    }

    [Fact]
    public void An_edited_parameter_reaches_the_compiled_repository()
    {
        if (Fixture.Read(Stoopid) is not { } original) return;

        XElement wait = Instance(original, $"{FollowTarget}/CTaskWait1");
        wait.Elements("Parameter").Single(p => (string?)p.Attribute("Name") == "timeToWait").SetAttributeValue("Value", "2.5");

        AiPackedRepository repo = PackedOf(Splitter.Apply(original, Stage(wait)));
        AiPackedTask task = repo.Tasks.Single(t => t.Name == $"{FollowTarget}/CTaskWait1");

        Assert.Equal("2.5", (string?)RmlDocument.Deserialize(repo.Blobs[task.Blob].Rml).Attribute("timeToWait"));
    }

    [Fact]
    public void A_new_task_runs_under_the_plan_that_adds_it()
    {
        if (Fixture.Read(Stoopid) is not { } original) return;

        const string name = $"{FollowTarget}/NewWait";
        XElement plan = Instance(original, FollowTarget);
        plan.Add(new XElement("Add", new XAttribute("Task", name)));
        var task = new XElement("Task", new XAttribute("Name", name), new XAttribute("Class", "CTaskWait"),
            new XAttribute("Looping", "0"), new XAttribute("Independent", "0"),
            new XElement("Parameter", new XAttribute("Name", "timeToWait"), new XAttribute("Value", "1")));

        AiPackedRepository repo = PackedOf(Splitter.Apply(original, Stage(plan, task)));
        AiConnectionRecord owner = repo.ConnectionsOf(repo.Tasks.Single(t => t.Name == name))[0];

        Assert.Equal(AiConnectionKind.Owner, owner.Kind);
        Assert.Equal(FollowTarget, repo.Tasks[owner.Source].Name);
    }

    [Fact]
    public void Two_mods_adding_to_one_plan_both_land()
    {
        if (Fixture.Read(Stoopid) is not { } original) return;

        string ancestor = Splitter.Open(original).Extract(AiWorkspaceContainerSplitter.IdOf(Instance(original, FollowTarget)))!;
        (string ours, string theirs) = (WithAdd(ancestor, "Ours"), WithAdd(ancestor, "Theirs"));

        (string merged, IReadOnlyList<string> conflicts) = Splitter.Merge("", ancestor, ours, theirs);

        Assert.Empty(conflicts);
        Assert.Equal(
            [$"{FollowTarget}/Ours", $"{FollowTarget}/Theirs"],
            XElement.Parse(merged).Elements("Add").Select(a => (string)a.Attribute("Task")!).Where(t => t.EndsWith("Ours") || t.EndsWith("Theirs")));
    }

    [Fact]
    public void A_fragment_holding_another_instance_is_refused()
    {
        if (Fixture.Read(Stoopid) is not { } original) return;

        XElement wait = Instance(original, $"{FollowTarget}/CTaskWait1");

        Assert.Throws<InvalidDataException>(() => Splitter.Apply(original, new Dictionary<string, string> { ["CTaskWait1.1.xml"] = wait.ToString() }));
    }

    private static AiPackedRepository PackedOf(byte[] workspace) => AiPackedRepository.Read(AiWorkspaceFile.Read(workspace).Packed);

    private static XElement Instance(byte[] workspace, string name)
        => AiWorkspaceFile.Read(workspace).Source.Elements().Single(e => (string?)e.Attribute("Name") == name);

    private static Dictionary<string, string> Stage(params XElement[] instances)
        => instances.ToDictionary(AiWorkspaceContainerSplitter.IdOf, i => i.ToString());

    private static string WithAdd(string plan, string task)
    {
        XElement element = XElement.Parse(plan);
        element.Add(new XElement("Add", new XAttribute("Task", $"{FollowTarget}/{task}")));
        return element.ToString();
    }
}
