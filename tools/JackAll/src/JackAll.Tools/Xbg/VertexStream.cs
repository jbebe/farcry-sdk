using System.Buffers.Binary;

namespace JackAll.Tools.Xbg;

/// <summary>
/// Typed access to an `.xbg` vertex buffer.
/// </summary>
/// <remarks>
/// The container keeps each LOD's vertex block as bytes; this splits it into per-component runs and
/// packs them back. Components are held at file precision and converted only on demand, so an
/// unpack followed by a pack reproduces the original bytes exactly - which is what lets an editor
/// rewrite one part of a buffer and leave the rest untouched.
/// </remarks>
public sealed class VertexStream
{
    /// <summary>An unused UV channel is written as this sentinel rather than zeroed.</summary>
    public const short UvUnused = -32768;

    private readonly Dictionary<string, byte[]> _components;

    private VertexStream(uint flags, int count, Dictionary<string, byte[]> components)
    {
        Flags = flags;
        Count = count;
        _components = components;
    }

    public uint Flags { get; }

    public int Count { get; }

    public static VertexStream Unpack(ReadOnlySpan<byte> data, XbgVertexBuffer buffer, int count)
    {
        (List<(string Name, int Offset, int Size)> layout, int stride) = XbgFile.VertexLayout(buffer.Flags);
        if (stride != buffer.Stride)
        {
            throw new InvalidDataException(
                $"Flags 0x{buffer.Flags:X} imply stride {stride}, the file says {buffer.Stride}.");
        }

        Dictionary<string, byte[]> components = new(StringComparer.Ordinal);
        foreach ((string name, int offset, int size) in layout)
        {
            var run = new byte[count * size];
            for (int i = 0; i < count; i++)
            {
                data.Slice((int)buffer.Offset + offset + (i * stride), size)
                    .CopyTo(run.AsSpan(i * size));
            }
            components[name] = run;
        }
        return new VertexStream(buffer.Flags, count, components);
    }

    /// <summary>A stream over <paramref name="count"/> of this buffer's vertices, from a start.</summary>
    public VertexStream Slice(int start, int count)
    {
        Dictionary<string, byte[]> components = new(StringComparer.Ordinal);
        foreach ((string name, byte[] run) in _components)
        {
            int size = run.Length / Count;
            components[name] = run.AsSpan(start * size, count * size).ToArray();
        }
        return new VertexStream(Flags, count, components);
    }

    public byte[] Pack()
    {
        (List<(string Name, int Offset, int Size)> layout, int stride) = XbgFile.VertexLayout(Flags);
        var packed = new byte[Count * stride];
        foreach ((string name, int offset, int size) in layout)
        {
            byte[] run = _components[name];
            for (int i = 0; i < Count; i++)
            {
                run.AsSpan(i * size, size).CopyTo(packed.AsSpan((i * stride) + offset));
            }
        }
        return packed;
    }

    public bool Has(string component) => _components.ContainsKey(component);

    /// <summary>Model-space positions. int16 storage is scaled by the PMCP factor.</summary>
    public (float X, float Y, float Z)[] Positions(float scale)
    {
        byte[] run = _components["pos"];
        var out_ = new (float, float, float)[Count];
        if ((Flags & XbgFile.PosFloat) != 0)
        {
            for (int i = 0; i < Count; i++)
            {
                ReadOnlySpan<byte> v = run.AsSpan(i * 12, 12);
                out_[i] = (Single(v, 0), Single(v, 4), Single(v, 8));
            }
            return out_;
        }
        if ((Flags & XbgFile.PosHalf) != 0)
        {
            throw new NotSupportedException("Half-float positions; no shipped file uses them.");
        }
        for (int i = 0; i < Count; i++)
        {
            ReadOnlySpan<byte> v = run.AsSpan(i * 8, 8);
            out_[i] = (Int16(v, 0) * scale, Int16(v, 2) * scale, Int16(v, 4) * scale);
        }
        return out_;
    }

    /// <summary>
    /// UVs in the game's D3D space, where V = 0 is the top row, or null when the channel is unused.
    /// </summary>
    /// <remarks>
    /// A bottom-up tool wants <c>1 - v</c>; that flip belongs to whatever targets one rather than
    /// here, since the file's own convention is what every other consumer needs.
    /// </remarks>
    public (float U, float V)[]? Uvs(float translate, float scale, int channel = 0)
    {
        if (!_components.TryGetValue($"uv{channel}", out byte[]? run))
        {
            return null;
        }

        var out_ = new (float, float)[Count];
        bool used = false;
        for (int i = 0; i < Count; i++)
        {
            short u = Int16(run, i * 4);
            short v = Int16(run, (i * 4) + 2);
            used |= u != UvUnused || v != UvUnused;
            out_[i] = (translate + (u * scale), translate + (v * scale));
        }
        return used ? out_ : null;
    }

    public (float X, float Y, float Z)[]? Normals() => Directions("normal");

    public (float X, float Y, float Z)[]? Tangents() => Directions("tangent");

    public (float X, float Y, float Z)[]? Binormals() => Directions("binormal");

    /// <summary>Vertex colour as RGBA floats; the file stores BGRA.</summary>
    public (float R, float G, float B, float A)[]? Colours()
    {
        if (!_components.TryGetValue("color", out byte[]? run))
        {
            return null;
        }

        var out_ = new (float, float, float, float)[Count];
        for (int i = 0; i < Count; i++)
        {
            out_[i] = (run[(i * 4) + 2] / 255.0f, run[(i * 4) + 1] / 255.0f,
                       run[i * 4] / 255.0f, run[(i * 4) + 3] / 255.0f);
        }
        return out_;
    }

    /// <summary>(weight, palette slot) pairs per vertex, zero-weight entries dropped.</summary>
    public List<(float Weight, int Slot)>[]? Skin()
    {
        if (!_components.TryGetValue("bone_wts1", out byte[]? first))
        {
            return null;
        }

        _components.TryGetValue("bone_wts2", out byte[]? second);
        var out_ = new List<(float, int)>[Count];
        for (int i = 0; i < Count; i++)
        {
            List<(float, int)> pairs = [];
            AddWeights(pairs, first, i);
            if (second is not null)
            {
                AddWeights(pairs, second, i);
            }
            out_[i] = pairs;
        }
        return out_;
    }

    /// <summary>
    /// D3DCOLOR bytes are unsigned-normalised and stored BGRA, so a direction component is
    /// <c>byte / 255 * 2 - 1</c> read back as z, y, x.
    /// </summary>
    private (float X, float Y, float Z)[]? Directions(string name)
    {
        if (!_components.TryGetValue(name, out byte[]? run))
        {
            return null;
        }

        var out_ = new (float, float, float)[Count];
        for (int i = 0; i < Count; i++)
        {
            out_[i] = (Signed(run[(i * 4) + 2]), Signed(run[(i * 4) + 1]), Signed(run[i * 4]));
        }
        return out_;
    }

    private static void AddWeights(List<(float, int)> pairs, byte[] run, int vertex)
    {
        for (int slot = 0; slot < 4; slot++)
        {
            byte weight = run[(vertex * 8) + slot];
            if (weight != 0)
            {
                pairs.Add((weight / 255.0f, run[(vertex * 8) + 4 + slot]));
            }
        }
    }

    private static float Signed(byte value) => (value / 255.0f * 2.0f) - 1.0f;

    private static float Single(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);

    private static short Int16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);
}
