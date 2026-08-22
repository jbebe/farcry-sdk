using JackAll.Core.Format;
using JackAll.Tools.Xbg;

namespace JackAll.Tools.Xbm;

public enum XbmSection
{
    Texture,
    Float,
    Integer,
}

/// <summary>
/// One property, in the order the file stores it.
/// </summary>
/// <remarks>
/// A section may repeat a key - <c>FATHER_MALIYA_HAIRHELMET</c> lists
/// <c>OmniSpotLightingDisabled</c> twice - so a reader that keeps only a map loses data and its
/// writer cannot put the file back. The maps on <see cref="XbmFile"/> are for lookup; this list is
/// what gets written.
/// </remarks>
public sealed class XbmEntry
{
    public required XbmSection Section { get; init; }

    /// <summary>The texture slot, or the property name.</summary>
    public required string Key { get; init; }

    /// <summary>The texture's game-relative path, for a texture entry.</summary>
    public string Path { get; set; } = string.Empty;

    public float[] Floats { get; set; } = [];

    public uint Integer { get; set; }
}

/// <summary>
/// Reader and writer for `.xbm`, the Dunia material.
/// </summary>
/// <remarks>
/// An `.xbm` is the same chunk container as an <see cref="XbgFile"/>; everything that matters sits
/// in its <c>LTMD</c> chunk, which an `.xbg` may also carry inline. Either way the body is a run of
/// counted sections: texture maps first, then property groups of one, two, three and four floats,
/// then a group of integers. The two differ only in what precedes that body - a standalone chunk
/// opens with five bytes nothing traced reads, an embedded one with the name its geometry
/// references and the part that name belongs to. Read one with the other's layout and it
/// desynchronises on the first field.
/// <para>
/// <see cref="XbmMaterial"/> flattens this into display-formatted text for the file viewer.
/// </para>
/// </remarks>
public sealed class XbmFile
{
    /// <summary>Bytes a standalone LTMD opens with that nothing traced reads.</summary>
    public const int PreambleLength = 5;

    /// <summary>Property group widths, in the order the sections appear.</summary>
    public static ReadOnlySpan<int> GroupSizes => [1, 2, 3, 4];

    public const string Diffuse1 = "DiffuseTexture1";
    public const string Diffuse2 = "DiffuseTexture2";
    public const string Mask1 = "MaskTexture1";
    public const string Specular1 = "SpecularTexture1";

    /// <summary>A character's skin and a cloth material name their albedo differently.</summary>
    public static readonly string[] AlbedoSlots = [Diffuse1, "SkinTexture", "FabricTexture"];

    public string Name { get; set; } = string.Empty;

    /// <summary>The part an embedded material applies to; empty for a standalone one.</summary>
    public string Part { get; set; } = string.Empty;

    public string Shader { get; set; } = string.Empty;

    public byte[] Preamble { get; set; } = new byte[PreambleLength];

    public uint Trailing { get; set; }

    public List<XbmEntry> Entries { get; } = [];

    public Dictionary<string, string> Textures { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, float[]> Floats { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, uint> Integers { get; } = new(StringComparer.Ordinal);

    /// <summary>The other chunks an `.xbm` carries, so an edit can be written back.</summary>
    public XbgFile? Container { get; private set; }

    public static XbmFile Parse(byte[] data)
    {
        XbgFile model = XbgFile.Parse(data);
        XbmFile self = ParseLtmd(ChunkOf(model).Raw);
        self.Container = model;
        return self;
    }

    /// <summary>A standalone `.xbm`'s LTMD, which opens with five bytes nothing reads.</summary>
    public static XbmFile ParseLtmd(byte[] raw)
    {
        var self = new XbmFile();
        var r = new ByteCursor(raw);
        self.Preamble = r.ReadBytes(PreambleLength);
        self.Name = r.ReadCString();
        self.Shader = r.ReadCString();
        self.ReadBody(ref r, raw.Length, "LTMD");
        return self;
    }

    /// <summary>
    /// The LTMD an `.xbg` embeds, whose body is preceded by the name its geometry references and the
    /// part that name belongs to.
    /// </summary>
    public static XbmFile ParseInline(byte[] raw)
    {
        var self = new XbmFile();
        var r = new ByteCursor(raw);
        self.Name = r.ReadCString();
        self.Part = r.ReadCString();
        self.Shader = r.ReadCString();
        self.ReadBody(ref r, raw.Length, "inline LTMD");
        return self;
    }

    /// <summary>The whole `.xbm` back, carrying whatever was changed here.</summary>
    public byte[] Write()
    {
        if (Container is null)
        {
            throw new InvalidOperationException(
                $"'{Name}' came from an .xbg, which has no .xbm to write.");
        }
        ChunkOf(Container).Raw = Pack();
        return Container.Write();
    }

    /// <summary>The LTMD payload this material is stored as.</summary>
    public byte[] Pack()
    {
        var w = new ByteWriter();
        w.WriteRaw(Preamble);
        w.WriteCString(Name);
        w.WriteCString(Shader);

        List<XbmEntry> textures = Section(XbmSection.Texture);
        w.WriteU32((uint)textures.Count);
        foreach (XbmEntry entry in textures)
        {
            w.WriteCString(entry.Path);
            w.WriteCString(entry.Key);
        }

        foreach (int width in GroupSizes)
        {
            List<XbmEntry> group = Section(XbmSection.Float, width);
            w.WriteU32((uint)group.Count);
            foreach (XbmEntry entry in group)
            {
                w.WriteCString(entry.Key);
                w.WriteF32Array(entry.Floats);
            }
        }

        List<XbmEntry> integers = Section(XbmSection.Integer);
        w.WriteU32((uint)integers.Count);
        foreach (XbmEntry entry in integers)
        {
            w.WriteCString(entry.Key);
            w.WriteU32(entry.Integer);
        }

        w.WriteU32(Trailing);
        return w.ToArray();
    }

    /// <summary>One section's entries in file order, optionally one float width.</summary>
    public List<XbmEntry> Section(XbmSection section, int? width = null)
        => [.. Entries.Where(e => e.Section == section && (width is null || e.Floats.Length == width))];

    /// <summary>The diffuse map, under whichever slot name this shader uses.</summary>
    public string? Albedo()
        => AlbedoSlots.Select(slot => Textures.GetValueOrDefault(slot)).FirstOrDefault(path => path is not null);

    public (float U, float V) Tiling(string key)
        => Floats.TryGetValue(key, out float[]? values) && values.Length >= 2
            ? (values[0], values[1])
            : (1.0f, 1.0f);

    /// <summary>Change a property the material already carries.</summary>
    public void Set(XbmSection section, string key, float[] values)
    {
        foreach (XbmEntry entry in Entries.Where(e => e.Section == section && e.Key == key))
        {
            entry.Floats = values;
        }
        if (!Floats.ContainsKey(key))
        {
            throw new KeyNotFoundException($"'{Name}' carries no float property named '{key}'.");
        }
        Floats[key] = values;
    }

    /// <summary>The materials an `.xbg` embeds, keyed by the name its geometry references.</summary>
    public static Dictionary<string, XbmFile> InlineMaterials(XbgFile model)
    {
        Dictionary<string, XbmFile> found = new(StringComparer.OrdinalIgnoreCase);
        foreach (XbgChunk chunk in model.Chunks)
        {
            if (chunk.Tag == XbgFile.TagMaterialBody && chunk.Raw.Length > 0)
            {
                XbmFile material = ParseInline(chunk.Raw);
                found[material.Name] = material;
            }
        }
        return found;
    }

    /// <summary>The chunk an `.xbm` keeps its material in.</summary>
    public static XbgChunk ChunkOf(XbgFile model)
        => model.Chunk(XbgFile.TagMaterialBody)
           ?? throw new InvalidDataException("No LTMD chunk.");

    private void ReadBody(ref ByteCursor r, int length, string what)
    {
        for (uint i = r.ReadU32(); i > 0; i--)
        {
            // The path comes first and names its slot second.
            string path = r.ReadCString();
            Add(new XbmEntry { Section = XbmSection.Texture, Key = r.ReadCString(), Path = path });
        }

        foreach (int width in GroupSizes)
        {
            for (uint i = r.ReadU32(); i > 0; i--)
            {
                string key = r.ReadCString();
                Add(new XbmEntry { Section = XbmSection.Float, Key = key, Floats = r.ReadF32Array(width) });
            }
        }

        for (uint i = r.ReadU32(); i > 0; i--)
        {
            string key = r.ReadCString();
            Add(new XbmEntry { Section = XbmSection.Integer, Key = key, Integer = r.ReadU32() });
        }

        Trailing = r.ReadU32();
        if (r.Position != length)
        {
            throw new InvalidDataException($"{what} consumed {r.Position} of {length} bytes.");
        }
    }

    /// <summary>
    /// Append a property, keeping the file-order list and the lookup maps in step.
    /// </summary>
    /// <remarks>
    /// Both have to be maintained together: the list is what gets written, including the one shipped
    /// material that repeats a key, and the maps are what callers read by name.
    /// </remarks>
    public void Add(XbmEntry entry)
    {
        Entries.Add(entry);
        switch (entry.Section)
        {
            case XbmSection.Texture:
                Textures[entry.Key] = entry.Path;
                break;
            case XbmSection.Float:
                Floats[entry.Key] = entry.Floats;
                break;
            case XbmSection.Integer:
                Integers[entry.Key] = entry.Integer;
                break;
        }
    }
}
