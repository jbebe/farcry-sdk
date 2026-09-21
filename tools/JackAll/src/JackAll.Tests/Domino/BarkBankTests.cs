using JackAll.Tools.Bark;
using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.Spk;

namespace JackAll.Tests;

public class BarkBankTests
{
    // Bark data is split across these archives' roots in the retail export.
    private static byte[]? ReadExported(string path) =>
        new[] { "common", @"worlds\worlds", @"worlds\worlds_english" }
            .Select(root => Path.Combine(Fc2Corpus.Root, root, path))
            .Where(File.Exists)
            .Select(File.ReadAllBytes)
            .FirstOrDefault();

    private static readonly Lazy<BarkBankIndex> Index = new(() => BarkBankIndex.Load(ReadExported));

    [Fact]
    public void A_mission_tag_finds_its_bank_and_each_line_names_an_event_in_the_bank_sounds()
    {
        if (!Fc2Corpus.Present) return;

        var (bank, barks) = Assert.NotNull(Index.Value.ForMission("A1SM03_SE02A"));
        Assert.Equal(1431575u, bank);

        BarkEntry greet = Assert.Single(barks, b => b.Block == "GREET");
        Assert.Equal("BFG", greet.Source);
        Assert.NotEmpty(greet.Lines);

        var events = SpkPackage.Parse(ReadExported(BarkBank.SoundsPath(bank))!).Records.Select(r => r.Id).ToHashSet();
        Assert.All(barks.SelectMany(b => b.Lines).SelectMany(l => l.SoundIds), id => Assert.Contains(id, events));
    }

    [Fact]
    public void Every_bark_bank_a_real_graph_loads_is_found()
    {
        if (!Fc2Corpus.Present || DominoCorpus.UserDirectory is not { } userDir) return;

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(userDir, "*.lua", SearchOption.AllDirectories).Where(f => !DominoDebugTwin.IsTwinPath(f)))
        {
            ReconstructedGraph graph = GraphBuilder.Build(UserGraphParser.Parse(DominoLuaSource.Parse(File.ReadAllText(file))));
            foreach (GraphNode node in graph.Nodes.Where(n => n.NodeTypePath.EndsWith("/SetMissionBarkBankState.lua", StringComparison.OrdinalIgnoreCase)))
            {
                if (node.Params.TryGetValue("MissionTag", out var expr)
                    && DominoValueRefs.Resolve(expr, graph) is { Variable: null } literal
                    && Index.Value.ForMission(literal.Value) is null)
                {
                    missing.Add(literal.Value);
                }
            }
        }

        Assert.True(missing.Count == 0, "No bank for: " + string.Join(", ", missing));
    }
}
