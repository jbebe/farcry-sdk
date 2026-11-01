using System.Numerics;
using System.Runtime.InteropServices;
using JackAll.Core.Format;
using JackAll.Tools.Fc2Model;
using JackAll.Tools.Xbt;

namespace JackAll.Tools.Mft;

/// <summary>A font page's coverage, one byte per pixel, rows first: the alpha of a white page.</summary>
public sealed record FontPage(int Width, int Height, byte[] Alpha)
{
    /// <summary>A page from a decoded texture's alpha.</summary>
    public static FontPage FromRgba(int width, int height, byte[] rgba)
        => new(width, height, [.. Enumerable.Range(0, width * height).Select(i => rgba[(i * 4) + 3])]);

    /// <summary>The page as the game reads it: single-level DXT5 under a fresh header, as the stock pages are.</summary>
    public byte[] ToXbt()
    {
        var rgba = new byte[Alpha.Length * 4];
        Span<uint> pixels = MemoryMarshal.Cast<byte, uint>(rgba.AsSpan());
        for (int i = 0; i < Alpha.Length; i++)
        {
            pixels[i] = 0x00FFFFFFu | ((uint)Alpha[i] << 24);
        }
        return new TextureDocument
        {
            Width = Width,
            Height = Height,
            Codec = "DXT5",
            Levels = 1,
            Header = XbtTexture.NewHeader(),
            Rgba = rgba,
        }.ToXbt().Base;
    }
}

/// <summary>A font made from a font file, and the game's characters the file lacks.</summary>
public sealed record BuiltFont(MftFont Font, IReadOnlyList<FontPage> Pages, IReadOnlyList<int> Missing);

/// <summary>
/// Draws a face of a TrueType or OpenType file into a Magma pixmap font: every character the game
/// uses, shelf-packed onto 1024-wide pages, each cell a pixel wider and taller than its glyph as the
/// stock fonts' are.
/// </summary>
public static class MftBuilder
{
    private const int PageWidth = 1024;
    private const int MaxPageHeight = 1024;

    /// <summary>Pixels between cells, against bleeding when a page is filtered.</summary>
    private const int Gap = 2;

    /// <summary>Every character the game's two fonts carry between them, Farcry2_25 and
    /// arial_cyrillic_25: the languages it ships in.</summary>
    public static readonly (int First, int Last)[] GameCharacters =
    [
        (0x0020, 0x007E), (0x00A0, 0x017F), (0x0192, 0x0192), (0x01FA, 0x01FF), (0x02C6, 0x02C7),
        (0x02C9, 0x02C9), (0x02D8, 0x02DD), (0x037E, 0x037E), (0x0384, 0x038A), (0x038C, 0x038C),
        (0x038E, 0x03A1), (0x03A3, 0x03CE), (0x0401, 0x040C), (0x040E, 0x044F), (0x0451, 0x045C),
        (0x045E, 0x045F), (0x0490, 0x0491), (0x1E80, 0x1E85), (0x1EF2, 0x1EF3), (0x2013, 0x2015),
        (0x2017, 0x201A), (0x201C, 0x201E), (0x2020, 0x2022), (0x2026, 0x2026), (0x2030, 0x2030),
        (0x2039, 0x203A), (0x2044, 0x2044), (0x2122, 0x2122), (0x2212, 0x2212), (0xFB01, 0xFB02),
    ];

    /// <param name="name">What the game knows the font by, such as <c>Caveat_40</c>.</param>
    /// <param name="source">The font file's name, recorded in the font.</param>
    /// <param name="axes">Design axis values by tag, for a variable font.</param>
    /// <param name="cyrillic">False to leave the Cyrillic characters out, for a smaller page.</param>
    public static BuiltFont Build(
        byte[] fontFile, int faceIndex, int pixelSize, string name, string source, IReadOnlyDictionary<string, float>? axes = null,
        bool cyrillic = true)
    {
        // Glyph 0 is the empty one a character the font lacks maps to.
        List<RasterGlyph> drawn = [new(MftFont.MissingCode, 0, 0, 0, 0, 0, [])];
        List<int> missing = [];
        using (var raster = new FontRaster(fontFile, faceIndex, pixelSize, axes))
        {
            foreach (int code in GameCharacters.SelectMany(r => Enumerable.Range(r.First, r.Last - r.First + 1))
                .Where(c => cyrillic || c is < 0x0400 or > 0x04FF))
            {
                if (raster.Draw(code) is { } glyph)
                {
                    drawn.Add(glyph);
                }
                else
                {
                    missing.Add(code);
                }
            }
        }

        (List<(int X, int Y, int Page)> places, List<int> heights) = Pack(drawn);
        var pages = heights.Select(h => new FontPage(PageWidth, h, new byte[PageWidth * h])).ToList();
        var font = new MftFont
        {
            Name = name,
            Source = source,
            Size = checked((byte)pixelSize),
            Pages = [.. pages.Select((_, i) => MftFont.PageName(name, i))],
        };
        foreach ((RasterGlyph g, (int x, int y, int page)) in drawn.Zip(places))
        {
            FontPage target = pages[page];
            for (int row = 0; row < g.Height; row++)
            {
                g.Coverage.AsSpan(row * g.Width, g.Width).CopyTo(target.Alpha.AsSpan(((y + row) * target.Width) + x));
            }
            font.Glyphs.Add(new MftGlyph(
                (ushort)g.Code, (ushort)g.Width, (ushort)g.Height, (short)g.Left, (short)g.Top, (ushort)Math.Max(g.Advance, 0),
                (x + 0.5f) / target.Width, (y + 0.5f) / target.Height,
                (x + g.Width + 1.5f) / target.Width, (y + g.Height + 1.5f) / target.Height, (ushort)page));
        }
        return new BuiltFont(font, pages, missing);
    }

    /// <summary>Where each glyph's cell goes, and each page's height: full for every page but the
    /// last, which is only as tall as it is filled, to a power of two.</summary>
    private static (List<(int X, int Y, int Page)> Places, List<int> Heights) Pack(List<RasterGlyph> glyphs)
    {
        List<(int, int, int)> places = [];
        List<int> heights = [];
        int x = 0, y = 0, shelf = 0;
        foreach (RasterGlyph g in glyphs)
        {
            if (g.Width + 1 > PageWidth || g.Height + 1 > MaxPageHeight)
            {
                throw new InvalidDataException($"U+{g.Code:X4} is drawn {g.Width}x{g.Height}, larger than a page.");
            }
            if (x + g.Width + 1 > PageWidth)
            {
                (x, y, shelf) = (0, y + shelf + Gap, 0);
            }
            if (y + g.Height + 1 > MaxPageHeight)
            {
                heights.Add(MaxPageHeight);
                (x, y, shelf) = (0, 0, 0);
            }
            places.Add((x, y, heights.Count));
            x += g.Width + 1 + Gap;
            shelf = Math.Max(shelf, g.Height + 1);
        }
        heights.Add((int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(y + shelf, 4)));
        return (places, heights);
    }
}
