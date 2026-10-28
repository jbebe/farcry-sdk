using JackAll.Core.Format;

namespace JackAll.Tools.Mft;

/// <summary>
/// Text set in an <see cref="MftFont"/> from its pages the way Magma sets it: each glyph's cell
/// placed at the pen plus its left bearing, its top that far above the baseline, the pen moved on
/// by its advance.
/// </summary>
public static class MftPreview
{
    public const string SampleText = "The quick brown fox jumps over the lazy dog.\n"
        + "Àéîõü ÆØÅ ß ąčęłńšž «Привет» 0123456789 !?\"'()-+=/:;,.";

    /// <summary>The text in <paramref name="ink"/> on <paramref name="paper"/>, RGBA, lines one size apart.</summary>
    /// <param name="pages">Each page, in the font's page order.</param>
    public static (byte[] Rgba, int Width, int Height) Render(
        MftFont font, IReadOnlyList<FontPage> pages, string text, uint ink = 0xFF202020, uint paper = 0xFFE8DCC0)
    {
        Dictionary<ushort, MftGlyph> byCode = [];
        foreach (MftGlyph glyph in font.Glyphs)
        {
            byCode.TryAdd(glyph.Code, glyph);
        }

        int lineHeight = Math.Max((int)Math.Ceiling(font.Size * 1.25), font.Glyphs.Max(g => g.Height) + 2);
        string[] lines = text.Split('\n');
        int margin = font.Size / 2;
        int width = margin * 2 + lines.Max(line => line.Sum(c => byCode.TryGetValue(c, out MftGlyph g) ? g.Advance : 0));
        int height = margin * 2 + (lineHeight * lines.Length);
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = (byte)(paper >> 16);
            rgba[i + 1] = (byte)(paper >> 8);
            rgba[i + 2] = (byte)paper;
            rgba[i + 3] = 255;
        }

        for (int line = 0; line < lines.Length; line++)
        {
            int pen = margin;
            int baseline = margin + (line * lineHeight) + font.Size;
            foreach (char c in lines[line])
            {
                if (!byCode.TryGetValue(c, out MftGlyph g))
                {
                    continue;
                }

                FontPage page = pages[g.Page];
                int sourceX = (int)Math.Round((g.U0 * page.Width) - 0.5);
                int sourceY = (int)Math.Round((g.V0 * page.Height) - 0.5);
                for (int y = 0; y < g.Height; y++)
                {
                    for (int x = 0; x < g.Width; x++)
                    {
                        int tx = pen + g.Left + x, ty = baseline - g.Top + y;
                        if (tx < 0 || ty < 0 || tx >= width || ty >= height)
                        {
                            continue;
                        }
                        Blend(rgba, ((ty * width) + tx) * 4, ink, page.Alpha[((sourceY + y) * page.Width) + sourceX + x]);
                    }
                }
                pen += g.Advance;
            }
        }
        return (rgba, width, height);
    }

    private static void Blend(byte[] rgba, int at, uint ink, byte alpha)
    {
        for (int channel = 0; channel < 3; channel++)
        {
            int inkValue = (byte)(ink >> (16 - (8 * channel)));
            rgba[at + channel] = (byte)(rgba[at + channel] + (((inkValue - rgba[at + channel]) * alpha) / 255));
        }
    }
}
