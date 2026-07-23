namespace JackAll.Core.Naming;

/// <summary>What a file turned out to be: a broad category plus the extension it should carry.</summary>
public readonly record struct FileType(string Category, string Extension)
{
    public static readonly FileType Unknown = new("unknown", "bin");
    public override string ToString() => Extension;
}

/// <summary>
/// Identifies a file from its first bytes.
/// </summary>
/// <remarks>
/// This is what makes the ~unnamed entries in the archives workable rather than opaque blobs: an
/// entry whose hash we can't reverse still gets a real category and extension, so it lands in the
/// right place in the tree and gets the right actions (a texture is previewable, a sound is
/// playable) even though nobody knows what it's called.
///
/// The magic table is ported from Gibbed's FileDetection, which is the accumulated result of the
/// community actually identifying these formats.
/// </remarks>
public static class FileTypeSniffer
{
    /// <summary>Bytes needed for a confident guess.</summary>
    public const int HeaderBytes = 64;

    /// <summary>
    /// Identifies by content. <paramref name="knownPath"/>, when we have it, wins — a real filename
    /// is better evidence than a magic number.
    /// </summary>
    public static FileType Identify(ReadOnlySpan<byte> header, string? knownPath = null)
    {
        if (!string.IsNullOrEmpty(knownPath))
        {
            string ext = Path.GetExtension(knownPath).TrimStart('.').ToLowerInvariant();
            if (ext.Length > 0)
            {
                return new FileType(CategoryForExtension(ext), ext);
            }
        }
        return IdentifyByContent(header);
    }

    public static FileType IdentifyByContent(ReadOnlySpan<byte> header)
    {
        if (header.Length == 0)
        {
            return new FileType("empty", "bin");
        }

        // Formats whose magic sits at a fixed offset other than 0 have to be checked before the
        // generic magic-at-0 scan, or a coincidental first word would shadow them.
        if (StartsWith(header, "MAGMA")) return new FileType("ui", "mgb");
        if (StartsWith(header, "BIK")) return new FileType("video", "bik");
        if (StartsWith(header, "UEF")) return new FileType("ui", "feu");
        if (header.Length >= 15 && StartsWith(header, "SQLite format 3")) return new FileType("db", "sqlite3");

        if (header.Length >= 20 && Matches(header, 16, 'W', 0xE0, 0xE0, 'W')) return new FileType("physics", "hkx");
        if (header.Length >= 48 && Matches(header, 44, 'S', 'D', 'K', 'V')) return new FileType("physics", "hkx");
        if (header.Length >= 24 && Matches(header, 20, 0xC8, 0xEF, 0x1D, 0x3E)) return new FileType("terrain", "terrainnode.bdl");

        // Some containers carry their magic at +16 or +4 rather than at 0.
        foreach (int offset in (int[])[16, 4, 0])
        {
            if (header.Length < offset + 4) continue;
            uint magic = ReadU32(header, offset);
            FileType? byMagic = FromMagic(magic) ?? FromMagic(Swap(magic));
            if (byMagic is not null) return byMagic.Value;
        }

        // Text-ish formats last: an XML declaration or a Lua comment is only meaningful once every
        // binary signature has been ruled out.
        if (header.Length >= 8 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)
        {
            return new FileType("misc", "xml");
        }
        if (StartsWith(header, "<?xml") || StartsWith(header, "<root>")) return new FileType("misc", "xml");
        if (StartsWith(header, "<package>")) return new FileType("ui", "mgb.desc");
        if (StartsWith(header, "-- ")) return new FileType("scripts", "lua");
        if (header.Length >= 3 && header[0] == 0 && header[1] == 0 && header[2] == 0xFF)
        {
            return new FileType("misc", "rml"); // binary XML
        }
        if (header.Length >= 2 && header[0] == 'p' && header[1] == 'A') return new FileType("animations", "dpax");

        return FileType.Unknown;
    }

    private static FileType? FromMagic(uint magic) => magic switch
    {
        0x00584254 => new FileType("textures", "xbt"),   // '\0XBT'
        // 'MESH' - .xbm materials share this exact container header/magic with .xbg meshes (see
        // XbmMaterial's remarks), so an unnamed entry always falls back to xbg here; only a known
        // path (checked before content-sniffing, see Identify) can tell the two apart.
        0x4D455348 => new FileType("meshes", "xbg"),
        0x47454F4D => new FileType("meshes", "xbg"),     // 'GEOM'
        0x474D4950 => new FileType("meshes", "xbgmip"),  // 'GMIP'
        0x54414D00 => new FileType("materials", "material.bin"),
        // Confirmed via GhidraMCP against Dunia.dll's real sound-bank loader (reverse/dunia - the
        // engine itself rejects anything whose first 4 bytes aren't exactly this): a version byte
        // (0x01) followed by "KPS" - "SPK" stored reversed, same FourCC convention as .xbg/.xbm.
        0x53504B01 => new FileType("sounds", "spk"),
        0x00032A02 => new FileType("sounds", "sbao"),
        0x46464952 => new FileType("sounds", "wem"),     // 'RIFF'
        0x4643626E => new FileType("data", "fcb"),       // 'FCbn'
        0x45534142 => new FileType("data", "wlu.fcb"),   // 'BASE'
        0x61754C1B => new FileType("scripts", "luab"),   // '\x1BLua'
        0x4341554C => new FileType("scripts", "luac"),   // 'LUAC'
        0x474E5089 => new FileType("textures", "png"),
        0x4F54544F => new FileType("fonts", "otf"),
        0x43425844 => new FileType("shaders", "bin"),    // 'DXBC'
        0x534E644E => new FileType("navmesh", "rnv"),    // 'SNdN'
        0x4D564D00 => new FileType("navmesh", "mvn"),
        0x53435452 => new FileType("terrain", "sctr"),
        0xE9001052 => new FileType("terrain", "sdat"),  // world-sector chunk (CSector::ExportSectorDataChunk)
        0x54524545 => new FileType("terrain", "tree"),
        0x42544348 => new FileType("terrain", "cbatch"),
        0x5374726D => new FileType("streams", "bin"),    // 'Strm'
        0x00014C53 => new FileType("languages", "loc"),
        _ => null,
    };

    private static string CategoryForExtension(string ext) => ext switch
    {
        "xbt" or "dds" or "png" or "tga" => "textures",
        "xbg" or "xbgmip" => "meshes",
        "xbm" => "materials",
        "spk" or "sbao" or "wem" or "wav" => "sounds",
        "fcb" => "data",
        "lua" or "luab" or "luac" => "scripts",
        "xml" or "rml" => "misc",
        "bik" => "video",
        "hkx" => "physics",
        "mgb" or "feu" or "desc" => "ui",
        "sdat" => "terrain",
        _ => "misc",
    };

    private static uint ReadU32(ReadOnlySpan<byte> b, int offset)
        => (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));

    private static uint Swap(uint v)
        => (v >> 24) | ((v >> 8) & 0xFF00) | ((v << 8) & 0xFF0000) | (v << 24);

    private static bool StartsWith(ReadOnlySpan<byte> header, string ascii)
    {
        if (header.Length < ascii.Length) return false;
        for (int i = 0; i < ascii.Length; i++)
        {
            if (header[i] != (byte)ascii[i]) return false;
        }
        return true;
    }

    private static bool Matches(ReadOnlySpan<byte> header, int offset, params int[] expected)
    {
        if (header.Length < offset + expected.Length) return false;
        for (int i = 0; i < expected.Length; i++)
        {
            if (header[offset + i] != (byte)expected[i]) return false;
        }
        return true;
    }
}
