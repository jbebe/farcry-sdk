using System.Globalization;
using System.Text;
using JackAll.Core.Format;
using JackAll.Tools.Xbg;

namespace JackAll.Tools.Xbm;

/// <summary>One keyed entry from an .xbm's material definition - a texture slot ("DiffuseTexture1" ->
/// an .xbt path) or a numeric property (tiling, color, specular power, ...) already formatted for
/// display.</summary>
public sealed class XbmProperty
{
    public required string Key { get; init; }
    public required string Value { get; init; }
}

/// <summary>
/// Reads a Far Cry 2 .xbm material definition - name, shader template, texture slot bindings, and
/// every other keyed property - for preview purposes.
///
/// .xbm shares its outer "MESH"/chunk-list container with .xbg (see <see cref="XbgModel"/> and
/// research/file_manifest.md §3), but the only chunk a material preview needs is the material
/// definition itself ("LTMD" - "DMTL", i.e. "material", stored reversed). Rather than walking the
/// full chunk list (whose header layout is only needed for round-tripping/writing a new .xbm), this
/// locates that one chunk directly by its tag bytes, the same way both reference implementations do.
/// Ported from <c>tools/XBG-Importer/modules/Far_Cry_2/{import_materials_fc2,xbm_builder_fc2}.py</c> -
/// xbm_builder_fc2.parse_dmtl is the validated one (round-trip-tested against a 1583-file corpus), so
/// its chunk-header size (20 bytes: tag + version + chunkSize + dataSize, all u32) and preamble-search
/// logic are what this follows, rather than the older script's simplified 16-byte description.
/// </summary>
public sealed class XbmMaterial
{
    public required string Name { get; init; }
    public required string Template { get; init; }
    public required IReadOnlyList<XbmProperty> Textures { get; init; }
    public required IReadOnlyList<XbmProperty> Properties { get; init; }

    public static XbmMaterial Parse(byte[] data)
    {
        if (data.Length < 4 || data[0] != (byte)'H' || data[1] != (byte)'S' || data[2] != (byte)'E' || data[3] != (byte)'M')
        {
            throw new InvalidDataException(
                "Not a Far Cry 2 .xbm (no \"HSEM\" header) - this viewer doesn't support this file's format.");
        }

        int idx = data.AsSpan().IndexOf("LTMD"u8);
        if (idx < 0)
        {
            throw new InvalidDataException("No \"LTMD\" material chunk found in this .xbm.");
        }

        // tag(4) + version/chunkSize/dataSize/reserved (4 x u32) = 20-byte chunk header, then payload.
        if (idx + 20 > data.Length)
        {
            throw new InvalidDataException("Truncated LTMD chunk header.");
        }

        uint dataSize = ByteCursor.U32(data, idx + 12);
        int payloadStart = idx + 20;
        if (dataSize > int.MaxValue || payloadStart + (long)dataSize > data.Length)
        {
            throw new InvalidDataException("Truncated LTMD chunk payload.");
        }

        int payloadEnd = payloadStart + (int)dataSize;
        var r = new ByteCursor(data.AsSpan(0, payloadEnd));

        r.Position = FindPreamble(data, payloadStart, payloadEnd)
            ?? throw new InvalidDataException("Couldn't locate the LTMD material preamble.");

        string name = ReadString(ref r);
        string template = ReadString(ref r);

        var textures = new List<XbmProperty>();
        uint texCount = r.ReadU32();
        if (texCount > 256)
        {
            throw new InvalidDataException("Implausible texture slot count in LTMD chunk.");
        }

        for (int i = 0; i < texCount; i++)
        {
            string value = ReadString(ref r);
            string key = ReadString(ref r);
            textures.Add(new XbmProperty { Key = key, Value = value });
        }

        var properties = new List<XbmProperty>();
        foreach (int ncomp in new[] { 1, 2, 3, 4 })
        {
            uint count = r.ReadU32();
            if (count > 1024)
            {
                throw new InvalidDataException($"Implausible float{ncomp} property count in LTMD chunk.");
            }

            for (int i = 0; i < count; i++)
            {
                string key = ReadString(ref r);
                var vals = new float[ncomp];
                for (int c = 0; c < ncomp; c++)
                {
                    vals[c] = r.ReadF32();
                }

                properties.Add(new XbmProperty { Key = key, Value = string.Join(", ", vals.Select(FormatFloat)) });
            }
        }

        uint intCount = r.ReadU32();
        if (intCount > 1024)
        {
            throw new InvalidDataException("Implausible int property count in LTMD chunk.");
        }

        for (int i = 0; i < intCount; i++)
        {
            string key = ReadString(ref r);
            uint val = r.ReadU32();
            properties.Add(new XbmProperty { Key = key, Value = val.ToString() });
        }

        return new XbmMaterial { Name = name, Template = template, Textures = textures, Properties = properties };
    }

    /// <summary>Invariant so the text stays machine-readable wherever it is parsed back - a comma
    /// decimal separator would collide with the separator between a vector's components.</summary>
    private static string FormatFloat(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The material preamble (a handful of reserved bytes before the name string) varies in
    /// length between files - rather than assume a fixed size, try the offsets actually observed in the
    /// corpus (mirrors xbm_builder_fc2.parse_dmtl) and accept the first one whose length-prefixed string
    /// looks like a real, printable material name.</summary>
    private static int? FindPreamble(byte[] data, int payloadStart, int payloadEnd)
    {
        foreach (int off in new[] { 5, 9, 1, 0 })
        {
            int lenPos = payloadStart + off;
            if (lenPos + 4 > payloadEnd)
            {
                continue;
            }

            uint len = ByteCursor.U32(data, lenPos);
            if (len is 0 or >= 64)
            {
                continue;
            }

            int bodyStart = lenPos + 4;
            if (bodyStart + len > payloadEnd)
            {
                continue;
            }

            bool printable = true;
            for (int i = 0; i < len; i++)
            {
                byte b = data[bodyStart + i];
                if (b < 32 || b >= 127)
                {
                    printable = false;
                    break;
                }
            }

            if (printable)
            {
                return lenPos;
            }
        }

        return null;
    }

    /// <summary>A length-prefixed Latin1 string, followed by a NUL terminator when one is present.</summary>
    private static string ReadString(ref ByteCursor cursor)
    {
        uint len = cursor.ReadU32();
        if (len > 4096)
        {
            throw new InvalidDataException($"Implausible string length {len} at offset 0x{cursor.Position - 4:X}.");
        }

        string s = Encoding.Latin1.GetString(cursor.ReadSpan((int)len));
        if (cursor.Remaining > 0 && cursor.Data[cursor.Position] == 0)
        {
            cursor.Position += 1;
        }

        return s;
    }
}
