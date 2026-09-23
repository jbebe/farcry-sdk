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
            function export:f_box_SCRIPTEDPAWN_WAIT_BECKON_GREET_1_Greet_finished()
                CDominoManager_GetInstance():TraceConnection("{{Container}}",
                    "box_SCRIPTEDPAWN_WAIT_BECKON_GREET_1.Greet finished",
                    "box_SCRIPTEDPAWN_DIALOG_INTERACT_2.Start",
                    self.box_SCRIPTEDPAWN_WAIT_BECKON_GREET_1, self.box_SCRIPTEDPAWN_DIALOG_INTERACT_2);
                self.box_SCRIPTEDPAWN_DIALOG_INTERACT_2._type.Start(self.box_SCRIPTEDPAWN_DIALOG_INTERACT_2);
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
        Assert.Equal(0, connection.FireOrdinal);
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
            function export:Start()
                CDominoManager_GetInstance():TraceConnection("{{Container}}",
                    "Start", "box_SetMissionBarkBankState_0.Load", self, self.box_SetMissionBarkBankState_0);
                self.box_SetMissionBarkBankState_0._type.Load(self.box_SetMissionBarkBankState_0);
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
            function export:f_box_Set_Entity_2_Out()
                CDominoManager_GetInstance():TraceConnection("{{Container}}",
                    "box_Set_Entity_2.Out", "box_Simple_Node_0.In",
                    Boxes[PathID("Domino/System/SetEntity.lua")], Boxes[PathID("Domino/System/SimpleNode.lua")]);
                Boxes[PathID("Domino/System/SimpleNode.lua")]._type.In(Boxes[PathID("Domino/System/SimpleNode.lua")]);
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

    private const string Release = """
        export = { };
        function export:Init(cbox)
            self[0] = cbox:CreateBox("Domino/System/SetMissionBarkBankState.lua");
            self[0].Out = self._type.f_0_Out;
            self[1] = cbox:CreateBox("Domino/System/Delay.lua");
        end;
        function export:Start()
            self[0]._type.Load(self[0]);
        end;
        function export:f_0_Out()
            self = self._graph;
            self[1]._type.Start(self[1]);
        end;
        """;

    /// <summary><see cref="Release"/>'s debug twin, with <paramref name="delay"/> as the box the second
    /// trace names and <paramref name="fire"/> as the pin box 0's continuation fires.</summary>
    private static string Twin(string delay = "box_Delay_1", string fire = "Start") => $$"""
        export = { };
        function export:Init(cbox)
            self.box_SetMissionBarkBankState_0 = cbox:CreateBox("Domino/System/SetMissionBarkBankState.lua");
            self.box_SetMissionBarkBankState_0.Out = self._type.f_box_SetMissionBarkBankState_0_Out;
            self.box_Delay_1 = cbox:CreateBox("Domino/System/Delay.lua");
        end;
        function export:Start()
            CDominoManager_GetInstance():TraceConnection("{{Container}}",
                "Start", "box_SetMissionBarkBankState_0.Load", self, self.box_SetMissionBarkBankState_0);
            self.box_SetMissionBarkBankState_0._type.Load(self.box_SetMissionBarkBankState_0);
        end;
        function export:f_box_SetMissionBarkBankState_0_Out()
            self = self._graph;
            CDominoManager_GetInstance():TraceConnection("{{Container}}",
                "box_SetMissionBarkBankState_0.Out", "{{delay}}.Start",
                self.box_SetMissionBarkBankState_0, self.box_Delay_1);
            self.box_Delay_1._type.{{fire}}(self.box_Delay_1);
        end;
        """;

    private static TwinValidation Validate(string release, string twin) =>
        GraphBuilder.Build(Classify(release), catalog: null, TwinOf(twin)).Twin!;

    [Fact]
    public void Names_reconstructed_nodes_from_the_twins_box_names()
    {
        ReconstructedGraph graph = GraphBuilder.Build(Classify(Release), catalog: null, TwinOf(Twin()));

        Assert.Equal("box_SetMissionBarkBankState_0", graph.Nodes.Single(n => n.Id == "p:0").OriginalName);
        Assert.Equal("box_Delay_1", graph.Nodes.Single(n => n.Id == "p:1").OriginalName);
    }

    [Fact]
    public void Validation_matches_every_traced_fire_including_the_entry_pins()
    {
        TwinValidation result = Validate(Release, Twin());

        Assert.Equal(2, result.TracedFires);
        Assert.Equal(2, result.Matched);
        Assert.True(result.IsClean, string.Join('\n', result.Problems));
    }

    [Fact]
    public void Validation_reports_a_fire_the_twin_traces_to_another_box()
    {
        TwinValidation result = Validate(Release, Twin(delay: "box_Delay_2"));

        Assert.False(result.IsClean);
        Assert.Equal(1, result.Matched);
        Assert.Contains(result.Problems, p => p.StartsWith("disagrees:", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_refuses_a_twin_whose_code_differs_from_the_release_file()
    {
        TwinValidation result = Validate(Release, Twin(fire: "Stop"));

        Assert.Equal(0, result.Matched);
        Assert.Contains(result.Problems, p => p.Contains("differs from its twin", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pooled_box_nothing_else_names_takes_its_id_from_the_twin()
    {
        const string release = """
            export = { };
            function export:Start()
                Boxes[PathID("Domino/System/SetMalaria.lua")]._graph = self;
                Boxes[PathID("Domino/System/SetMalaria.lua")].Out = DummyFunction;
                Boxes[PathID("Domino/System/SetMalaria.lua")]._type.In(Boxes[PathID("Domino/System/SetMalaria.lua")]);
            end;
            """;
        string twin = $$"""
            export = { };
            function export:Start()
                Boxes[PathID("Domino/System/SetMalaria.lua")]._graph = self;
                Boxes[PathID("Domino/System/SetMalaria.lua")].Out = DummyFunction;
                CDominoManager_GetInstance():TraceConnection("{{Container}}",
                    "Start", "box_Set_Malaria_38.In", self, Boxes[PathID("Domino/System/SetMalaria.lua")]);
                Boxes[PathID("Domino/System/SetMalaria.lua")]._type.In(Boxes[PathID("Domino/System/SetMalaria.lua")]);
            end;
            """;

        ReconstructedGraph graph = GraphBuilder.Build(Classify(release), catalog: null, TwinOf(twin));

        GraphNode node = Assert.Single(graph.Nodes);
        Assert.Equal("q:38", node.Id);
        Assert.Equal(BoxIdSource.Twin, node.IdSource);
        Assert.Equal(1, graph.Twin!.NamedFromTwin);
    }

    /// <summary>Every fire the twin traces must be one of the reconstruction's edges, pooled boxes and
    /// entry pins included. A mismatch is a reconstruction bug, not a test to loosen.</summary>
    [Theory]
    [InlineData(DominoFixtures.FastTravel)]
    [InlineData(DominoFixtures.HealthEven)]
    [InlineData(DominoFixtures.SafehouseTut)]
    [InlineData(DominoFixtures.PrisonMission)]
    public void A_reconstruction_agrees_with_every_fire_its_debug_twin_traces(string release)
    {
        if (Fixture.ReadText(release) is not { } source
            || Fixture.ReadText(DominoDebugTwin.TwinPathFor(release)) is not { } twinSource) return;

        TwinValidation result = Validate(source, twinSource);

        Assert.True(result.IsClean, string.Join('\n', result.Problems.Take(5)));
        Assert.Equal(result.TracedFires, result.Matched);
    }
}
