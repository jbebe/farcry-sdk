using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Core.Tests;

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

    [Fact]
    public void Builds_one_node_per_pooled_configure_and_fire_occurrence()
    {
        var graph = BuildFrom("""
            function export:f_1_Out()
                self = self._graph;
                Boxes[PathID("Domino/System/SetEntity.lua")].Entity = "123";
                Boxes[PathID("Domino/System/SetEntity.lua")]._graph = self;
                Boxes[PathID("Domino/System/SetEntity.lua")].Out = self._type.f_0_Out;
                Boxes[PathID("Domino/System/SetEntity.lua")]._type.FromEntity(Boxes[PathID("Domino/System/SetEntity.lua")]);
            end;

            function export:f_0_Out()
            end;
            """);

        var node = Assert.Single(graph.Nodes);
        Assert.Equal(BoxInstanceKind.Pooled, node.Kind);
        Assert.Equal("Domino/System/SetEntity.lua", node.NodeTypePath);
        Assert.Equal("123", StringValue(node.Params["Entity"]));

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(EdgeTarget.DeadEnd, edge.Target); // f_0_Out fires nothing further
    }

    [Fact]
    public void The_same_pooled_path_in_two_functions_becomes_two_separate_nodes()
    {
        var graph = BuildFrom("""
            function export:f_0_Out()
                Boxes[PathID("Domino/System/X.lua")]._type.In(Boxes[PathID("Domino/System/X.lua")]);
            end;

            function export:f_1_Out()
                Boxes[PathID("Domino/System/X.lua")]._type.In(Boxes[PathID("Domino/System/X.lua")]);
            end;
            """);

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Equal(2, graph.Nodes.Select(n => n.Id).Distinct().Count());
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
        // Confirmed common in the real corpus: ~30% of functions fire more than one thing in sequence.
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

    [Fact]
    public void Real_corpus_graphs_build_without_error_and_mostly_resolve_to_real_targets()
    {
        if (DominoCorpus.UserDirectory is not { } dir) return;

        var files = Directory.EnumerateFiles(dir, "*.lua", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count > 0, "Fixture corpus is present but empty.");

        long totalEdges = 0, nodeEdges = 0;
        var failures = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var graph = BuildFrom(File.ReadAllText(file));
                totalEdges += graph.Edges.Count;
                nodeEdges += graph.Edges.Count(e => e.Target == EdgeTarget.Node);
            }
            catch (Exception ex)
            {
                failures.Add($"{file}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count}/{files.Count} files failed:\n" + string.Join('\n', failures.Take(10)));

        double nodeFraction = totalEdges == 0 ? 0 : (double)nodeEdges / totalEdges;
        Assert.True(nodeFraction > 0.5, $"Only {nodeFraction:P2} of edges resolved to a real node - expected over 50%.");
    }
}
