using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using JackAll.Core.Format;

namespace JackAll.Tools.Shader;

/// <summary>One <c>#define</c> a shader is compiled with: its bit in the permutation mask, its position
/// in the source's define list and, for a ranged option, the highest value it counts to.</summary>
public sealed record ShaderOption(string Name, int Bit, int Define, uint? Max)
{
    /// <summary>Bits the value occupies: one for a switch, enough for 1 to Max + 1 for a ranged option.</summary>
    public int Width => Max is { } max ? BitOperations.Log2(max + 1) + 1 : 1;

    /// <summary>The option's value in a permutation mask; zero when the permutation does not set it.</summary>
    public uint ValueIn(ulong mask) => (uint)((mask >> Bit) & ((1UL << Width) - 1));
}

/// <summary>A shader as <c>fastinitdata_d3d9.bin</c> lists it, with its options.</summary>
public sealed class ShaderDefinition(string name, int number, IReadOnlyList<ShaderOption> options)
{
    public string Name { get; } = name;

    /// <summary>1-based; the low byte of every permutation mask of this shader.</summary>
    public int Number { get; } = number;

    /// <summary>In ascending CRC32 of the name, the order a permutation key lists them in.</summary>
    public IReadOnlyList<ShaderOption> Options { get; } = options;

    /// <summary>The options a permutation mask sets.</summary>
    public IEnumerable<ShaderOption> OptionsIn(ulong mask) => Options.Where(o => o.ValueIn(mask) != 0);

    /// <summary>
    /// The string whose CRC32 keys the permutation in the index tables: the shader's name, then per
    /// option set <c>_</c> + its define index, and for a ranged option <c>-</c> + (value - 1) +
    /// <c>of</c> + max.
    /// </summary>
    public string PermutationName(ulong mask)
    {
        var name = new StringBuilder(Name);
        foreach (ShaderOption option in Options)
        {
            uint value = option.ValueIn(mask);
            if (value == 0)
            {
                continue;
            }
            name.Append('_').Append(option.Define);
            if (option.Max is { } max)
            {
                name.Append('-').Append(value - 1).Append("of").Append(max);
            }
        }
        return name.ToString();
    }
}

/// <summary>
/// <c>engine\shaders\fastinitdata_d3d9.bin</c>, big-endian: every shader permutation the game was
/// compiled with as a 64-bit option mask, then each shader's options and defines.
/// </summary>
/// <remarks>
/// The low byte of a mask is the shader's <see cref="ShaderDefinition.Number"/>; the options shared by
/// every material shader sit at bits 8 to 30 and a shader's own from bit 31. Per-shader fields the
/// labels do not use (preload variations, material LOD masks) are read past, not kept.
/// </remarks>
public sealed class ShaderInitData
{
    private const uint Version = 1;

    private static ReadOnlySpan<byte> Trailer => "AllOk"u8;

    private ShaderInitData(IReadOnlyList<ulong> permutations, IReadOnlyList<ShaderDefinition> shaders)
    {
        Permutations = permutations;
        Shaders = shaders;
    }

    /// <summary>Every permutation's option mask, ascending.</summary>
    public IReadOnlyList<ulong> Permutations { get; }

    public IReadOnlyList<ShaderDefinition> Shaders { get; }

    /// <summary>The shader a permutation mask belongs to.</summary>
    public ShaderDefinition ShaderOf(ulong mask) => Shaders[(int)(mask & 0xFF) - 1];

    public static ShaderInitData Parse(ReadOnlySpan<byte> data)
    {
        var cursor = new ByteCursor(data);
        if (U32(ref cursor) != Version)
        {
            throw new InvalidDataException("Not a fastinitdata table: version is not 1.");
        }

        var permutations = new ulong[U32(ref cursor)];
        for (int i = 0; i < permutations.Length; i++)
        {
            permutations[i] = U64(ref cursor);
        }

        var shaders = new ShaderDefinition[U32(ref cursor)];
        for (int i = 0; i < shaders.Length; i++)
        {
            shaders[i] = ReadShader(ref cursor, i + 1);
        }

        if (!cursor.ReadSpan(Trailer.Length).SequenceEqual(Trailer) || cursor.Remaining != 0)
        {
            throw new InvalidDataException("fastinitdata does not end with its AllOk trailer.");
        }
        return new ShaderInitData(permutations, shaders);
    }

    private static ShaderDefinition ReadShader(ref ByteCursor cursor, int number)
    {
        string name = Text(ref cursor);
        // Per-stage relevant options, the SHADOW, DEPTH and PICKING bits, the platform's valid bits, the next free bit.
        cursor.ReadSpan((7 * 8) + 4);

        var options = new (uint Crc, string Name, int Bit, uint Max)[U32(ref cursor)];
        for (int i = 0; i < options.Length; i++)
        {
            options[i] = (U32(ref cursor), Text(ref cursor), (int)U32(ref cursor), U32(ref cursor));
        }

        // Preload variations: an option mask and a pass flag byte each.
        SkipList(ref cursor, 9);

        var defines = new string[U32(ref cursor)];
        for (int i = 0; i < defines.Length; i++)
        {
            defines[i] = Text(ref cursor);
        }

        for (int i = 0; i < 3; i++)
        {
            Text(ref cursor);
        }
        // Two masks, a word, the material LOD level count, eight LOD masks, a flag byte.
        cursor.ReadSpan(8 + 8 + 4 + 4 + (8 * 8) + 1);
        SkipList(ref cursor, 8);

        return new ShaderDefinition(name, number, [.. options.OrderBy(o => o.Crc).Select(o => new ShaderOption(
            o.Name, o.Bit, DefineIndex(defines, o.Name, name), o.Max == uint.MaxValue ? null : o.Max))]);
    }

    private static int DefineIndex(string[] defines, string option, string shader)
    {
        int index = Array.IndexOf(defines, option);
        return index >= 0 ? index
            : throw new InvalidDataException($"fastinitdata: {shader}'s option {option} is not among its defines.");
    }

    /// <summary>Reads past a counted list of fixed-size records.</summary>
    private static void SkipList(ref ByteCursor cursor, int recordSize)
    {
        int count = (int)U32(ref cursor);
        cursor.ReadSpan(count * recordSize);
    }

    private static string Text(ref ByteCursor cursor)
        => Encoding.ASCII.GetString(cursor.ReadSpan(BinaryPrimitives.ReadUInt16BigEndian(cursor.ReadSpan(2))));

    private static uint U32(ref ByteCursor cursor) => BinaryPrimitives.ReadUInt32BigEndian(cursor.ReadSpan(4));

    private static ulong U64(ref ByteCursor cursor) => BinaryPrimitives.ReadUInt64BigEndian(cursor.ReadSpan(8));
}
