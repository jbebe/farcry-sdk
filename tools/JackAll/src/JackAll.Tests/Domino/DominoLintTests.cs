using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.World;

namespace JackAll.Tests;

public class DominoLintTests
{
    /// <summary>A system node with the given reflection lines, and `Stateless` unless told otherwise.</summary>
    private static string Node(string name, string pins, bool stateless = true) => $"""
        -- DOMINO REFLECTION BOX START
        --
        -- <Display Category="Script Flow" Text="{name}"/>
        --
        {pins}
        --
        {(stateless ? "-- <Stateless/>" : "--")}
        --
        -- DOMINO REFLECTION BOX END
        export = {"{ }"};
        """;

    private static readonly DominoNodeCatalog Catalog = new(path => path switch
    {
        @"domino\system\delay.lua" => Node("Delay", """
            -- <ControlIn  Name="Start"/>
            -- <DataIn     Name="Seconds"      Type="Core|float"/>
            -- <ControlOut Name="TimeElapsed"/>
            """),
        @"domino\system\counter.lua" => Node("Counter", """
            -- <ControlIn  Name="Increment"/>
            -- <ControlOut Name="Out"/>
            """, stateless: false),
        @"domino\system\multipleand.lua" => Node("MultipleAND", """
            -- <ControlIn  Name="Condition" Dynamic="True"/>
            -- <ControlOut Name="Out"/>
            """),
        _ => null,
    });

    /// <summary>A graph that registers every node type and creates a Delay as box 1, plus
    /// <paramref name="body"/>.</summary>
    private static string Graph(string body) => $$"""
        function export:Create(cbox)
            cbox:RegisterBox("Domino/System/Delay.lua");
            cbox:RegisterBox("Domino/System/Counter.lua");
            cbox:RegisterBox("Domino/System/MultipleAND.lua");
        end;
        function export:Init(cbox)
            self[1] = cbox:CreateBox("Domino/System/Delay.lua");
            self[1].TimeElapsed = DummyFunction;
        end;
        {{body}}
        """;

    private static IReadOnlyList<DominoFinding> Lint(string source) =>
        DominoCheck.Run(source, twinSource: null, Catalog).Findings;

    private static DominoFinding Single(string source, string rule) =>
        Assert.Single(Lint(source), f => f.Rule == rule);

    [Fact]
    public void A_graph_BlackBox_could_have_written_has_no_findings()
    {
        Assert.Empty(Lint(Graph("""
            function export:Start()
                self[1].Seconds = 2.5;
                self[1]._type.Start(self[1]);
            end;
            """)));
    }

    [Fact]
    public void Firing_a_control_in_the_box_does_not_declare_is_an_error()
    {
        DominoFinding finding = Single(Graph("""
            function export:Start()
                self[1]._type.Stop(self[1]);
            end;
            """), "undeclared-in");

        Assert.Equal(LintSeverity.Error, finding.Severity);
    }

    [Fact]
    public void Wiring_a_control_out_the_box_does_not_declare_is_a_warning()
    {
        DominoFinding finding = Single(Graph("""
            function export:Start()
                self[1].Finished = self._type.f_1_Finished;
            end;
            function export:f_1_Finished()
            end;
            """), "undeclared-out");

        Assert.Equal(LintSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void A_parameter_that_is_not_a_data_in_is_a_warning()
    {
        Single(Graph("""
            function export:Start()
                self[1].Minutes = 3;
            end;
            """), "undeclared-param");
    }

    [Fact]
    public void A_literal_of_the_wrong_kind_for_its_data_in_is_a_warning()
    {
        Single(Graph("""
            function export:Start()
                self[1].Seconds = "soon";
            end;
            """), "literal-type");
    }

    [Fact]
    public void A_pooled_box_that_leaves_a_data_in_unset_is_a_warning()
    {
        DominoFinding finding = Single(Graph("""
            function export:Start()
                Boxes[PathID("Domino/System/Delay.lua")]._graph = self;
                Boxes[PathID("Domino/System/Delay.lua")].TimeElapsed = self._type.f_4_TimeElapsed;
                Boxes[PathID("Domino/System/Delay.lua")]._type.Start(Boxes[PathID("Domino/System/Delay.lua")]);
            end;
            function export:f_4_TimeElapsed()
            end;
            """), "stale-slot");

        Assert.Contains("Seconds", finding.Message);
    }

    [Fact]
    public void A_pooled_box_that_keeps_state_is_an_error()
    {
        Single(Graph("""
            function export:Start()
                Boxes[PathID("Domino/System/Counter.lua")]._graph = self;
                Boxes[PathID("Domino/System/Counter.lua")].Out = self._type.f_4_Out;
                Boxes[PathID("Domino/System/Counter.lua")]._type.Increment(Boxes[PathID("Domino/System/Counter.lua")]);
            end;
            function export:f_4_Out()
            end;
            """), "stateful-pooled");
    }

    /// <summary>A MultipleAND with two Condition slots, fired on <paramref name="slot"/> by the entry pin.</summary>
    private static string MultipleAnd(int slot) => Graph($$"""
        function export:Setup(cbox)
            self[7] = cbox:CreateBox("Domino/System/MultipleAND.lua");
            self[7]._DynamicAnchors = {
                Condition = 2,
            };
            self[7].Out = DummyFunction;
        end;
        function export:Start()
            self[7]._type.Condition(self[7], {{slot}});
        end;
        """);

    [Fact]
    public void Firing_a_dynamic_slot_the_box_does_not_have_is_an_error()
    {
        Single(MultipleAnd(slot: 2), "dynamic-index-range");
    }

    [Fact]
    public void A_dynamic_slot_nothing_fires_is_a_warning()
    {
        DominoFinding finding = Single(MultipleAnd(slot: 0), "unused-slot");

        Assert.Contains("slot 1", finding.Message);
    }

    [Fact]
    public void A_box_type_missing_from_create_is_an_error()
    {
        Single("""
            function export:Init(cbox)
                self[1] = cbox:CreateBox("Domino/System/Delay.lua");
            end;
            """, "unregistered-box");
    }

    [Fact]
    public void A_control_out_the_graph_never_fires_is_reported()
    {
        DominoFinding finding = Single("""
            function export:Init(cbox)
                self.Done = DummyFunction;
            end;
            function export:Done()
            end;
            """, "unfired-out-anchor");

        Assert.Equal(LintSeverity.Info, finding.Severity);
    }

    [Fact]
    public void A_debug_twin_that_no_longer_matches_is_reported()
    {
        string release = Graph("""
            function export:Start()
                self[1].Seconds = 2.5;
                self[1]._type.Start(self[1]);
            end;
            """);
        string twin = Graph("""
            function export:Start()
                self[1].Seconds = 5;
                CDominoManager_GetInstance():TraceConnection("c", "Start", "box_Delay_1.Start", self, self[1]);
                self[1]._type.Start(self[1]);
            end;
            """);

        var findings = DominoCheck.Run(release, twin, Catalog).Findings;

        Assert.Contains(findings, f => f.Rule == "stale-twin");
    }

    [Fact]
    public void A_mods_graph_is_told_only_about_what_it_added()
    {
        DominoCheckResult retail = DominoCheck.Run(Graph("""
            function export:Start()
                self[1].Minutes = 3;
            end;
            """), null, Catalog);
        DominoCheckResult edited = DominoCheck.Run(Graph("""
            function export:Start()
                self[1].Minutes = 3;
                self[1]._type.Stop(self[1]);
            end;
            """), null, Catalog);

        Assert.Equal("undeclared-in", Assert.Single(DominoCheck.NewSince(edited, retail)).Rule);
    }
}
