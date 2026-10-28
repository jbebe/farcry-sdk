using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace JackAll.Core.Format;

/// <summary>One glyph of an <see cref="MftFont"/>: how it sits on the line, and its cell on a page.</summary>
/// <param name="Left">From the pen to the glyph's left edge.</param>
/// <param name="Top">From the baseline up to the glyph's top edge.</param>
/// <param name="U0">The cell's UVs run from half a pixel inside its top-left corner to one and a half
/// pixels past the glyph's bottom-right, the convention every stock font follows.</param>
public readonly record struct MftGlyph(
    ushort Code, ushort Width, ushort Height, short Left, short Top, ushort Advance,
    float U0, float V0, float U1, float V1, ushort Page);

/// <summary>
/// A Magma pixmap font (<c>ui\fonts\*.mft</c>): glyph metrics, and where each glyph sits on the
/// DXT5 pages that ship beside it as <c>.xbt</c>. See docs/docs/file-formats/mft.md.
/// </summary>
/// <remarks>
/// The file also carries a character map, 256 blocks of 256 glyph indices, which every stock font
/// fills from its glyph list alone, so it is derived on write rather than kept.
/// </remarks>
public sealed class MftFont
{
    public const ushort CurrentVersion = 0xA0;

    /// <summary>Glyph 0, empty, which the character map's 0 lands on: a character the font lacks.</summary>
    public const ushort MissingCode = 0x7F;

    private const ushort Marker = 0xABCD;
    private const int BlockSize = 256;
    private static readonly byte[] Magic = "Magma Font\0"u8.ToArray();
    private static readonly Encoding Text = Encoding.Latin1;

    public ushort Version { get; set; } = CurrentVersion;

    public string Name { get; set; } = "";

    /// <summary>The font file it was drawn from, as whatever drew it recorded the path.</summary>
    public string Source { get; set; } = "";

    /// <summary>Five values from the tool that drew the font: 64, 64, 1024, 1024, then 1 or 10 in the
    /// stock fonts. The engine reads them and keeps none.</summary>
    public uint[] GeneratorSettings { get; set; } = [64, 64, 1024, 1024, 1];

    public byte Flag { get; set; }

    /// <summary>The pixel size the glyphs were drawn at.</summary>
    public byte Size { get; set; }

    public byte[] Style { get; set; } = new byte[4];

    public string Type { get; set; } = "PixmapFont";

    public List<MftGlyph> Glyphs { get; set; } = [];

    /// <summary>Two values the engine keeps beside the glyphs, of unknown use; Farcry2_25's.</summary>
    public float[] Trailer { get; set; } = [-1.171875f, 0.78125f];

    /// <summary>Each page as <c>\&lt;Name&gt;_&lt;n&gt;.png</c>; see <see cref="PagePath"/>.</summary>
    public List<string> Pages { get; set; } = [];

    public byte[] TextureType { get; set; } = "PNG\0\0\0\0\0\0\0"u8.ToArray();

    public string TextureSettings { get; set; } =
        "<settings database=\"TextureProfiles.xml\" profile=\"Grayscale AlphaBlend No MipMap\" />\n";

    /// <summary>The name a page is listed under.</summary>
    public static string PageName(string fontName, int page) => $"\\{fontName}_{page}.png";

    public static MftFont Read(byte[] data)
    {
        var c = new ByteCursor(data);
        if (!c.ReadSpan(Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a Magma font (missing 'Magma Font').");
        }

        var font = new MftFont { Version = c.ReadU16() };
        if (c.ReadU16() != Marker)
        {
            throw new InvalidDataException("Not a Magma font (missing its 0xABCD marker).");
        }
        font.Name = Text.GetString(c.ReadSpan(c.ReadU16()));
        font.Source = Text.GetString(c.ReadSpan((int)c.ReadU32()));
        font.GeneratorSettings = c.ReadU32Array(5);
        font.Flag = c.ReadU8();
        font.Size = c.ReadU8();
        font.Style = c.ReadBytes(4);
        font.Type = Text.GetString(c.ReadSpan(c.ReadU8()));

        int count = c.ReadU16();
        for (int i = 0; i < count; i++)
        {
            font.Glyphs.Add(new MftGlyph(c.ReadU16(), c.ReadU16(), c.ReadU16(), c.ReadI16(), c.ReadI16(), c.ReadU16(),
                c.ReadF32(), c.ReadF32(), c.ReadF32(), c.ReadF32(), c.ReadU16()));
        }

        ushort[]?[] map = new ushort[BlockSize][];
        for (int block = 0; block < BlockSize; block++)
        {
            map[block] = c.ReadU8() != 0 ? c.ReadU16Array(BlockSize) : null;
        }
        if (!SameMap(map, font.CharacterMap()))
        {
            throw new InvalidDataException("This font's character map names glyphs its glyph list does not, which this reader does not keep.");
        }

        font.Trailer = c.ReadF32Array(2);
        int pages = c.ReadU8();
        for (int i = 0; i < pages; i++)
        {
            font.Pages.Add(Text.GetString(c.ReadSpan((int)c.ReadU32())));
        }
        font.TextureType = c.ReadBytes(10);
        font.TextureSettings = Text.GetString(c.ReadSpan((int)c.ReadU32()));
        if (c.Remaining != 0)
        {
            throw new InvalidDataException($"{c.Remaining} bytes follow the font's texture settings.");
        }
        return font;
    }

    public byte[] Write()
    {
        var w = new ByteWriter();
        w.WriteRaw(Magic);
        w.WriteU16(Version);
        w.WriteU16(Marker);
        byte[] name = Text.GetBytes(Name);
        w.WriteU16((ushort)name.Length);
        w.WriteRaw(name);
        WriteLong(w, Source);
        w.WriteU32Array(GeneratorSettings);
        w.WriteU8(Flag);
        w.WriteU8(Size);
        w.WriteRaw(Style);
        byte[] type = Text.GetBytes(Type);
        w.WriteU8((byte)type.Length);
        w.WriteRaw(type);

        w.WriteU16(checked((ushort)Glyphs.Count));
        foreach (MftGlyph g in Glyphs)
        {
            w.WriteU16(g.Code);
            w.WriteU16(g.Width);
            w.WriteU16(g.Height);
            w.WriteI16(g.Left);
            w.WriteI16(g.Top);
            w.WriteU16(g.Advance);
            w.WriteF32(g.U0);
            w.WriteF32(g.V0);
            w.WriteF32(g.U1);
            w.WriteF32(g.V1);
            w.WriteU16(g.Page);
        }

        foreach (ushort[]? block in CharacterMap())
        {
            w.WriteU8(block is null ? (byte)0 : (byte)1);
            if (block is not null)
            {
                w.WriteU16Array(block);
            }
        }

        w.WriteF32Array(Trailer);
        w.WriteU8(checked((byte)Pages.Count));
        foreach (string page in Pages)
        {
            WriteLong(w, page);
        }
        w.WriteRaw(TextureType);
        WriteLong(w, TextureSettings);
        return w.ToArray();
    }

    /// <summary>The font as editable XML: its header as attributes and a row per glyph.</summary>
    public XElement ToXml() => new("MagmaFont",
        new XAttribute("version", $"0x{Version:X}"),
        new XAttribute("name", Name),
        new XAttribute("source", Source),
        new XAttribute("generator", string.Join(' ', GeneratorSettings)),
        new XAttribute("flag", Flag),
        new XAttribute("size", Size),
        new XAttribute("style", Convert.ToHexString(Style)),
        new XAttribute("type", Type),
        new XAttribute("trailer", string.Join(' ', Trailer.Select(F))),
        new XAttribute("textureType", Convert.ToHexString(TextureType)),
        new XElement("TextureSettings", TextureSettings),
        new XElement("Pages", Pages.Select(p => new XElement("Page", p))),
        new XElement("Glyphs", Glyphs.Select(g => new XElement("Glyph",
            new XAttribute("code", $"0x{g.Code:X4}"),
            new XAttribute("char", char.IsControl((char)g.Code) || char.IsSurrogate((char)g.Code) ? "" : ((char)g.Code).ToString()),
            new XAttribute("width", g.Width), new XAttribute("height", g.Height),
            new XAttribute("left", g.Left), new XAttribute("top", g.Top), new XAttribute("advance", g.Advance),
            new XAttribute("uv", string.Join(' ', new[] { g.U0, g.V0, g.U1, g.V1 }.Select(F))),
            new XAttribute("page", g.Page)))));

    public static MftFont FromXml(XElement root)
    {
        if (root.Name.LocalName != "MagmaFont")
        {
            throw new InvalidDataException($"A <{root.Name.LocalName}> is not a <MagmaFont>.");
        }

        return new MftFont
        {
            Version = Hex(root, "version"),
            Name = Attr(root, "name"),
            Source = Attr(root, "source"),
            GeneratorSettings = Numbers<uint>(root, "generator"),
            Flag = Number<byte>(root, "flag"),
            Size = Number<byte>(root, "size"),
            Style = Convert.FromHexString(Attr(root, "style")),
            Type = Attr(root, "type"),
            Trailer = Numbers<float>(root, "trailer"),
            TextureType = Convert.FromHexString(Attr(root, "textureType")),
            TextureSettings = (string?)root.Element("TextureSettings") ?? "",
            Pages = [.. root.Element("Pages")?.Elements("Page").Select(p => p.Value) ?? []],
            Glyphs = [.. root.Element("Glyphs")?.Elements("Glyph").Select(g =>
            {
                float[] uv = Numbers<float>(g, "uv");
                return new MftGlyph(Hex(g, "code"), Number<ushort>(g, "width"), Number<ushort>(g, "height"),
                    Number<short>(g, "left"), Number<short>(g, "top"), Number<ushort>(g, "advance"),
                    uv[0], uv[1], uv[2], uv[3], Number<ushort>(g, "page"));
            }) ?? []],
        };
    }

    /// <summary>The page's file in the game: <c>\Name_0.png</c> is <c>&lt;folder&gt;\name_0.xbt</c>.</summary>
    /// <param name="folder">The folder the font is in, <c>ui\fonts</c> for the game's own.</param>
    public string PagePath(string folder, int page)
        => Path.Combine(folder, Path.ChangeExtension(Pages[page].TrimStart('\\'), ".xbt")).ToLowerInvariant();

    /// <summary>The characters the font draws, glyph 0 aside.</summary>
    public int CharacterCount => Glyphs.Count(g => g.Code != MissingCode);

    /// <summary>A block per 256 characters, null when none of them has a glyph, else each
    /// character's glyph index, 0 for one the font lacks.</summary>
    private ushort[]?[] CharacterMap()
    {
        ushort[]?[] map = new ushort[BlockSize][];
        for (int i = 0; i < Glyphs.Count; i++)
        {
            ushort code = Glyphs[i].Code;
            if (code != MissingCode)
            {
                (map[code >> 8] ??= new ushort[BlockSize])[code & 0xFF] = checked((ushort)i);
            }
        }
        return map;
    }

    private static bool SameMap(ushort[]?[] a, ushort[]?[] b)
        => a.Zip(b).All(p => p.First is null ? p.Second is null : p.Second is not null && p.First.SequenceEqual(p.Second));

    private static void WriteLong(ByteWriter w, string text)
    {
        byte[] bytes = Text.GetBytes(text);
        w.WriteU32((uint)bytes.Length);
        w.WriteRaw(bytes);
    }

    private static string Attr(XElement element, string name)
        => (string?)element.Attribute(name) ?? throw new InvalidDataException($"<{element.Name.LocalName}> has no {name}.");

    private static T Number<T>(XElement element, string name) where T : IParsable<T>
        => T.Parse(Attr(element, name), CultureInfo.InvariantCulture);

    private static T[] Numbers<T>(XElement element, string name) where T : IParsable<T>
        => [.. Attr(element, name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(n => T.Parse(n, CultureInfo.InvariantCulture))];

    /// <summary>A <c>0x</c> value.</summary>
    private static ushort Hex(XElement element, string name)
        => ushort.Parse(Attr(element, name).AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>A float in the shortest text that reads back to the same bits.</summary>
    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
