using JackAll.Tools.Bark;
using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.Spk;

namespace JackAll.Tests;

public class BarkBankTests
{
    // The bank list plus the two banks the tests look up, laid out as in the game's archives.
    private static byte[]? ReadExported(string path) => Fixture.Read(Path.Combine("Bark", path));

    private static readonly Lazy<BarkBankIndex> Index = new(() => BarkBankIndex.Load(ReadExported));

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(
        Path.Combine("Bark", BarkBank.ListPath),
        Path.Combine("Bark", BarkBank.BankPath(1431575)),
        Path.Combine("Bark", BarkBank.SoundsPath(1431575)),
        Path.Combine("Bark", BarkBank.BankPath(1431505)));

    [Fact]
    public void A_mission_tag_finds_its_bank_and_each_line_names_an_event_in_the_bank_sounds()
    {
        if (ReadExported(BarkBank.ListPath) is null) return;

        var (bank, barks) = Assert.NotNull(Index.Value.ForMission("A1SM03_SE02A"));
        Assert.Equal(1431575u, bank);

        BarkEntry greet = Assert.Single(barks, b => b.Block == "GREET");
        Assert.Equal("BFG", greet.Source);
        Assert.NotEmpty(greet.Lines);

        var events = SpkPackage.Parse(ReadExported(BarkBank.SoundsPath(bank))!).Records.Select(r => r.Id).ToHashSet();
        Assert.All(barks.SelectMany(b => b.Lines).SelectMany(l => l.SoundIds), id => Assert.Contains(id, events));
    }

    [Fact]
    public void A_real_graphs_literal_bark_bank_tag_finds_its_bank()
    {
        if (ReadExported(BarkBank.ListPath) is null || Fixture.ReadText(DominoFixtures.ReapSewBriefing) is not { } source) return;

        ReconstructedGraph graph = GraphBuilder.Build(UserGraphParser.Parse(DominoLuaSource.Parse(source)));
        string[] tags = [.. graph.Nodes
            .Where(n => n.NodeTypePath.EndsWith("/SetMissionBarkBankState.lua", StringComparison.OrdinalIgnoreCase)
                && n.Params.ContainsKey("MissionTag"))
            .Select(n => DominoValueRefs.Resolve(n.Params["MissionTag"], graph))
            .OfType<(string Value, string? Variable)>()
            .Where(r => r.Variable is null)
            .Select(r => r.Value)];

        Assert.Equal(["A1LM02_SE02"], tags);
        Assert.Equal(1431505u, Assert.NotNull(Index.Value.ForMission(tags[0])).Bank);
    }
}
