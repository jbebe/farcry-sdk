using System.Buffers.Binary;

namespace JackAll.Tools.Xbg;

/// <summary>The scales a stream's integer components are expressed in.</summary>
public readonly record struct VertexScales(float PosScale, float UvTranslate, float UvScale)
{
    public static VertexScales Of(XbgFile model)
        => new(model.PosScale, model.UvCompress[0], model.UvCompress[1]);
}

/// <summary>
/// Float-space vertex arrays, as an editor holds them. Anything left null is inherited rather than
/// written.
/// </summary>
public sealed class VertexData
{
    public (float X, float Y, float Z)[]? Positions { get; init; }

    public (float U, float V)[]? Uvs { get; init; }

    public (float U, float V)[]? Uvs1 { get; init; }

    public (float X, float Y, float Z)[]? Normals { get; init; }

    public (float X, float Y, float Z)[]? Tangents { get; init; }

    public (float X, float Y, float Z)[]? Binormals { get; init; }

    public (float R, float G, float B, float A)[]? Colours { get; init; }

    public List<(float Weight, int Slot)>[]? Skin { get; init; }
}

/// <summary>
/// Packs float-space arrays back into a <see cref="VertexStream"/>, the inverse of its accessors.
/// </summary>
/// <remarks>
/// A template stream can be passed to inherit anything not supplied, which is how an untouched part
/// comes back byte for byte; without one, the constants every shipped vertex carries are used
/// instead. Every component quantises back exactly - checked per component over every shipped
/// buffer - so an editor handed float values does not lose anything by handing them back.
/// </remarks>
public static class VertexEncoder
{
    /// <summary>int16 positions, so a coordinate is this many scale steps at most.</summary>
    public const int PositionLimit = 32767;

    /// <summary>Weights are stored in four slots per set, two sets at most.</summary>
    public const int SlotsPerSet = 4;

    /// <summary>Slots carrying the same value in all 14,319,419 shipped vertices.</summary>
    public const short PositionW = 1;
    public const byte DirectionW = 128;

    /// <summary>What one vertex of each component holds before anything is written into it.</summary>
    private static readonly Dictionary<string, byte[]> Defaults = new(StringComparer.Ordinal)
    {
        // Four int16s: x, y, z, then PositionW.
        ["pos"] = [0, 0, 0, 0, 0, 0, 1, 0],
        ["uv0"] = [0, 0, 0, 0],
        ["uv1"] = [0, 0, 0, 0],
        ["uv2"] = [0, 0, 0, 0],
        ["normal"] = [DirectionW, DirectionW, 255, DirectionW],
        ["tangent"] = [255, DirectionW, DirectionW, DirectionW],
        ["binormal"] = [DirectionW, 255, DirectionW, DirectionW],
        ["color"] = [255, 255, 255, 255],
        ["unk400"] = [0, 0, 0, 0],
        ["bone_wts1"] = [255, 0, 0, 0, 0, 0, 0, 0],
        ["bone_wts2"] = [0, 0, 0, 0, 0, 0, 0, 0],
    };

    public static VertexStream Encode(
        uint flags, int count, VertexScales scales, VertexData data, VertexStream? template = null)
    {
        if ((flags & (XbgFile.PosFloat | XbgFile.PosHalf)) != 0)
        {
            throw new NotSupportedException("Only int16 positions are written.");
        }
        if (template is not null && template.Count != count)
        {
            throw new InvalidDataException(
                $"Template holds {template.Count} vertices, need {count}.");
        }

        (List<(string Name, int Offset, int Size)> layout, _) = XbgFile.VertexLayout(flags);
        Dictionary<string, byte[]> components = new(StringComparer.Ordinal);
        foreach ((string name, _, int size) in layout)
        {
            components[name] = template is not null
                ? [.. template.Components[name]]
                : Repeat(Defaults.TryGetValue(name, out byte[]? one) ? one : new byte[size], count);
        }

        if (data.Positions is { } positions)
        {
            WritePositions(components["pos"], positions, scales.PosScale, template);
        }
        WriteUvs(components, layout, "uv0", data.Uvs, scales);
        WriteUvs(components, layout, "uv1", data.Uvs1, scales);
        WriteDirections(components, layout, "normal", data.Normals);
        WriteDirections(components, layout, "tangent", data.Tangents);
        WriteDirections(components, layout, "binormal", data.Binormals);

        if (data.Colours is { } colours && components.TryGetValue("color", out byte[]? colour))
        {
            for (int i = 0; i < colours.Length; i++)
            {
                // The file stores BGRA.
                colour[i * 4] = ToByte(colours[i].B);
                colour[(i * 4) + 1] = ToByte(colours[i].G);
                colour[(i * 4) + 2] = ToByte(colours[i].R);
                colour[(i * 4) + 3] = ToByte(colours[i].A);
            }
        }

        if (data.Skin is { } skin && components.ContainsKey("bone_wts1"))
        {
            WriteSkin(components, skin, (flags & XbgFile.BoneWeights2) != 0 ? 2 : 1);
        }

        return VertexStream.FromComponents(flags, count, components);
    }

    /// <summary>Quantise to 0..255 across the given range, clamped.</summary>
    public static byte ToByte(float value, float low = 0.0f, float high = 1.0f)
    {
        float span = high - low;
        if (span == 0.0f)
        {
            span = 1.0f;
        }
        return (byte)Math.Clamp(Math.Round((value - low) / span * 255.0), 0, 255);
    }

    private static void WritePositions(
        byte[] run, (float X, float Y, float Z)[] positions, float scale, VertexStream? template)
    {
        float limit = PositionLimit * scale;
        foreach ((float x, float y, float z) in positions)
        {
            float worst = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
            if (worst > limit)
            {
                throw new InvalidDataException(
                    $"A vertex at {worst:0.###} is past the {limit:0.###} this model's PMCP scale "
                    + "can store; rescale before encoding.");
            }
        }

        // The fourth slot is 1 in every shipped vertex, but an inherited one wins if it disagrees.
        short w = template is not null ? BinaryPrimitives.ReadInt16LittleEndian(run.AsSpan(6)) : PositionW;
        for (int i = 0; i < positions.Length; i++)
        {
            Span<byte> vertex = run.AsSpan(i * 8, 8);
            BinaryPrimitives.WriteInt16LittleEndian(vertex, Steps(positions[i].X, scale));
            BinaryPrimitives.WriteInt16LittleEndian(vertex[2..], Steps(positions[i].Y, scale));
            BinaryPrimitives.WriteInt16LittleEndian(vertex[4..], Steps(positions[i].Z, scale));
            BinaryPrimitives.WriteInt16LittleEndian(vertex[6..], w);
        }
    }

    private static void WriteUvs(
        Dictionary<string, byte[]> components, List<(string Name, int Offset, int Size)> layout,
        string name, (float U, float V)[]? values, VertexScales scales)
    {
        if (values is null || !components.TryGetValue(name, out byte[]? run)
            || !layout.Any(entry => entry.Name == name))
        {
            return;
        }

        for (int i = 0; i < values.Length; i++)
        {
            Span<byte> vertex = run.AsSpan(i * 4, 4);
            BinaryPrimitives.WriteInt16LittleEndian(vertex, Steps(values[i].U - scales.UvTranslate, scales.UvScale));
            BinaryPrimitives.WriteInt16LittleEndian(vertex[2..], Steps(values[i].V - scales.UvTranslate, scales.UvScale));
        }
    }

    /// <summary>A direction as the file stores it: unsigned BGRA, so z, y, x, then w.</summary>
    private static void WriteDirections(
        Dictionary<string, byte[]> components, List<(string Name, int Offset, int Size)> layout,
        string name, (float X, float Y, float Z)[]? values)
    {
        if (values is null || !components.TryGetValue(name, out byte[]? run)
            || !layout.Any(entry => entry.Name == name))
        {
            return;
        }

        for (int i = 0; i < values.Length; i++)
        {
            run[i * 4] = ToByte(values[i].Z, -1.0f, 1.0f);
            run[(i * 4) + 1] = ToByte(values[i].Y, -1.0f, 1.0f);
            run[(i * 4) + 2] = ToByte(values[i].X, -1.0f, 1.0f);
            run[(i * 4) + 3] = DirectionW;
        }
    }

    /// <summary>Split (weight, slot) pairs across the one or two weight components.</summary>
    private static void WriteSkin(
        Dictionary<string, byte[]> components, List<(float Weight, int Slot)>[] skin, int sets)
    {
        for (int vertex = 0; vertex < skin.Length; vertex++)
        {
            List<(float Weight, int Slot)> pairs = skin[vertex];
            for (int set = 0; set < sets; set++)
            {
                if (!components.TryGetValue(set == 0 ? "bone_wts1" : "bone_wts2", out byte[]? run))
                {
                    continue;
                }

                Span<byte> block = run.AsSpan(vertex * 8, 8);
                for (int slot = 0; slot < SlotsPerSet; slot++)
                {
                    int at = (set * SlotsPerSet) + slot;
                    (float weight, int bone) = at < pairs.Count ? pairs[at] : (0.0f, 0);
                    block[slot] = ToByte(weight);
                    block[SlotsPerSet + slot] = (byte)bone;
                }
            }
        }
    }

    private static short Steps(float value, float scale) => (short)Math.Round(value / scale);

    private static byte[] Repeat(byte[] one, int count)
    {
        var run = new byte[one.Length * count];
        for (int i = 0; i < count; i++)
        {
            one.CopyTo(run, i * one.Length);
        }
        return run;
    }
}
