using System.Buffers.Binary;

namespace JackAll.Tools.Move;

/// <summary>
/// One direction of the MOVE serializer. Integer and version calls return their value because the
/// layout branches on it; the writer returns what the reader saw, so both take the same path.
/// </summary>
internal interface IMoveCodec
{
    uint Flags { get; }

    byte U8(string name);

    uint U32(string name);

    int S32(string name);

    void F32(string name);

    void Str(string name);

    void Data(string name);

    void Raw(string name, int count);

    uint Version(string name);

    MoveObject? Pointer(string name);
}

/// <summary>Reads and writes MOVE animation graphs (movemgr.bin, dlc*.bin).</summary>
/// <remarks>
/// Objects are addressed by their position in registration order, which is assigned pre-order -
/// after the ClassType word, before the object's own Serialize - so a back-reference can only ever
/// point backwards. <see cref="Save"/> discards the indices it read and renumbers from object
/// identity, which is what makes a byte-identical round trip evidence that the model is right.
/// </remarks>
public static class MoveCodec
{
    internal const uint VersionTag = 0x3ADE68B1;

    private const int HeaderSize = 12;

    public static MoveFile Load(byte[] data)
    {
        if (data.Length < HeaderSize)
        {
            throw new MoveFormatException("too short to hold a MOVE header");
        }

        MoveFile file = new()
        {
            Type = BinaryPrimitives.ReadUInt32LittleEndian(data),
            Version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)),
            Flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)),
        };

        MoveReadCodec codec = new(data, file);
        codec.ReadRoot();
        return file;
    }

    public static byte[] Save(MoveFile file)
    {
        MoveWriteCodec codec = new(file);
        return codec.WriteAll();
    }

    /// <summary>
    /// The 105 value channels as (name, enum values), read from a <em>named</em> twin. Only the
    /// loadable form is fully decoded, so the parse is allowed to fail once the value container is
    /// behind us - the channel table is the first thing in the file.
    /// </summary>
    public static IReadOnlyList<MoveChannel> ChannelTable(byte[] namedData)
    {
        MoveFile probe = new() { Flags = BinaryPrimitives.ReadUInt32LittleEndian(namedData.AsSpan(8)) };
        MoveReadCodec codec = new(namedData, probe);
        try
        {
            codec.ReadRoot();
        }
        catch (MoveFormatException)
        {
            // Expected: the authoring form is only partly decoded past the value container.
        }

        MoveObject container = probe.Objects.FirstOrDefault(o => o.ClassName == "CMoveValueContainer")
            ?? throw new MoveFormatException("no CMoveValueContainer in this file");

        List<MoveChannel> channels = [];
        uint? pendingType = null;
        List<string>? values = null;
        foreach (MoveOp op in container.Ops)
        {
            switch (op.Name)
            {
                case "m_eMVType":
                    pendingType = op.Number;
                    break;
                case "m_szName" when pendingType is { } type:
                    values = [];
                    channels.Add(new MoveChannel(Text(op.Bytes), type == 5 ? values : null));
                    pendingType = null;
                    break;
                case "m_szEnumValue":
                    values?.Add(Text(op.Bytes));
                    break;
            }
        }

        return channels;
    }

    private static string Text(byte[]? bytes) =>
        bytes is null ? string.Empty : System.Text.Encoding.Latin1.GetString(bytes);
}

/// <summary>One value channel: its name, and its value names when it is an enum.</summary>
public sealed record MoveChannel(string Name, IReadOnlyList<string>? Values);
