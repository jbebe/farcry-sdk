using JackAll.Core.Format.Move;

namespace JackAll.Tests;

/// <summary>
/// Round-trips the MOVE animation graph, binary and through XML.
/// </summary>
/// <remarks>
/// <c>Save(Load(x)) == x</c> is stronger here than it looks: the writer discards the
/// back-reference indices it read and renumbers every pointer from object identity, so a
/// byte-identical result also proves the registration-order model. The XML trip then adds what
/// binary alone cannot catch - float bit patterns and non-text string bytes that are
/// unrepresentable in text. See docs/docs/file-formats/move.md.
/// </remarks>
public sealed class MoveCodecTests
{
    [Theory]
    [MemberData(nameof(MoveStateIndexTests.Graphs), MemberType = typeof(MoveStateIndexTests))]
    public void Round_trips_a_graph_byte_for_byte(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        byte[] rebuilt = MoveCodec.Save(MoveCodec.Load(original));

        Fixture.AssertSameBytes(path, original, rebuilt);
    }

    [Theory]
    [MemberData(nameof(MoveStateIndexTests.Graphs), MemberType = typeof(MoveStateIndexTests))]
    public void Round_trips_a_graph_through_xml(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        byte[] rebuilt = MoveXml.Encode(MoveXml.Decode(original));

        Fixture.AssertSameBytes(path, original, rebuilt);
    }

    [Theory]
    [MemberData(nameof(MoveStateIndexTests.Graphs), MemberType = typeof(MoveStateIndexTests))]
    public void Parses_a_graph_of_known_classes_only(string path)
    {
        if (Fixture.Read(path) is not { } graph) return;

        MoveFile file = MoveCodec.Load(graph);

        Assert.NotEmpty(file.Objects);
        Assert.NotNull(file.StateMachine);
        Assert.All(file.Objects, o => Assert.NotNull(MoveClasses.Name(MoveClasses.Id(o.ClassName))));
    }

    /// <summary>The channel table only exists in a named twin; the loadable form has no names.</summary>
    [Fact]
    public void Reads_the_channel_table_from_a_named_twin()
    {
        if (Fixture.Read(MoveStateIndexTests.Named) is not { } named) return;

        IReadOnlyList<MoveChannel> channels = MoveCodec.ChannelTable(named);

        Assert.Equal(105, channels.Count);
        Assert.Equal("HeadingAngle", channels[0].Name);
        Assert.Equal("EquippedWeapon", channels[17].Name);
        Assert.Equal(44, channels[17].Values!.Count);
        Assert.Equal("SawedOffShotgun", channels[17].Values![42]);
    }
}
