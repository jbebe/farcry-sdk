using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.Tests;

public class DominoValueRefsTests
{
    private static ReconstructedGraph BuildFrom(string source, DominoNodeCatalog? catalog = null) =>
        GraphBuilder.Build(UserGraphParser.Parse(DominoLuaSource.Parse(source)), catalog);

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

    [Fact]
    public void Every_real_sound_literal_is_a_sound_id_and_every_graph_value_names_a_real_graph()
    {
        if (DominoCorpus.UserDirectory is not { } userDir || DominoCorpus.SystemDirectory is not { } systemDir) return;

        var userGraphs = Directory.EnumerateFiles(userDir, "*.lua", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(userDir, f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalog = new DominoNodeCatalog(path =>
        {
            string name = Path.GetFileName(path);
            string candidate = Path.Combine(systemDir, name);
            if (!File.Exists(candidate))
            {
                candidate = Path.Combine(userDir, name);
            }
            return File.Exists(candidate) ? File.ReadAllText(candidate) : null;
        });

        var failures = new List<string>();
        int sounds = 0;
        foreach (string file in Directory.EnumerateFiles(userDir, "*.lua").Where(f => !DominoDebugTwin.IsTwinPath(f)))
        {
            ReconstructedGraph graph = BuildFrom(File.ReadAllText(file), catalog);
            foreach (GraphNode node in graph.Nodes)
            {
                var refs = DominoValueRefs.For(node, graph);
                foreach (DataInPin pin in node.Signature?.DataIns ?? [])
                {
                    bool literal = node.Params.TryGetValue(pin.Name, out var expr) && DominoValueRefs.Resolve(expr, graph) is not null;
                    if (literal && pin.Type == "Nomad|Sound" && !refs.Any(r => r.Pin == pin.Name && r.Kind == ValueRefKind.Sound))
                    {
                        failures.Add($"{Path.GetFileName(file)}: {node.Id}.{pin.Name} = {DominoExprPreview.Full(expr!)} is not a sound id");
                    }
                }

                sounds += refs.Count(r => r.Kind == ValueRefKind.Sound);
                foreach (ValueRef graphRef in refs.Where(r => r.Kind == ValueRefKind.Graph))
                {
                    if (!userGraphs.Contains(DominoNodeCatalog.ToVfsPath(graphRef.Value)[@"domino\user\".Length..]))
                    {
                        failures.Add($"{Path.GetFileName(file)}: {graphRef.Value} is not a user graph");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures.Take(20)));
        Assert.True(sounds > 0 || userGraphs.Count < 20, "No sound values found in the corpus.");
    }
}
