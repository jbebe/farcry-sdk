using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Core.Tests;

public class UserGraphParserTests
{
    private static UserGraph Classify(string source) => UserGraphParser.Parse(DominoLuaSource.Parse(source));

    [Fact]
    public void Recognizes_register_box_in_create()
    {
        const string source = """
            function export:Create(cbox)
                cbox:RegisterBox("Domino/System/GetLocalPlayer.lua");
                cbox:RegisterBox("Domino/System/SetEntity.lua");
            end;
            """;

        var graph = Classify(source);

        var create = Assert.Single(graph.Functions, f => f.Name == "Create");
        Assert.Equal(
            [new RegisterBoxStmt("Domino/System/GetLocalPlayer.lua"), new RegisterBoxStmt("Domino/System/SetEntity.lua")],
            create.Body);
    }

    [Fact]
    public void Recognizes_the_full_pooled_box_configure_and_fire_sequence()
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

        var graph = Classify(source);
        var fn = Assert.Single(graph.Functions);

        var box = new PooledBoxRef("Domino/System/SetEntity.lua");

        Assert.IsType<RebindSelfToGraphStmt>(fn.Body[0]);
        var readData = Assert.IsType<ReadDataStmt>(fn.Body[1]);
        Assert.Equal(box, readData.Box);
        Assert.Equal("Target", readData.PinName);

        var setParam = Assert.IsType<SetParamStmt>(fn.Body[2]);
        Assert.Equal(box, setParam.Box);
        Assert.Equal("Entity", setParam.ParamName);

        var backref = Assert.IsType<SetGraphBackrefStmt>(fn.Body[3]);
        Assert.Equal(box, backref.Box);

        var wire = Assert.IsType<WireControlOutStmt>(fn.Body[4]);
        Assert.Equal(box, wire.Box);
        Assert.Equal("Out", wire.PinName);
        Assert.Equal("f_0_Out", wire.TargetHandler);
        Assert.Null(wire.Index);

        var fire = Assert.IsType<FireControlInStmt>(fn.Body[5]);
        Assert.Equal(box, fire.Box);
        Assert.Equal("FromEntity", fire.PinName);
    }

    [Fact]
    public void Recognizes_both_instance_box_forms()
    {
        var graph = Classify("""
            function export:Create(cbox)
                self[5] = cbox:CreateBox("Domino/System/SimpleNode.lua");
                self.box_HealthEvents_5 = cbox:CreateBox("Domino/System/HealthEvents.lua");
            end;
            """);

        var create = Assert.Single(graph.Functions);

        var numeric = Assert.IsType<CreateBoxStmt>(create.Body[0]);
        Assert.Equal(new InstanceBoxRef(5), numeric.Box);

        var named = Assert.IsType<CreateBoxStmt>(create.Body[1]);
        Assert.Equal(new NamedInstanceBoxRef("box_HealthEvents_5"), named.Box);
    }

    [Fact]
    public void Recognizes_dummy_function_as_an_unwired_pin()
    {
        var graph = Classify("function export:Init(cbox) self[5].Out = DummyFunction; end;");
        var wire = Assert.IsType<WireControlOutStmt>(Assert.Single(graph.Functions).Body[0]);
        Assert.Null(wire.TargetHandler);
    }

    [Fact]
    public void Recognizes_dynamic_indexed_control_out_wiring()
    {
        var graph = Classify("""
            function export:Init(cbox)
                self[218].Output[0] = self._type.f_218_Output_0;
                self[218].Output[1] = DummyFunction;
            end;
            """);

        var fn = Assert.Single(graph.Functions);

        var wired = Assert.IsType<WireControlOutStmt>(fn.Body[0]);
        Assert.Equal(0, wired.Index);
        Assert.Equal("f_218_Output_0", wired.TargetHandler);

        var unwired = Assert.IsType<WireControlOutStmt>(fn.Body[1]);
        Assert.Equal(1, unwired.Index);
        Assert.Null(unwired.TargetHandler);
    }

    [Fact]
    public void Recognizes_own_handler_calls_and_own_pin_fires()
    {
        var graph = Classify("""
            function export:f_0_Out()
                self._type.en_3(self);
                self:Out();
            end;
            """);

        var fn = Assert.Single(graph.Functions);

        var own = Assert.IsType<CallOwnHandlerStmt>(fn.Body[0]);
        Assert.Equal("en_3", own.HandlerName);

        var pin = Assert.IsType<FireOwnPinStmt>(fn.Body[1]);
        Assert.Equal("Out", pin.PinName);
    }

    [Fact]
    public void Recognizes_plain_graph_field_init()
    {
        var graph = Classify("""
            function export:Init(cbox)
                self.Merc01 = nil;
                self.WagerStart = 0;
            end;
            """);

        var fn = Assert.Single(graph.Functions);

        var f1 = Assert.IsType<SetGraphFieldStmt>(fn.Body[0]);
        Assert.Equal("Merc01", f1.FieldName);
        var nilValue = Assert.IsType<LiteralExpressionSyntax>(f1.Value);
        Assert.Equal(SyntaxKind.NilLiteralExpression, nilValue.Kind());

        var f2 = Assert.IsType<SetGraphFieldStmt>(fn.Body[1]);
        Assert.Equal("WagerStart", f2.FieldName);
    }

    [Fact]
    public void Real_corpus_statements_are_almost_entirely_classified()
    {
        string dir = Path.Combine("Fixtures", "Domino", "user");
        if (!Directory.Exists(dir)) return;

        var files = Directory.EnumerateFiles(dir, "*.lua", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count > 0, "Fixture corpus is present but empty.");

        long total = 0, other = 0;
        foreach (var file in files)
        {
            var graph = Classify(File.ReadAllText(file));
            foreach (var fn in graph.Functions)
            {
                foreach (var stmt in fn.Body)
                {
                    total++;
                    if (stmt is OtherStmt)
                    {
                        other++;
                    }
                }
            }
        }

        double unclassifiedFraction = total == 0 ? 0 : (double)other / total;
        Assert.True(unclassifiedFraction < 0.02,
            $"{other}/{total} ({unclassifiedFraction:P2}) statements unclassified - expected under 2%.");
    }
}
