using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace JackAll.Tools.Shader;

/// <summary>
/// One of the <c>index.pso</c>/<c>index.vso</c>/<c>index.rs</c> tables that sit at the root of a
/// <c>shadersobj</c> tree: a sorted map from a shader permutation to the object it loads.
/// </summary>
/// <remarks>
/// A permutation keys on <see cref="KeyOf"/> of its name: the shader's source name alone with no
/// options, or <see cref="ShaderDefinition.PermutationName"/> with them.
/// </remarks>
public sealed class ShaderIndex
{
    /// <summary>The D3D9 tree's folder in the archive; its index tables sit at its root.</summary>
    public const string Root = @"engine\shaders\obj\";

    /// <summary>The object kinds, each with its own index table: pixel shader, vertex shader, render state.</summary>
    public static readonly IReadOnlyList<string> Stages = ["pso", "vso", "rs"];

    private const int HeaderSize = 28;

    private static ReadOnlySpan<byte> Magic => "DAEH"u8;

    private readonly Dictionary<uint, uint> byKey;

    private ShaderIndex(uint version, Dictionary<uint, uint> byKey)
    {
        Version = version;
        this.byKey = byKey;
    }

    /// <summary>Format version stored in the header; 10 in the shipped tables.</summary>
    public uint Version { get; }

    /// <summary>Every permutation key the table holds, in the table's own sorted order.</summary>
    public IReadOnlyCollection<uint> Keys => byKey.Keys;

    /// <summary>How many permutations the table maps.</summary>
    public int Count => byKey.Count;

    /// <summary>The key of a permutation by its name; a bare shader name is its permutation with no options.</summary>
    public static uint KeyOf(string permutationName) => Crc32.HashToUInt32(Encoding.ASCII.GetBytes(permutationName));

    public static ShaderIndex Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data.Slice(4, 4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a shadersobj index: the file does not carry the DAEH magic.");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[24..]);
        if (data.Length != HeaderSize + (count * 8))
        {
            throw new InvalidDataException(
                $"shadersobj index declares {count} entries, which do not fill its {data.Length} bytes.");
        }

        var byKey = new Dictionary<uint, uint>(count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = data.Slice(HeaderSize + (i * 8), 8);
            byKey[BinaryPrimitives.ReadUInt32LittleEndian(entry)] =
                BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
        }
        return new ShaderIndex(version, byKey);
    }

    /// <summary>
    /// The object hash this permutation loads, or null when the table does not map the key. Zero is
    /// a real answer meaning the permutation binds no object of this kind — an <c>index.rs</c> entry
    /// for a shader that overrides no render state, say.
    /// </summary>
    public uint? Lookup(uint key) => byKey.TryGetValue(key, out uint hash) ? hash : null;

    /// <summary>
    /// The archive path the engine builds for an object hash: 128 buckets keyed on the hash's low
    /// seven bits, which is why <c>h00</c>..<c>h7f</c> are the only folders in a shadersobj tree.
    /// </summary>
    public static string PathOf(uint objectHash, string extension)
        => $"h{objectHash & 0x7F:x2}\\shadernumber_{objectHash:x8}.{extension}";
}
