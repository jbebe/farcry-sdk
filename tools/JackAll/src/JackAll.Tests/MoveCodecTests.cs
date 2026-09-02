using JackAll.Tools.Move;

namespace JackAll.Tests;

/// <summary>
/// The correctness gate for the MOVE animation graph.
/// </summary>
/// <remarks>
/// <c>Save(Load(x)) == x</c> is stronger here than it looks: the writer discards the
/// back-reference indices it read and renumbers every pointer from object identity, so a
/// byte-identical result also proves the registration-order model. The XML gate then adds what
/// binary alone cannot catch - float bit patterns and non-text string bytes that are
/// unrepresentable in text. See docs/docs/file-formats/move.md.
/// </remarks>
public sealed class MoveCodecTests
{
    /// <summary>The loadable graphs. The named twins are the authoring form - only ~90% decoded,
    /// and the engine refuses them - so they are out of scope for a round-trip gate.</summary>
    private static List<string> MoveGraphs() =>
    [
        .. Fc2Corpus.Find(".bin").Where(path =>
            Path.GetDirectoryName(path)?.EndsWith("move", StringComparison.OrdinalIgnoreCase) == true
            && !Path.GetFileNameWithoutExtension(path)
                .EndsWith("named", StringComparison.OrdinalIgnoreCase)),
    ];

    public static TheoryData<string> CorpusFiles()
    {
        TheoryData<string> data = [];
        foreach (string path in MoveGraphs())
        {
            data.Add(path);
        }

        if (data.Count == 0)
        {
            data.Add(string.Empty);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Round_trips_every_graph_byte_for_byte(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        byte[] original = File.ReadAllBytes(path);
        byte[] rebuilt = MoveCodec.Save(MoveCodec.Load(original));

        Assert.Equal(original.Length, rebuilt.Length);
        int at = Fc2Corpus.FirstDifference(original, rebuilt);
        Assert.True(at < 0, $"{Path.GetFileName(path)} differs at 0x{at:x}");
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Round_trips_every_graph_through_xml(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        byte[] original = File.ReadAllBytes(path);
        byte[] rebuilt = MoveXml.Encode(MoveXml.Decode(original));

        Assert.Equal(original.Length, rebuilt.Length);
        int at = Fc2Corpus.FirstDifference(original, rebuilt);
        Assert.True(at < 0, $"{Path.GetFileName(path)} differs at 0x{at:x} after an XML round trip");
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Parses_a_graph_of_known_classes_only(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        MoveFile file = MoveCodec.Load(File.ReadAllBytes(path));

        Assert.NotEmpty(file.Objects);
        Assert.NotNull(file.StateMachine);
        Assert.All(file.Objects, o => Assert.NotNull(MoveClasses.Name(MoveClasses.Id(o.ClassName))));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Corpus_holds_the_move_graphs()
    {
        Assert.True(MoveGraphs().Count > 0, Fc2Corpus.MissingMessage(".bin"));
    }

    /// <summary>The channel table only exists in a named twin; the loadable form has no names.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Reads_the_channel_table_from_a_named_twin()
    {
        string named = Path.Combine(
            Fc2Corpus.Root, "common", "graphics", "move", "movemgrnamed.bin");
        Assert.True(File.Exists(named), Fc2Corpus.MissingMessage("movemgrnamed.bin"));

        IReadOnlyList<MoveChannel> channels = MoveCodec.ChannelTable(File.ReadAllBytes(named));

        Assert.Equal(105, channels.Count);
        Assert.Equal("HeadingAngle", channels[0].Name);
        Assert.Equal("EquippedWeapon", channels[17].Name);
        Assert.Equal(44, channels[17].Values!.Count);
        Assert.Equal("SawedOffShotgun", channels[17].Values![42]);
    }
}
