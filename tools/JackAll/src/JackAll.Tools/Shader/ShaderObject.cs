using System.Buffers.Binary;
using System.Globalization;
using System.Xml.Linq;

namespace JackAll.Tools.Shader;

/// <summary>
/// One compiled Direct3D 9 shader out of <c>shadersobj</c>: a parameter binding table in front of
/// stock <c>vs_3_0</c>/<c>ps_3_0</c> bytecode.
/// </summary>
/// <remarks>
/// The bytecode ships without its CTAB, so this table is the only surviving record of which
/// parameter feeds which register, and parameter names survive only as CRC32 — pass a dictionary to
/// <see cref="ToXml"/> to name them. The sibling <c>engine\shaders\obj10</c> tree is a different,
/// Direct3D 10 container this does not read.
/// </remarks>
public sealed record ShaderObject(byte Kind, IReadOnlyList<ShaderParameter> Parameters, byte[] Bytecode)
{
    private const uint Magic = 0x0208068D;

    /// <summary>Fills the two 16-bit gaps flanking the kind/count pair. Constant in every shipped object.</summary>
    private const ushort Padding = 0x7F7F;

    private const int HeaderSize = 12;
    private const int ParameterSize = 8;

    /// <summary>Parses a <c>.pso</c>/<c>.vso</c>, or throws when the bytes are not one.</summary>
    public static ShaderObject Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
        {
            throw new InvalidDataException(
                "Not a Direct3D 9 shader object: the file does not start with the shadersobj magic.");
        }

        int bytecodeOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        byte kind = data[8];
        int count = data[9];

        if (bytecodeOffset != HeaderSize + (ParameterSize * count) || bytecodeOffset > data.Length)
        {
            throw new InvalidDataException(
                $"Shader object header is inconsistent: {count} parameter(s) do not reach the "
                + $"bytecode at offset {bytecodeOffset}.");
        }

        var parameters = new ShaderParameter[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = data.Slice(HeaderSize + (i * ParameterSize), ParameterSize);
            parameters[i] = new ShaderParameter(
                BinaryPrimitives.ReadUInt32LittleEndian(entry),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]),
                entry[6],
                entry[7]);
        }

        return new ShaderObject(kind, parameters, data[bytecodeOffset..].ToArray());
    }

    /// <summary>Writes the object back out, byte-for-byte for anything <see cref="Parse"/> read.</summary>
    public byte[] Build()
    {
        int bytecodeOffset = HeaderSize + (ParameterSize * Parameters.Count);
        byte[] result = new byte[bytecodeOffset + Bytecode.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(result, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), (ushort)bytecodeOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), Padding);
        result[8] = Kind;
        result[9] = checked((byte)Parameters.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), Padding);

        for (int i = 0; i < Parameters.Count; i++)
        {
            Span<byte> entry = result.AsSpan(HeaderSize + (i * ParameterSize), ParameterSize);
            ShaderParameter p = Parameters[i];
            BinaryPrimitives.WriteUInt32LittleEndian(entry, p.NameHash);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], p.Binding);
            entry[6] = p.Count;
            entry[7] = p.Stride;
        }

        Bytecode.CopyTo(result, bytecodeOffset);
        return result;
    }

    /// <summary>
    /// Drops the comment block <c>fxc</c> writes in front of its output — the constant table, which
    /// names every parameter and its register. No shipped object carries one, and the engine reads
    /// bindings from the table in front of the bytecode instead, so keeping it would double the file
    /// to say something twice.
    /// </summary>
    public static byte[] StripConstantTable(byte[] bytecode)
    {
        const uint CommentToken = 0xFFFE;
        const uint EndToken = 0x0000FFFF;

        int cursor = 4;
        while (cursor + 4 <= bytecode.Length)
        {
            uint token = BinaryPrimitives.ReadUInt32LittleEndian(bytecode.AsSpan(cursor));
            if ((token & 0xFFFF) != CommentToken)
            {
                break;
            }
            cursor += 4 * (1 + (int)((token >> 16) & 0x7FFF));
        }

        if (cursor == 4 || cursor > bytecode.Length)
        {
            return bytecode;
        }

        byte[] stripped = new byte[4 + bytecode.Length - cursor];
        bytecode.AsSpan(0, 4).CopyTo(stripped);
        bytecode.AsSpan(cursor).CopyTo(stripped.AsSpan(4));

        // A truncated stream is worse than a fat one, so hand back the original unless the result
        // still ends where a shader must.
        return stripped.Length >= 8
            && BinaryPrimitives.ReadUInt32LittleEndian(stripped.AsSpan(stripped.Length - 4)) == EndToken
                ? stripped
                : bytecode;
    }

    /// <summary>The shader model the bytecode declares, as it appears in an <c>fxc</c> listing.</summary>
    public string Profile => Bytecode.Length < 4
        ? "unknown"
        : BinaryPrimitives.ReadUInt32LittleEndian(Bytecode) switch
        {
            0xFFFF0300 => "ps_3_0",
            0xFFFE0300 => "vs_3_0",
            uint other => "0x" + other.ToString("x8", CultureInfo.InvariantCulture),
        };

    /// <summary>
    /// Renders the binding table as a companion XML file, resolving parameter names through
    /// <paramref name="names"/> where it can. Only the attributes are read back by
    /// <see cref="FromXml"/>; a resolved name is a comment for the reader.
    /// </summary>
    public string ToXml(IReadOnlyDictionary<uint, string>? names = null)
    {
        var table = new XElement("Parameters");
        foreach (ShaderParameter p in Parameters)
        {
            var entry = new XElement(
                "Parameter",
                new XAttribute("hash", p.NameHash.ToString("x8", CultureInfo.InvariantCulture)),
                new XAttribute("binding", p.Binding.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("count", p.Count.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("stride", p.Stride.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("register", p.Register.ToString(CultureInfo.InvariantCulture)));

            if (names is not null && names.TryGetValue(p.NameHash, out string? name))
            {
                entry.Add(new XAttribute("name", name));
            }
            table.Add(entry);
        }

        var root = new XElement(
            "ShaderObject",
            new XAttribute("kind", Kind.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("profile", Profile),
            table);
        return new XDocument(root).ToString();
    }

    /// <summary>Restores the binding table from a companion XML file produced by <see cref="ToXml"/>.</summary>
    public static ShaderObject FromXml(string xml, byte[] bytecode)
    {
        XElement? root = XDocument.Parse(xml).Root;
        if (root is not { Name.LocalName: "ShaderObject" })
        {
            throw new InvalidDataException("Not a shader object XML file.");
        }

        byte kind = byte.Parse(root.Attribute("kind")!.Value, CultureInfo.InvariantCulture);
        var parameters = root.Element("Parameters")?.Elements("Parameter").Select(e =>
            new ShaderParameter(
                uint.Parse(e.Attribute("hash")!.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                ushort.Parse(e.Attribute("binding")!.Value, CultureInfo.InvariantCulture),
                byte.Parse(e.Attribute("count")!.Value, CultureInfo.InvariantCulture),
                byte.Parse(e.Attribute("stride")!.Value, CultureInfo.InvariantCulture)))
            .ToArray() ?? [];

        return new ShaderObject(kind, parameters, bytecode);
    }
}

/// <summary>
/// One row of a <see cref="ShaderObject"/>'s binding table: the register the engine writes a named
/// value into.
/// </summary>
/// <param name="NameHash">CRC32 of the parameter's name, exact case — not the lowercasing
/// <c>NameHash.Compute</c> applies to archive paths.</param>
/// <param name="Binding">The packed register and its flags; see <see cref="Register"/>.</param>
/// <param name="Count">How many registers the value occupies.</param>
/// <param name="Stride">Registers per element, 4 for a matrix.</param>
public readonly record struct ShaderParameter(uint NameHash, ushort Binding, byte Count, byte Stride)
{
    /// <summary>The register the value starts at, matching what <c>fxc /dumpbin</c> shows.</summary>
    public int Register => Binding >> 6;

    /// <summary>
    /// The low bits of <see cref="Binding"/>, which sort parameters into kinds the shipped corpus
    /// only partly explains: 12 is always a sampler and 8 always a constant, while 0, 9, 10, 16 and
    /// 20 also occur.
    /// </summary>
    public int Flags => Binding & 0x3F;
}
