using JackAll.Core.Format.Move;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// Recovering the names a loadable MOVE graph threw away, from a twin nobody can parse.
/// </summary>
/// <remarks>
/// The whole method is one idea: a candidate string is accepted only when its CRC-32 equals a hash
/// the loadable graph actually keys on. The match is the proof, so the authoring format never has to
/// be decoded and the two files never have to agree structurally.
/// </remarks>
public sealed class MoveNamesTests
{
    /// <summary>A CPathID is plain CRC-32 of the lowercased name - the value move.md records for a
    /// state that really is in <c>dlc1.bin</c>.</summary>
    [Theory]
    [InlineData("dlc1_aim", 0x32F3C893u)]
    [InlineData("DLC1_Aim", 0x32F3C893u)]
    [InlineData("Pawn_Generic_Aim", 0x681235D2u)]
    public void A_name_hashes_to_its_CPathID(string name, uint expected)
        => Assert.Equal(expected, MoveNames.HashOf(name));

    /// <summary>
    /// Forward slashes must survive. <c>NameHash.Compute</c> folds them to backslashes because it
    /// hashes archive paths, and a state name may contain one.
    /// </summary>
    [Fact]
    public void A_slash_in_a_name_is_not_folded()
        => Assert.NotEqual(
            MoveNames.HashOf("Pawn_Generic_Aim_First/Stand"),
            MoveNames.HashOf(@"Pawn_Generic_Aim_First\Stand"));

    /// <summary>The retail graph and the names its twin gives it, or null when this checkout lacks them.</summary>
    private static readonly Lazy<(byte[] Graph, MoveFile File, MoveNames Names)?> Harvested = new(() =>
    {
        if (Fixture.Read(MoveStateIndexTests.Manager) is not { } graph
            || Fixture.Read(MoveStateIndexTests.Named) is not { } twin) return null;

        MoveFile file = MoveCodec.Load(graph);
        return (graph, file, MoveNames.Harvest(twin, MoveNames.HashesIn(file)));
    });

    /// <summary>
    /// The measurement the whole approach rests on: every state name comes back.
    /// </summary>
    [Fact]
    public void Every_state_name_is_recovered_from_the_authoring_twin()
    {
        if (Harvested.Value is not (_, var file, var names)) return;

        MoveStateIndex index = MoveStateIndex.Build(file);

        List<uint> states = [.. index.Slots.Select(s => MoveStateIndex.NameHashOf(s)!.Value)];
        List<uint> missing = [.. states.Where(h => names.Of(h) is null)];

        Assert.Empty(missing);
        Assert.All(states, h => Assert.Equal(h, MoveNames.HashOf(names.Of(h)!)));
    }

    /// <summary>A name is kept only when it proves itself, so an unrelated file yields nothing.</summary>
    [Fact]
    public void A_string_that_hashes_to_nothing_wanted_is_discarded()
    {
        if (Fixture.Read(MoveStateIndexTests.Named) is not { } twin) return;

        MoveNames none = MoveNames.Harvest(twin, new HashSet<uint>());
        Assert.Equal(0, none.Count);

        MoveNames one = MoveNames.Harvest(
            twin, new HashSet<uint> { MoveNames.HashOf("Pawn_Generic_Aim") });
        Assert.True(one.Count <= 1);
    }

    /// <summary>The table survives the trip to disk and back.</summary>
    [Fact]
    public void The_table_round_trips_through_its_tsv()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tsv");
        try
        {
            MoveNames named = MoveNames.Harvest(
                System.Text.Encoding.ASCII.GetBytes("\0\0\0Pawn_Generic_Aim"),
                new HashSet<uint> { MoveNames.HashOf("Pawn_Generic_Aim") });
            Assert.Equal("Pawn_Generic_Aim", named.Of(0x681235D2));

            File.WriteAllText(path, named.ToTsv());
            Assert.Equal("Pawn_Generic_Aim", MoveNames.Load(path).Of(0x681235D2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A label is decoration and the number binds, so naming a fragment cannot change which unit it
    /// resolves to - the property that lets the bundled table be optional.
    /// </summary>
    [Fact]
    public void Naming_a_fragment_does_not_change_what_it_binds_to()
    {
        if (Harvested.Value is not (var bytes, _, var names)) return;

        IContainerTree bare = MoveContainerSplitter.Instance.Open(bytes);
        IContainerTree labelled = new MoveContainerSplitter(names).Open(bytes);

        List<string> bareIds = [.. bare.List().Select(r => r.Id)];
        List<string> labelledIds = [.. labelled.List().Select(r => r.Id)];
        Assert.Equal(bareIds.Count, labelledIds.Count);

        int renamed = 0;
        for (int i = 0; i < bareIds.Count; i++)
        {
            Assert.Equal(
                MoveContainerSplitter.UnitOf(bareIds[i]), MoveContainerSplitter.UnitOf(labelledIds[i]));
            string? xml = bare.Extract(bareIds[i]);
            Assert.Equal(xml, labelled.Extract(labelledIds[i]));

            // A labelled id also resolves through the unlabelled tree, and the other way round.
            if (bareIds[i] == labelledIds[i])
            {
                Assert.NotNull(xml);
            }
            else
            {
                Assert.NotNull(bare.Extract(labelledIds[i]));
                renamed++;
            }
        }

        Assert.True(renamed > 0, "the twin is expected to name something");
    }
}
