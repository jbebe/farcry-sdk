using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using Loretta.CodeAnalysis.Lua;

namespace JackAll.Tests;

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
    public void A_dynamic_fire_keeps_its_slot_when_written_in_blackbox_form()
    {
        var graph = Classify("""
            function export:f_5_Killed()
                self[7]._type.Condition(self[7], 1);
            end;
            """);

        string canonical = UserGraphWriter.WriteCanonical(graph);

        Assert.Contains("self[7]._type.Condition(self[7], 1);", canonical);
        var fire = Assert.IsType<FireControlInStmt>(Classify(canonical).Functions.Single().Body.Single());
        Assert.Equal(1, fire.Index);
    }

    [Fact]
    public void An_edited_statement_changes_only_its_own_line()
    {
        const string source = "function export:en_1()\r\n\t-- the bark\r\n\tself[1].GreetBark = \"GREET\";\r\n\tself[1].ExitBark_ = \"EXITA\";\r\nend;\r\n";
        UserGraph graph = Classify(source);
        UserGraphFunction fn = graph.Functions.Single();
        var edited = (SetParamStmt)fn.Body[1];
        UserGraph changed = graph with
        {
            Functions = [fn with { Body = [fn.Body[0], edited with { Value = SyntaxFactory.ParseExpression("\"EXITB\"") }] }],
        };

        Assert.Equal(source.Replace("EXITA", "EXITB"), UserGraphWriter.Write(changed));
    }

    [Theory]
    [InlineData(DominoFixtures.HealthEven)]
    [InlineData(DominoFixtures.FastTravelTwin)]
    public void A_real_graph_written_back_unchanged_is_byte_for_byte_the_source(string script)
    {
        if (Fixture.ReadText(script) is not { } source) return;

        Assert.Equal(source, UserGraphWriter.Write(Classify(source)));
    }

    [Theory]
    [InlineData(DominoFixtures.HealthEven)]
    [InlineData(DominoFixtures.FastTravelTwin)]
    public void A_real_graph_written_in_blackbox_form_has_the_same_tokens_as_the_source(string script)
    {
        if (Fixture.ReadText(script) is not { } source) return;

        string canonical = UserGraphWriter.WriteCanonical(Classify(source));

        Assert.Equal(Tokens(source), Tokens(canonical));
    }

    private static List<string> Tokens(string source) =>
        DominoLuaSource.Parse(source).DescendantTokens().Select(t => t.Text).ToList();
}
