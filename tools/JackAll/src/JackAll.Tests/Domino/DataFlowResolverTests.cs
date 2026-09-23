using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;

namespace JackAll.Tests;

public class DataFlowResolverTests
{
    private static ReconstructedGraph Build(string source) =>
        GraphBuilder.Build(UserGraphParser.Parse(DominoLuaSource.Parse(source)));

    /// <summary>Two boxes and the standard `Init` preamble, so each test only has to state the handlers
    /// that matter to it.</summary>
    private static string WithBoxes(string handlers) => $$"""
        export = { };
        function export:Init(cbox)
            self[0] = cbox:CreateBox("Domino/System/SpawnBuddy.lua");
            self[1] = cbox:CreateBox("Domino/System/LookAtTarget.lua");
            self[2] = cbox:CreateBox("Domino/System/SpawnBuddy.lua");
        end;
        {{handlers}}
        """;

    [Fact]
    public void Joins_a_producer_and_consumer_through_the_graph_variable_between_them()
    {
        var graph = Build(WithBoxes("""
            function export:Spawn()
                self.BuddyPawn = self[0].SpawnedBuddy;
            end;
            function export:Look()
                self[1].Pawn = self.BuddyPawn;
                self[1]._type.Start(self[1]);
            end;
            """));

        DataEdge edge = Assert.Single(graph.DataEdges);
        Assert.Equal(DataEdgeKind.NodeToNode, edge.Kind);
        Assert.Equal("p:0", edge.SourceNodeId);
        Assert.Equal("SpawnedBuddy", edge.SourcePin);
        Assert.Equal("p:1", edge.TargetNodeId);
        Assert.Equal("Pawn", edge.TargetPin);
        Assert.Equal("BuddyPawn", edge.ViaVariable);
        Assert.False(edge.Ambiguous);
    }

    [Fact]
    public void Prefers_the_producer_earlier_in_the_same_handler_over_any_other()
    {
        // Box 2 also writes BuddyPawn, but box 0 wrote it in this very handler immediately before the
        // read - the read-then-use idiom, and not ambiguous.
        var graph = Build(WithBoxes("""
            function export:Elsewhere()
                self.BuddyPawn = self[2].SpawnedBuddy;
            end;
            function export:Run()
                self.BuddyPawn = self[0].SpawnedBuddy;
                self[1].Pawn = self.BuddyPawn;
            end;
            """));

        DataEdge edge = Assert.Single(graph.DataEdges);
        Assert.Equal("p:0", edge.SourceNodeId);
        Assert.False(edge.Ambiguous);
    }

    [Fact]
    public void Reports_every_candidate_producer_as_ambiguous_when_they_are_genuinely_different_boxes()
    {
        // Two different node types writing the same variable really is unresolvable from the flattened
        // script, so both are reported rather than one being guessed at.
        var graph = Build("""
            export = { };
            function export:Init(cbox)
                self[0] = cbox:CreateBox("Domino/System/SpawnBuddy.lua");
                self[1] = cbox:CreateBox("Domino/System/LookAtTarget.lua");
                self[2] = cbox:CreateBox("Domino/System/GetLocalPlayer.lua");
            end;
            function export:PathA()
                self.Who = self[0].SpawnedBuddy;
            end;
            function export:PathB()
                self.Who = self[2].LocalPlayer;
            end;
            function export:Look()
                self[1].Pawn = self.Who;
            end;
            """);

        Assert.Equal(2, graph.DataEdges.Count);
        Assert.All(graph.DataEdges, e => Assert.True(e.Ambiguous));
        Assert.Equal(["p:0", "p:2"], graph.DataEdges.Select(e => e.SourceNodeId).Order());
    }

    [Fact]
    public void Collapses_repeated_occurrences_of_one_operation_into_a_single_unambiguous_source()
    {
        // A mission that runs the same sequence per story branch writes the same variable from several
        // occurrences of the same node type. They aren't rival producers - they compute the same thing -
        // so one edge states the provenance instead of N interchangeable wires per consumer.
        var graph = Build("""
            export = { };
            function export:Init(cbox)
                self[0] = cbox:CreateBox("Domino/System/GetLocalPlayer.lua");
                self[1] = cbox:CreateBox("Domino/System/LookAtTarget.lua");
                self[2] = cbox:CreateBox("Domino/System/GetLocalPlayer.lua");
            end;
            function export:BranchA()
                self.Player = self[0].LocalPlayer;
            end;
            function export:BranchB()
                self.Player = self[2].LocalPlayer;
            end;
            function export:Look()
                self[1].Pawn = self.Player;
            end;
            """);

        DataEdge edge = Assert.Single(graph.DataEdges);
        Assert.False(edge.Ambiguous);
        Assert.True(edge.RepeatedSource);
        Assert.Equal(2, edge.SourceOccurrences);
        Assert.Equal("LocalPlayer", edge.SourcePin);
    }

    [Fact]
    public void Treats_a_variable_no_box_produces_as_this_graphs_own_data_input()
    {
        var graph = Build(WithBoxes("""
            function export:Look()
                self[1].Pawn = self.PawnFromParent;
            end;
            """));

        DataEdge edge = Assert.Single(graph.DataEdges);
        Assert.Equal(DataEdgeKind.GraphInput, edge.Kind);
        Assert.Null(edge.SourceNodeId);
        Assert.Equal("PawnFromParent", edge.ViaVariable);
        Assert.Equal("p:1", edge.TargetNodeId);
    }

    [Fact]
    public void Recognizes_the_rare_direct_box_to_box_data_assignment()
    {
        var graph = Build(WithBoxes("""
            function export:Look()
                self[1].Pawn = self[0].SpawnedBuddy;
            end;
            """));

        DataEdge edge = Assert.Single(graph.DataEdges);
        Assert.Equal(DataEdgeKind.NodeToNode, edge.Kind);
        Assert.Equal("p:0", edge.SourceNodeId);
        Assert.Equal("SpawnedBuddy", edge.SourcePin);
        Assert.Equal("p:1", edge.TargetNodeId);
        Assert.Null(edge.ViaVariable);
    }

    [Fact]
    public void Does_not_treat_a_literal_parameter_as_a_connection()
    {
        var graph = Build(WithBoxes("""
            function export:Look()
                self[1].Pawn = "SomeName";
                self[1].Radius = 12.5;
                self[1].Enabled = true;
            end;
            """));

        Assert.Empty(graph.DataEdges);
        // The values are still on the node - they're settings to show, just not wires to draw.
        Assert.Equal(3, graph.Nodes.Single(n => n.Id == "p:1").Params.Count);
    }

    [Fact]
    public void Keeps_control_edges_and_data_edges_in_separate_collections()
    {
        var graph = Build(WithBoxes("""
            function export:Init2(cbox)
                self[0].Out = self._type.f_0_Out;
            end;
            function export:Run()
                self.BuddyPawn = self[0].SpawnedBuddy;
            end;
            function export:f_0_Out()
                self = self._graph;
                self[1].Pawn = self.BuddyPawn;
                self[1]._type.Start(self[1]);
            end;
            """));

        GraphEdge control = Assert.Single(graph.Edges);
        Assert.Equal(EdgeTarget.Node, control.Target);
        Assert.Equal("Out", control.SourcePin);
        Assert.Equal("Start", control.TargetPin);

        DataEdge data = Assert.Single(graph.DataEdges);
        Assert.Equal("SpawnedBuddy", data.SourcePin);
        Assert.Equal("Pawn", data.TargetPin);
    }

    [Theory]
    [InlineData(DominoFixtures.FastTravel)]
    [InlineData(DominoFixtures.TaxiRide)]
    public void A_real_graphs_data_edges_connect_nodes_that_exist(string script)
    {
        if (Fixture.ReadText(script) is not { } source) return;

        ReconstructedGraph graph = Build(source);

        // Both are dense with data flow; none would mean the resolver silently stopped working.
        Assert.NotEmpty(graph.DataEdges);
        var ids = graph.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(graph.DataEdges, edge =>
        {
            Assert.Contains(edge.TargetNodeId, ids);
            Assert.True(edge.SourceNodeId is null || ids.Contains(edge.SourceNodeId));
        });
    }
}
