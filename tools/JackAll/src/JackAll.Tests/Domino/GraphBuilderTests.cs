using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tests;

public class GraphBuilderTests
{
    private static ReconstructedGraph BuildFrom(string source) =>
        GraphBuilder.Build(UserGraphParser.Parse(DominoLuaSource.Parse(source)));

    private static string StringValue(ExpressionSyntax expr)
    {
        var lit = Assert.IsType<LiteralExpressionSyntax>(expr);
        Assert.Equal(SyntaxKind.StringLiteralExpression, lit.Kind());
        return lit.Token.ValueText;
    }

    private const string SetEntity = "Boxes[PathID(\"Domino/System/SetEntity.lua\")]";

    /// <summary>Two pooled SetEntity boxes in a chain, as healtheven has them: the entry pin fires box 2,
    /// whose continuation reads box 2's output and then configures and fires box 1 on the same slot.</summary>
    private const string PooledChain = $$"""
        function export:Init(cbox)
            self[5] = cbox:CreateBox("Domino/System/HealthEvents.lua");
        end;

        function export:Start()
            {{SetEntity}}.Entity = "10";
            {{SetEntity}}._graph = self;
            {{SetEntity}}.Out = self._type.f_2_Out;
            {{SetEntity}}._type.FromEntity({{SetEntity}});
        end;

        function export:f_2_Out()
            self = self._graph;
            self.Merc01 = {{SetEntity}}.Target;
            {{SetEntity}}.Entity = "20";
            {{SetEntity}}._graph = self;
            {{SetEntity}}.Out = self._type.f_1_Out;
            {{SetEntity}}._type.FromEntity({{SetEntity}});
        end;

        function export:f_1_Out()
            self = self._graph;
            self.Merc02 = {{SetEntity}}.Target;
            self[5].Pawn = self.Merc01;
            self[5]._type.Enable(self[5]);
        end;
        """;

    [Fact]
    public void A_pooled_box_is_the_editor_box_its_control_out_is_wired_to()
    {
        var graph = BuildFrom(PooledChain);

        GraphNode first = graph.Nodes.Single(n => n.Id == "q:2");
        GraphNode second = graph.Nodes.Single(n => n.Id == "q:1");
        Assert.Equal(BoxIdSource.Wire, first.IdSource);
        Assert.Equal("10", StringValue(first.Params["Entity"]));
        Assert.Equal("20", StringValue(second.Params["Entity"]));

        // Reading the slot in a continuation is the box that just fired, not another box.
        Assert.Equal(3, graph.Nodes.Count);
        Assert.Empty(graph.Findings);
    }

    [Fact]
    public void A_continuations_read_is_credited_to_the_box_that_fired_it()
    {
        var graph = BuildFrom(PooledChain);

        // f_2_Out reads Merc01 before it reconfigures the slot as box 1, so the value is box 2's.
        DataEdge edge = Assert.Single(graph.DataEdges);
        Assert.Equal("q:2", edge.SourceNodeId);
        Assert.Equal("p:5", edge.TargetNodeId);
    }

    [Fact]
    public void An_entry_pin_is_the_source_of_what_it_fires()
    {
        var graph = BuildFrom(PooledChain);

        GraphEdge entry = Assert.Single(graph.Edges, e => e.FromEntry);
        Assert.Equal("Start", entry.SourcePin);
        Assert.Equal("q:2", entry.TargetNodeId);
        Assert.Equal("FromEntity", entry.TargetPin);
        Assert.Contains(graph.Edges, e => e.SourceNodeId == "q:2" && e.TargetNodeId == "q:1");
    }

    [Fact]
    public void A_prologue_configures_the_box_its_callers_fire()
    {
        var graph = BuildFrom("""
            function export:Init(cbox)
                self[1] = cbox:CreateBox("Domino/System/SetEntity.lua");
                self[1].Out = self._type.f_1_Out;
                self[3] = cbox:CreateBox("Domino/System/SetEntity.lua");
                self[3].Out = self._type.f_3_Out;
            end;

            function export:f_1_Out()
                self._type.en_0(self);
                Boxes[PathID("Domino/System/SimpleNode.lua")]._type.In(Boxes[PathID("Domino/System/SimpleNode.lua")]);
            end;

            function export:f_3_Out()
                self._type.en_0(self);
                Boxes[PathID("Domino/System/SimpleNode.lua")]._type.In(Boxes[PathID("Domino/System/SimpleNode.lua")]);
            end;

            function export:en_0()
                Boxes[PathID("Domino/System/SimpleNode.lua")]._graph = self;
                Boxes[PathID("Domino/System/SimpleNode.lua")].Out = DummyFunction;
            end;
            """);

        GraphNode simple = Assert.Single(graph.Nodes, n => n.Kind == BoxInstanceKind.Pooled);
        Assert.Equal("q:0", simple.Id);
        Assert.Equal(BoxIdSource.Prologue, simple.IdSource);
        Assert.Equal(["p:1", "p:3"], graph.Edges.Where(e => e.TargetNodeId == "q:0").Select(e => e.SourceNodeId).Order());
        Assert.Single(graph.Edges, e => e.SourceNodeId == "q:0" && e.Target == EdgeTarget.Unwired);
    }

    [Fact]
    public void A_dynamic_control_in_keeps_the_slot_it_is_fired_on()
    {
        var graph = BuildFrom("""
            function export:Init(cbox)
                self[5] = cbox:CreateBox("Domino/System/HealthEvents.lua");
                self[5].Killed = self._type.f_5_Killed;
                self[7] = cbox:CreateBox("Domino/System/MultipleAND.lua");
                self[7]._DynamicAnchors = {
                    Condition = 2,
                };
            end;

            function export:f_5_Killed()
                self[7]._type.Condition(self[7], 1);
            end;
            """);

        GraphEdge edge = Assert.Single(graph.Edges, e => e.Target == EdgeTarget.Node);
        Assert.Equal("Condition", edge.TargetPin);
        Assert.Equal(1, edge.TargetIndex);
        Assert.Equal(2, graph.Nodes.Single(n => n.Id == "p:7").DynamicSlots["Condition"]);
    }

    [Fact]
    public void A_pooled_fire_nothing_names_is_reported()
    {
        var graph = BuildFrom("""
            function export:Start()
                Boxes[PathID("Domino/System/X.lua")]._type.In(Boxes[PathID("Domino/System/X.lua")]);
            end;
            """);

        Assert.Equal("#?", Assert.Single(graph.Nodes).InstanceLabel);
        Assert.Contains(graph.Findings, f => f.Rule == "bare-fire");
    }

    [Fact]
    public void A_pooled_read_no_fire_explains_is_reported()
    {
        var graph = BuildFrom("""
            function export:Start()
                self.Who = Boxes[PathID("Domino/System/X.lua")].Target;
            end;
            """);

        Assert.Empty(graph.Nodes);
        Assert.Contains(graph.Findings, f => f.Rule == "unbound-read");
    }

    [Fact]
    public void A_wire_to_a_handler_that_does_not_exist_is_reported()
    {
        var graph = BuildFrom("""
            function export:Init(cbox)
                self[1] = cbox:CreateBox("Domino/System/Delay.lua");
                self[1].TimeElapsed = self._type.f_1_TimeElapsed;
            end;
            """);

        Assert.Equal(EdgeTarget.DeadEnd, Assert.Single(graph.Edges).Target);
        Assert.Contains(graph.Findings, f => f.Rule == "undefined-handler");
    }

    [Fact]
    public void Init_literals_become_variable_defaults_but_out_anchors_and_boxes_do_not()
    {
        var graph = BuildFrom("""
            function export:Init()
                self.TheBigTruck = "2053887370209530241";
                self.Finished = DummyFunction;
                self.box_Delay_3 = cbox:CreateBox("Domino/System/Delay.lua");
            end;

            function export:f_1_Out()
                self.Later = "not a default";
            end;
            """);

        Assert.Equal("\"2053887370209530241\"", graph.VariableDefaults["TheBigTruck"]);
        Assert.Single(graph.VariableDefaults);
    }

    [Fact]
    public void An_instance_label_drops_the_editor_name_decoration()
    {
        var graph = BuildFrom("""
            function export:Init()
                self.box_BRIEFING_SUBVERT_7 = cbox:CreateBox("Domino/System/Delay.lua");
            end;
            """);

        Assert.Equal("BRIEFING_SUBVERT  ·  #7", Assert.Single(graph.Nodes).InstanceLabel);
    }

    [Fact]
    public void A_persistent_box_is_a_single_node_referenced_by_id_across_functions()
    {
        var graph = BuildFrom("""
            function export:Create(cbox)
                self[5] = cbox:CreateBox("Domino/System/SetEntity.lua");
            end;

            function export:Init(cbox)
                self[5].Entity = "abc";
            end;

            function export:ShutDown()
                self[5]._type.ShutDown(self[5]);
            end;
            """);

        // self[5] is touched from Create, Init, and ShutDown, but must still resolve to one node.
        var node = Assert.Single(graph.Nodes);
        Assert.Equal(BoxInstanceKind.Persistent, node.Kind);
        Assert.Equal("p:5", node.Id);
        Assert.Equal("abc", StringValue(node.Params["Entity"]));

        // Nothing wires *into* ShutDown here (it's an engine lifecycle hook, not a pin target), so
        // there's no WireControlOutStmt to produce an edge from.
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Dummy_function_wiring_produces_an_unwired_edge()
    {
        var graph = BuildFrom("""
            function export:Create(cbox)
                self[5] = cbox:CreateBox("Domino/System/OutputOrder.lua");
            end;

            function export:Init(cbox)
                self[5].Out = DummyFunction;
            end;
            """);

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(EdgeTarget.Unwired, edge.Target);
        Assert.Null(edge.TargetNodeId);
    }

    [Fact]
    public void Wiring_that_reaches_a_box_fire_resolves_to_a_node_edge()
    {
        var graph = BuildFrom("""
            function export:Create(cbox)
                self[1] = cbox:CreateBox("Domino/System/A.lua");
                self[2] = cbox:CreateBox("Domino/System/B.lua");
            end;

            function export:Init(cbox)
                self[1].Out = self._type.f_0_Out;
            end;

            function export:f_0_Out()
                self[2]._type.In(self[2]);
            end;
            """);

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(EdgeTarget.Node, edge.Target);
        Assert.Equal("p:2", edge.TargetNodeId);
        Assert.Equal("In", edge.TargetPin);
    }

    [Fact]
    public void A_handler_that_fires_multiple_things_fans_out_into_multiple_edges()
    {
        // Confirmed common in the retail scripts: ~30% of functions fire more than one thing in sequence.
        var graph = BuildFrom("""
            function export:Create(cbox)
                self[1] = cbox:CreateBox("Domino/System/Source.lua");
                self[2] = cbox:CreateBox("Domino/System/A.lua");
                self[3] = cbox:CreateBox("Domino/System/B.lua");
            end;

            function export:Init(cbox)
                self[1].Out = self._type.f_0_Out;
            end;

            function export:f_0_Out()
                self[2]._type.In(self[2]);
                self[3]._type.In(self[3]);
            end;
            """);

        var edges = graph.Edges.Where(e => e.SourceNodeId == "p:1").ToList();
        Assert.Equal(2, edges.Count);
        Assert.Contains(edges, e => e.TargetNodeId == "p:2");
        Assert.Contains(edges, e => e.TargetNodeId == "p:3");
    }

    [Fact]
    public void A_call_to_an_own_handler_does_not_swallow_statements_that_follow_it()
    {
        // This is exactly the bug the fan-out redesign fixed: a wire into a function that calls a
        // helper (en_N-style) partway through must still see whatever that function fires afterward.
        var graph = BuildFrom("""
            function export:Create(cbox)
                self[1] = cbox:CreateBox("Domino/System/Source.lua");
                self[2] = cbox:CreateBox("Domino/System/Target.lua");
            end;

            function export:Init(cbox)
                self[1].Out = self._type.f_0_Out;
            end;

            function export:f_0_Out()
                self._type.en_9(self);
                self[2]._type.In(self[2]);
            end;

            function export:en_9()
                self[1].Command = "SomeParam";
            end;
            """);

        var edge = Assert.Single(graph.Edges, e => e.SourceNodeId == "p:1");
        Assert.Equal(EdgeTarget.Node, edge.Target);
        Assert.Equal("p:2", edge.TargetNodeId);
    }

    [Fact]
    public void Firing_own_exposed_pin_resolves_to_a_graph_exit_edge()
    {
        var graph = BuildFrom("""
            function export:Create(cbox)
                self[1] = cbox:CreateBox("Domino/System/Source.lua");
            end;

            function export:Init(cbox)
                self[1].Out = self._type.f_0_Out;
            end;

            function export:f_0_Out()
                self:Finished();
            end;
            """);

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(EdgeTarget.GraphExit, edge.Target);
        Assert.Equal("Finished", edge.GraphExitPin);
    }

    [Fact]
    public void A_node_pointing_at_a_user_graph_path_is_flagged_as_a_sub_graph()
    {
        var graph = BuildFrom("""
            function export:Create(cbox)
                self[1] = cbox:CreateBox("Domino/User/A1BU03_DunkCage.A1BU03_Mission.lua");
            end;
            """);

        Assert.True(Assert.Single(graph.Nodes).IsSubGraph);
    }

    [Theory]
    [InlineData(DominoFixtures.FastTravel)]
    [InlineData(DominoFixtures.TaxiRide)]
    public void A_real_graphs_edges_mostly_resolve_to_real_targets(string script)
    {
        if (Fixture.ReadText(script) is not { } source) return;

        var graph = BuildFrom(source);

        double nodeFraction = (double)graph.Edges.Count(e => e.Target == EdgeTarget.Node) / graph.Edges.Count;
        Assert.True(nodeFraction > 0.5, $"Only {nodeFraction:P2} of edges resolved to a real node - expected over 50%.");
    }
}
