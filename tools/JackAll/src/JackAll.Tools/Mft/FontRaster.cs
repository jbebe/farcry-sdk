using System.Runtime.InteropServices;
using FreeTypeSharp;
using static FreeTypeSharp.FT;
using static FreeTypeSharp.FT_LOAD;

namespace JackAll.Tools.Mft;

/// <summary>One font a font file holds: a face of a collection, or a named style of a variable font.</summary>
/// <param name="Index">What FreeType opens it by: the face in the low 16 bits, the named style above.</param>
/// <param name="Axes">Its design axes, empty when it is not a variable font.</param>
public sealed record FontFace(int Index, string Family, string Style, IReadOnlyList<FontAxis> Axes)
{
    /// <summary>What the game knows it by when drawn at this size, unless named otherwise: <c>Caveat_40</c>.</summary>
    public string DefaultName(int size) => $"{Family.Replace(" ", "")}_{size}";

    public override string ToString() => $"{Family} {Style}";
}

/// <summary>A design axis of a variable font, such as its weight, <c>wght</c>.</summary>
/// <param name="Value">Where the face opened sits on it: a named style's own value.</param>
public sealed record FontAxis(string Tag, string Name, float Minimum, float Default, float Maximum, float Value);

/// <summary>A glyph as FreeType draws it: coverage, one byte per pixel, rows first.</summary>
public sealed record RasterGlyph(int Code, int Width, int Height, int Left, int Top, int Advance, byte[] Coverage);

/// <summary>
/// One face of a TrueType or OpenType file, opened with FreeType.
/// </summary>
public sealed unsafe class FontRaster : IDisposable
{
    private readonly FT_LibraryRec_* _library;
    private readonly FT_FaceRec_* _face;
    private GCHandle _data;

    /// <param name="pixelSize">The size to draw at; 0 to only read the face.</param>
    /// <param name="axes">Design axis values by tag, for a variable font; the face's own for the rest.</param>
    public FontRaster(byte[] fontFile, int faceIndex, int pixelSize = 0, IReadOnlyDictionary<string, float>? axes = null)
    {
        // FreeType reads a memory face for as long as it is open.
        _data = GCHandle.Alloc(fontFile, GCHandleType.Pinned);
        try
        {
            FT_LibraryRec_* library;
            Check(FT_Init_FreeType(&library), "start FreeType");
            _library = library;
            FT_FaceRec_* face;
            Check(FT_New_Memory_Face(_library, (byte*)_data.AddrOfPinnedObject(), fontFile.Length, faceIndex, &face),
                "open the font (is it a TrueType or OpenType file?)");
            _face = face;
            if (axes is { Count: > 0 })
            {
                SetAxes(axes);
            }
            if (pixelSize > 0)
            {
                Check(FT_Set_Pixel_Sizes(_face, 0, (uint)pixelSize), $"size the font at {pixelSize} px");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Every font the file holds: each face of a collection, and each named style of a
    /// variable one in place of its default.</summary>
    public static IReadOnlyList<FontFace> Faces(byte[] fontFile)
    {
        int count;
        using (var probe = new FontRaster(fontFile, -1))
        {
            count = (int)probe._face->num_faces;
        }

        List<FontFace> faces = [];
        for (int i = 0; i < count; i++)
        {
            using var face = new FontRaster(fontFile, i);
            int styles = (int)((long)face._face->style_flags >> 16);
            if (styles == 0)
            {
                faces.Add(face.Describe(i));
            }
            for (int s = 1; s <= styles; s++)
            {
                using var named = new FontRaster(fontFile, (s << 16) | i);
                faces.Add(named.Describe((s << 16) | i));
            }
        }
        return faces;
    }

    /// <summary>The glyph for a character, or null when the font has none.</summary>
    public RasterGlyph? Draw(int code)
    {
        if (FT_Get_Char_Index(_face, (nuint)code) == 0)
        {
            return null;
        }
        Check(FT_Load_Char(_face, (nuint)code, FT_LOAD_RENDER), $"draw U+{code:X4}");

        FT_GlyphSlotRec_* slot = _face->glyph;
        int width = (int)slot->bitmap.width;
        int height = (int)slot->bitmap.rows;
        var coverage = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            new ReadOnlySpan<byte>(slot->bitmap.buffer + (y * slot->bitmap.pitch), width).CopyTo(coverage.AsSpan(y * width));
        }
        return new RasterGlyph(code, width, height, slot->bitmap_left, slot->bitmap_top,
            (int)Math.Round((long)slot->advance.x / 64.0), coverage);
    }

    public void Dispose()
    {
        FT_Done_Face(_face);
        FT_Done_FreeType(_library);
        _data.Free();
    }

    private FontFace Describe(int index) => new(index, Text(_face->family_name), Text(_face->style_name), ReadAxes());

    private List<FontAxis> ReadAxes()
    {
        if (((long)_face->face_flags & (long)FT_FACE_FLAG.FT_FACE_FLAG_MULTIPLE_MASTERS) == 0)
        {
            return [];
        }

        MmVar* master;
        Check(FT_Get_MM_Var(_face, &master), "read the font's design axes");
        try
        {
            var values = new nint[master->AxisCount];
            fixed (nint* current = values)
            {
                Check(FT_Get_Var_Design_Coordinates(_face, master->AxisCount, current), "read the font's design coordinates");
            }

            List<FontAxis> axes = [];
            for (int i = 0; i < master->AxisCount; i++)
            {
                VarAxis axis = master->Axes[i];
                string tag = string.Concat(Enumerable.Range(0, 4).Select(b => (char)(((ulong)axis.Tag >> (24 - (8 * b))) & 0xFF)));
                axes.Add(new FontAxis(tag, Text(axis.Name), Float(axis.Minimum), Float(axis.Default), Float(axis.Maximum), Float(values[i])));
            }
            return axes;
        }
        finally
        {
            FT_Done_MM_Var(_library, master);
        }
    }

    private void SetAxes(IReadOnlyDictionary<string, float> values)
    {
        List<FontAxis> axes = ReadAxes();
        if (values.Keys.FirstOrDefault(tag => axes.All(a => a.Tag != tag)) is { } unknown)
        {
            throw new InvalidDataException(
                $"The font has no '{unknown}' axis. It has: {(axes.Count == 0 ? "none" : string.Join(", ", axes.Select(a => a.Tag)))}.");
        }

        nint[] coordinates = [.. axes.Select(a =>
            (nint)Math.Round(Math.Clamp(values.GetValueOrDefault(a.Tag, a.Value), a.Minimum, a.Maximum) * 65536.0))];
        fixed (nint* set = coordinates)
        {
            Check(FT_Set_Var_Design_Coordinates(_face, (uint)axes.Count, set), "set the font's design coordinates");
        }
    }

    /// <summary>A 16.16 fixed-point value.</summary>
    private static float Float(nint fixedPoint) => fixedPoint / 65536f;

    // ftmm.h, which the binding leaves out. Its FT_Fixed and FT_ULong are pointer-sized, as the
    // binding's other longs are in the FreeType it ships.
    [StructLayout(LayoutKind.Sequential)]
    private struct VarAxis
    {
        public byte* Name;
        public nint Minimum;
        public nint Default;
        public nint Maximum;
        public nuint Tag;
        public uint StringId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MmVar
    {
        public uint AxisCount;
        public uint DesignCount;
        public uint NamedStyleCount;
        public VarAxis* Axes;
        public void* NamedStyles;
    }

    [DllImport("freetype")]
    private static extern FT_Error FT_Get_MM_Var(FT_FaceRec_* face, MmVar** master);

    [DllImport("freetype")]
    private static extern FT_Error FT_Done_MM_Var(FT_LibraryRec_* library, MmVar* master);

    [DllImport("freetype")]
    private static extern FT_Error FT_Get_Var_Design_Coordinates(FT_FaceRec_* face, uint count, nint* coordinates);

    [DllImport("freetype")]
    private static extern FT_Error FT_Set_Var_Design_Coordinates(FT_FaceRec_* face, uint count, nint* coordinates);

    private static string Text(byte* text) => Marshal.PtrToStringUTF8((nint)text) ?? "";

    private static void Check(FT_Error error, string what)
    {
        if (error != FT_Error.FT_Err_Ok)
        {
            throw new InvalidDataException($"FreeType could not {what}: {error}.");
        }
    }
}
