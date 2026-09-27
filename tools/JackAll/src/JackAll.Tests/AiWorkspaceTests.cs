using System.Xml.Linq;
using JackAll.Tools.Ai;

namespace JackAll.Tests;

public class AiWorkspaceTests
{
    // The smallest shipped brain.
    public const string Stoopid = "Ai/stoopidsoldierbrain.ai.rml";

    // Parameter names with spaces, which are not legal XML names.
    public const string Buddy = "Ai/buddyworkspace.ai.rml";

    // Every soldier's brain: 13,582 tasks.
    public const string Merc = "Ai/mercbrain.ai.rml";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Stoopid, Buddy, Merc);

    [Theory]
    [InlineData(Stoopid)]
    [InlineData(Buddy)]
    [InlineData(Merc)]
    public void Rewriting_a_shipped_workspace_reproduces_it_byte_for_byte(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        AiWorkspaceFile file = AiWorkspaceFile.Read(original);

        Fixture.AssertSameBytes(path, original, file.Write());
        Fixture.AssertSameBytes(path, file.Packed, AiPackedRepository.Read(file.Packed).Write());
    }

    [Theory]
    [InlineData(Stoopid)]
    [InlineData(Buddy)]
    [InlineData(Merc)]
    public void Packing_the_shipped_source_loads_the_same_as_the_shipped_repository(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        AiWorkspaceFile file = AiWorkspaceFile.Read(original);
        AiPackedRepository packed = AiPackedRepository.Read(AiWorkspacePacker.Pack(file.Source));

        Assert.Null(AiPackedRepository.Read(file.Packed).FirstDifference(packed));
    }

    [Fact]
    public void A_task_names_the_plan_that_adds_it_then_its_exits()
    {
        if (Fixture.Read(Stoopid) is not { } original) return;

        AiPackedRepository repo = AiPackedRepository.Read(AiWorkspaceFile.Read(original).Packed);
        AiPackedTask task = repo.Tasks.Single(t => t.Name.EndsWith("/FollowTarget/CTaskSetPawnAttribute1"));

        IReadOnlyList<AiConnectionRecord> records = repo.ConnectionsOf(task);

        Assert.Equal(AiConnectionKind.Owner, records[0].Kind);
        Assert.EndsWith("/StoopidBrain/FollowTarget", repo.Tasks[records[0].Source].Name);
        Assert.Equal(AiConnectionKind.Exit, records[1].Kind);
        AiConnectionTarget next = Assert.Single(records[1].Targets);
        Assert.EndsWith("/CTaskPathFindAndMoveTo1", repo.Tasks[next.Task].Name);
    }

    [Fact]
    public void An_edited_parameter_reaches_the_compiled_repository()
    {
        if (Fixture.Read(Stoopid) is not { } original) return;

        AiWorkspaceFile file = AiWorkspaceFile.Read(original);
        XElement wait = file.Source.Elements("Task")
            .Single(t => ((string)t.Attribute("Name")!).EndsWith("/FollowTarget/CTaskWait1"))
            .Elements("Parameter").Single(p => (string?)p.Attribute("Name") == "timeToWait");
        wait.SetAttributeValue("Value", "2.5");

        AiPackedRepository repo = AiPackedRepository.Read(AiWorkspacePacker.Pack(file.Source));
        AiPackedTask task = repo.Tasks.Single(t => t.Name.EndsWith("/FollowTarget/CTaskWait1"));

        XElement parameters = Core.Format.Rml.RmlDocument.Deserialize(repo.Blobs[task.Blob].Rml);
        Assert.Equal("2.5", (string?)parameters.Attribute("timeToWait"));
    }
}
