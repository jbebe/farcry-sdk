using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;

namespace JackAll.Tests;

public class DebugTwinTests
{
    private static UserGraph Classify(string source) => UserGraphParser.Parse(DominoLuaSource.Parse(source));

    private static DominoDebugTwin? TwinOf(string source) => DominoDebugTwin.FromGraph(Classify(source));

    private const string Container =
        @"DocumentContainer|R:\\main\\data\\Domino\\User\\A1LM02_ReapSew.domino.xml|@A1LM02_BriefingSubvPawnBrief|1006789459";

    [Fact]
    public void Reads_a_traced_connection_including_the_document_graph_and_connection_id()
    {
        DominoDebugTwin? twin = TwinOf($$"""
            export = { };
            function export:Init(cbox)
                CDominoManager_GetInstance():TraceConnection("{{Container}}",
                    "box_SCRIPTEDPAWN_WAIT_BECKON_GREET_1.Greet finished",
                    "box_SCRIPTEDPAWN_DIALOG_INTERACT_2.Start",
                    self.box_SCRIPTEDPAWN_WAIT_BECKON_GREET_1, self.box_SCRIPTEDPAWN_DIALOG_INTERACT_2);
            end;
            """);

        Assert.NotNull(twin);
        Assert.Equal("A1LM02_BriefingSubvPawnBrief", twin.GraphName);
        Assert.Contains("A1LM02_ReapSew.domino.xml", twin.DocumentPath);

        TracedConnection connection = Assert.Single(twin.Connections);
        Assert.Equal("1006789459", connection.ConnectionId);
        Assert.Equal("box_SCRIPTEDPAWN_WAIT_BECKON_GREET_1", connection.SourceBox);
        Assert.Equal("Greet finished", connection.SourcePinLabel);
        Assert.Equal("box_SCRIPTEDPAWN_DIALOG_INTERACT_2", connection.TargetBox);
        Assert.Equal("Start", connection.TargetPinLabel);
    }

    [Fact]
    public void Maps_a_display_pin_label_to_the_identifier_the_generated_lua_uses()
    {
        Assert.Equal("Greet_finished", DominoDebugTwin.ToIdentifier("Greet finished"));
        Assert.Equal("Start", DominoDebugTwin.ToIdentifier("Start"));

        // Every character Lua won't accept in a name becomes an underscore, so a comma followed by a
        // space produces two - verified against `self[133].Free__if_this_pawn` in the retail scripts.
        Assert.Equal("Free__if_this_pawn", DominoDebugTwin.ToIdentifier("Free, if this pawn"));
        Assert.Equal("Started__to_CONVO", DominoDebugTwin.ToIdentifier("Started, to CONVO"));

        // A label starting with a digit can't be an identifier at all, so it gains a leading
        // underscore - verified against `self[47]._4a__Wager_finished__Buddy_healthy`.
        Assert.Equal("_2__Wager_started", DominoDebugTwin.ToIdentifier("2. Wager started"));
        Assert.Equal("_4a__Wager_finished__Buddy_healthy", DominoDebugTwin.ToIdentifier("4a. Wager finished, Buddy healthy"));
    }

    [Fact]
    public void Treats_an_unqualified_pin_label_as_one_of_the_graphs_own_pins()
    {
        DominoDebugTwin? twin = TwinOf($$"""
            export = { };
            function export:Init(cbox)
                CDominoManager_GetInstance():TraceConnection("{{Container}}",
                    "Start", "box_SetMissionBarkBankState_0.Load", self, self.box_SetMissionBarkBankState_0);
            end;
            """);

        TracedConnection connection = Assert.Single(twin!.Connections);
        Assert.Null(connection.SourceBox);
        Assert.Equal("Start", connection.SourcePinLabel);
        Assert.Equal("box_SetMissionBarkBankState_0", connection.TargetBox);
    }

    [Fact]
    public void Indexes_box_names_by_the_original_editor_id_in_their_suffix()
    {
        DominoDebugTwin? twin = TwinOf($$"""
            export = { };
            function export:Init(cbox)
                CDominoManager_GetInstance():TraceConnection("{{Container}}",
                    "box_Set_Entity_2.Out", "box_Simple_Node_0.In",
                    Boxes[PathID("Domino/System/SetEntity.lua")], Boxes[PathID("Domino/System/SimpleNode.lua")]);
            end;
            """);

        var names = twin!.BoxNamesById;
        Assert.Equal("box_Set_Entity_2", names[2]);
        Assert.Equal("box_Simple_Node_0", names[0]);
    }

    [Fact]
    public void Returns_null_for_a_file_that_carries_no_traced_connections()
    {
        Assert.Null(TwinOf("""
            export = { };
            function export:Init(cbox)
                self[0] = cbox:CreateBox("Domino/System/Delay.lua");
            end;
            """));
    }

    [Fact]
    public void Derives_a_graphs_twin_path_and_recognizes_one()
    {
        Assert.Equal(@"domino\user\a1bu00_tutorial.a1bu00_swap.debug.lua",
            DominoDebugTwin.TwinPathFor(@"domino\user\a1bu00_tutorial.a1bu00_swap.lua"));
        Assert.True(DominoDebugTwin.IsTwinPath(@"domino\user\a1bu00_tutorial.a1bu00_swap.debug.lua"));
        Assert.False(DominoDebugTwin.IsTwinPath(@"domino\user\a1bu00_tutorial.a1bu00_swap.lua"));
    }

    [Fact]
    public void Names_reconstructed_nodes_from_the_twins_box_names()
    {
        const string release = """
            export = { };
            function export:Init(cbox)
                self[0] = cbox:CreateBox("Domino/System/SetMissionBarkBankState.lua");
                self[0].Out = self._type.f_0_Out;
                self[1] = cbox:CreateBox("Domino/System/Delay.lua");
            end;
            function export:f_0_Out()
                self = self._graph;
                self[1]._type.Start(self[1]);
            end;
            """;
        DominoDebugTwin? twin = TwinOf($$"""
            export = { };
            function export:Init(cbox)
                CDominoManager_GetInstance():TraceConnection("{{Container}}",
                    "box_SetMissionBarkBankState_0.Out", "box_Delay_1.Start",
                    self.box_SetMissionBarkBankState_0, self.box_Delay_1);
            end;
            """);

        ReconstructedGraph graph = GraphBuilder.Build(Classify(release), catalog: null, twin);

        Assert.Equal("box_SetMissionBarkBankState_0", graph.Nodes.Single(n => n.Id == "p:0").OriginalName);
        Assert.Equal("box_Delay_1", graph.Nodes.Single(n => n.Id == "p:1").OriginalName);
    }

    [Fact]
    public void Validation_matches_a_reconstruction_against_its_twin()
    {
        const string release = """
            export = { };
            function export:Init(cbox)
                self[0] = cbox:CreateBox("Domino/System/SetMissionBarkBankState.lua");
                self[0].Out = self._type.f_0_Out;
                self[1] = cbox:CreateBox("Domino/System/Delay.lua");
            end;
            function export:f_0_Out()
                self = self._graph;
                self[1]._type.Start(self[1]);
            end;
            """;
        DominoDebugTwin? twin = TwinOf($$"""
            export = { };
            function export:Init(cbox)
                CDominoManager_GetInstance():TraceConnection("{{Container}}",
                    "box_SetMissionBarkBankState_0.Out", "box_Delay_1.Start",
                    self.box_SetMissionBarkBankState_0, self.box_Delay_1);
            end;
            """);

        ReconstructedGraph graph = GraphBuilder.Build(Classify(release), catalog: null, twin);
        TwinValidation result = DebugTwinValidator.Validate(graph, twin!);

        Assert.Equal(1, result.Matched);
        Assert.True(result.IsClean);
        Assert.Empty(result.Details);
    }

    [Fact]
    public void Validation_reports_a_connection_the_twin_has_but_the_reconstruction_missed()
    {
        // The release file wires nothing, so the traced connection has no counterpart.
        const string release = """
            export = { };
            function export:Init(cbox)
                self[0] = cbox:CreateBox("Domino/System/SetMissionBarkBankState.lua");
                self[0].Out = DummyFunction;
                self[1] = cbox:CreateBox("Domino/System/Delay.lua");
            end;
            """;
        DominoDebugTwin? twin = TwinOf($$"""
            export = { };
            function export:Init(cbox)
                CDominoManager_GetInstance():TraceConnection("{{Container}}",
                    "box_SetMissionBarkBankState_0.Out", "box_Delay_1.Start",
                    self.box_SetMissionBarkBankState_0, self.box_Delay_1);
            end;
            """);

        ReconstructedGraph graph = GraphBuilder.Build(Classify(release), catalog: null, twin);
        TwinValidation result = DebugTwinValidator.Validate(graph, twin!);

        Assert.False(result.IsClean);
        Assert.Equal(1, result.MissingFromReconstruction);
        Assert.Contains(result.Details, d => d.StartsWith("missing:", StringComparison.Ordinal));
    }

    /// <summary>The control edges <see cref="GraphBuilder"/> infers from a release file must match the
    /// connections the editor recorded in its twin. A mismatch is a reconstruction bug, not a test to
    /// loosen.</summary>
    [Theory]
    [InlineData(DominoFixtures.FastTravel)]
    [InlineData(DominoFixtures.TaxiRide)]
    public void A_reconstruction_agrees_with_its_debug_twin_on_box_to_box_control_flow(string release)
    {
        if (Fixture.ReadText(release) is not { } source
            || Fixture.ReadText(DominoDebugTwin.TwinPathFor(release)) is not { } twinSource) return;

        DominoDebugTwin? twin = DominoDebugTwin.FromGraph(Classify(twinSource));
        Assert.NotNull(twin);
        ReconstructedGraph graph = GraphBuilder.Build(Classify(source), catalog: null, twin);
        TwinValidation result = DebugTwinValidator.Validate(graph, twin);

        Assert.True(result.IsClean,
            $"-{result.MissingFromReconstruction} +{result.ExtraInReconstruction} ({result.Matched} matched):\n"
            + string.Join('\n', result.Details.Take(5)));
    }
}
