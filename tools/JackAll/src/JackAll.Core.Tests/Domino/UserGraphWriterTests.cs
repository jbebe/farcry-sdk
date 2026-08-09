using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;

namespace JackAll.Core.Tests;

public class UserGraphWriterTests
{
    private static UserGraph Classify(string source) => UserGraphParser.Parse(DominoLuaSource.Parse(source));

    [Fact]
    public void Round_trips_the_full_pooled_box_configure_and_fire_sequence()
    {
        const string source = """
            function export:f_1_Out()
                self = self._graph;
                self.Door = Boxes[PathID("Domino/System/SetEntity.lua")].Target;
                Boxes[PathID("Domino/System/SetEntity.lua")].Entity = "2056828857211684604";
                Boxes[PathID("Domino/System/SetEntity.lua")]._graph = self;
                Boxes[PathID("Domino/System/SetEntity.lua")].Out = self._type.f_0_Out;
                Boxes[PathID("Domino/System/SetEntity.lua")]._type.FromEntity(Boxes[PathID("Domino/System/SetEntity.lua")]);
            end;
            """;

        var graph1 = Classify(source);
        string generated1 = UserGraphWriter.Write(graph1);

        var graph2 = Classify(generated1);
        string generated2 = UserGraphWriter.Write(graph2);

        Assert.Equal(generated1, generated2);

        var fn = Assert.Single(graph2.Functions);
        Assert.Equal(6, fn.Body.Count);
        Assert.IsType<RebindSelfToGraphStmt>(fn.Body[0]);
        Assert.IsType<ReadDataStmt>(fn.Body[1]);
        Assert.IsType<SetParamStmt>(fn.Body[2]);
        Assert.IsType<SetGraphBackrefStmt>(fn.Body[3]);
        Assert.IsType<WireControlOutStmt>(fn.Body[4]);
        Assert.IsType<FireControlInStmt>(fn.Body[5]);
    }

    [Fact]
    public void Round_trips_dynamic_indexed_wiring_and_dummy_function()
    {
        const string source = """
            function export:Init(cbox)
                self[218].Output[0] = self._type.f_218_Output_0;
                self[218].Output[1] = DummyFunction;
            end;
            """;

        var graph1 = Classify(source);
        var graph2 = Classify(UserGraphWriter.Write(graph1));
        var fn = Assert.Single(graph2.Functions);

        var wired = Assert.IsType<WireControlOutStmt>(fn.Body[0]);
        Assert.Equal(0, wired.Index);
        Assert.Equal("f_218_Output_0", wired.TargetHandler);

        var unwired = Assert.IsType<WireControlOutStmt>(fn.Body[1]);
        Assert.Equal(1, unwired.Index);
        Assert.Null(unwired.TargetHandler);
    }

    [Fact]
    public void Round_trips_both_instance_box_forms_and_registered_dependencies()
    {
        const string source = """
            function export:Create(cbox)
                cbox:RegisterBox("Domino/System/SetEntity.lua");
                self[5] = cbox:CreateBox("Domino/System/SimpleNode.lua");
                self.box_HealthEvents_5 = cbox:CreateBox("Domino/System/HealthEvents.lua");
            end;
            """;

        var graph1 = Classify(source);
        var graph2 = Classify(UserGraphWriter.Write(graph1));
        var fn = Assert.Single(graph2.Functions);

        Assert.IsType<RegisterBoxStmt>(fn.Body[0]);
        var numeric = Assert.IsType<CreateBoxStmt>(fn.Body[1]);
        Assert.Equal(new InstanceBoxRef(5), numeric.Box);
        var named = Assert.IsType<CreateBoxStmt>(fn.Body[2]);
        Assert.Equal(new NamedInstanceBoxRef("box_HealthEvents_5"), named.Box);
    }

    [Fact]
    public void Round_trips_own_handler_calls_own_pin_fires_and_graph_field_init()
    {
        const string source = """
            function export:Init(cbox)
                self.Merc01 = nil;
                self.WagerStart = 0;
            end;

            function export:f_0_Out()
                self._type.en_3(self);
                self:Out();
            end;
            """;

        var graph1 = Classify(source);
        var graph2 = Classify(UserGraphWriter.Write(graph1));

        var init = graph2.Functions.Single(f => f.Name == "Init");
        Assert.IsType<SetGraphFieldStmt>(init.Body[0]);
        Assert.IsType<SetGraphFieldStmt>(init.Body[1]);

        var handler = graph2.Functions.Single(f => f.Name == "f_0_Out");
        Assert.IsType<CallOwnHandlerStmt>(handler.Body[0]);
        Assert.IsType<FireOwnPinStmt>(handler.Body[1]);
    }

    [Fact]
    public void A_graph_reconstructed_from_the_written_text_matches_node_and_edge_counts()
    {
        const string source = """
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
            """;

        var graph1 = Classify(source);
        var reconstructed1 = GraphBuilder.Build(graph1);

        var graph2 = Classify(UserGraphWriter.Write(graph1));
        var reconstructed2 = GraphBuilder.Build(graph2);

        Assert.Equal(reconstructed1.Nodes.Count, reconstructed2.Nodes.Count);
        Assert.Equal(reconstructed1.Edges.Count, reconstructed2.Edges.Count);
        Assert.Equal(reconstructed1.Edges.Single().Target, reconstructed2.Edges.Single().Target);
    }

    [Fact]
    public void Every_real_extracted_user_graph_round_trips_stably_through_the_writer()
    {
        if (DominoCorpus.UserDirectory is not { } dir) return;

        var files = Directory.EnumerateFiles(dir, "*.lua", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count > 0, "Fixture corpus is present but empty.");

        var failures = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var graph1 = Classify(File.ReadAllText(file));
                string generated1 = UserGraphWriter.Write(graph1);
                var graph2 = Classify(generated1);
                string generated2 = UserGraphWriter.Write(graph2);

                if (generated1 != generated2)
                {
                    failures.Add($"{file}: unstable round trip");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{file}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count}/{files.Count} files failed:\n" + string.Join('\n', failures.Take(10)));
    }
}
