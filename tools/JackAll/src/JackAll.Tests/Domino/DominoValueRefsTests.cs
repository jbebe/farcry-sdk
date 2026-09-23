using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.Tests;

public class DominoValueRefsTests
{
    private static ReconstructedGraph BuildFrom(string source) =>
        GraphBuilder.Build(UserGraphParser.Parse(DominoLuaSource.Parse(source)));

    [Fact]
    public void A_bark_pairs_its_mission_tag_from_init_with_its_block()
    {
        var graph = BuildFrom("""
            function export:Init()
                self.BarkMissionTag = "A1SM03_SE02A";
            end;

            function export:f_1_Out()
                self[4].Mission = self.BarkMissionTag;
                self[4].Block = "GREET";
                self[4]._type.In(self[4]);
            end;

            function export:Create(cbox)
                self[4] = cbox:CreateBox("Domino/System/PlayBark.lua");
            end;
            """);

        ValueRef bark = Assert.Single(DominoValueRefs.For(Assert.Single(graph.Nodes), graph));
        Assert.Equal(ValueRefKind.Bark, bark.Kind);
        Assert.Equal("A1SM03_SE02A", bark.Value);
        Assert.Equal("GREET", bark.Detail);
        Assert.Equal("BarkMissionTag", bark.Variable);
    }

    [Fact]
    public void An_untyped_sub_graph_pin_is_a_sound_when_the_graph_preloads_it()
    {
        var graph = BuildFrom("""
            function export:Create(cbox)
                cbox:LoadResource("sndres0x004e2911", "CSoundResource");
                self[1] = cbox:CreateBox("Domino/User/Common.BUDDY_PHONE_CALL_W1.lua");
            end;

            function export:Init()
                self[1].FrankStart = "0x004e2911";
                self[1].Other = "0x004e0000";
            end;
            """);

        ValueRef sound = Assert.Single(DominoValueRefs.For(Assert.Single(graph.Nodes), graph));
        Assert.Equal(ValueRefKind.Sound, sound.Kind);
        Assert.Equal(0x004e2911u, sound.SoundId);
    }

    [Fact]
    public void A_value_only_known_at_runtime_names_nothing()
    {
        var graph = BuildFrom("""
            function export:Create(cbox)
                self[1] = cbox:CreateBox("Domino/System/ObjectiveState.lua");
            end;

            function export:f_2_Out()
                self[1].ObjectiveState = self.Computed;
            end;
            """);

        Assert.Empty(DominoValueRefs.For(Assert.Single(graph.Nodes), graph));
    }

    [Theory]
    [InlineData(DominoFixtures.FastTravel, 21)]
    [InlineData(DominoFixtures.TaxiRide, 33)]
    public void A_real_graphs_preloaded_sound_literals_are_found(string script, int expected)
    {
        if (Fixture.ReadText(script) is not { } source) return;

        ReconstructedGraph graph = BuildFrom(source);

        Assert.Equal(expected, graph.Nodes.SelectMany(n => DominoValueRefs.For(n, graph)).Count(r => r.Kind == ValueRefKind.Sound));
    }
}
