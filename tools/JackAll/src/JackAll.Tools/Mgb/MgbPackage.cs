using System.Text;

namespace JackAll.Tools.Mgb;

/// <summary><c>VisitMaterial</c> (<c>0x0a0606a0</c>): a named texture reference plus the UV region
/// of it this material uses.</summary>
public sealed class MgbMaterial : MgbRecord
{
    public uint NameId;

    /// <summary>The texture path, as raw ANSI bytes.</summary>
    public byte[] TextureName = [];

    /// <summary>Four floats: the UV region, via <c>Material::SetRegion</c>.</summary>
    public uint[] RegionBits = new uint[4];

    public string TexturePath
    {
        get => MgbText.Ansi(TextureName);
        set => TextureName = MgbText.ToAnsi(value);
    }

    public float Region(int i) => BitConverter.UInt32BitsToSingle(RegionBits[i]);

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("name", ref NameId);
        c.AnsiString("texture", ref TextureName);
        c.F32BitsArray("REGION", RegionBits);
    }
}

/// <summary>One entry of <c>VisitPackage</c>'s first font loop: an embedded font blob registered
/// under a substitution name. The blob goes to <c>Font::Load</c>, which parses it out of the
/// already-read buffer and never touches the stream again.</summary>
public sealed class MgbFontSubst : MgbRecord
{
    public byte TypeSlot;
    public byte[] FontData = [];
    public byte[] SubstName = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.TypeSlot("slot", "type", ref TypeSlot, ctx.Types);
        c.AnsiString("fontData", ref FontData);
        c.AnsiString("FONTSUBST", ref SubstName);
    }
}

/// <summary>One entry of <c>VisitPackage</c>'s second font loop: a font referenced by name, looked
/// up via <c>Package::GetFontSubst</c> and otherwise requested from the font server.</summary>
public sealed class MgbFontRef : MgbRecord
{
    public byte TypeSlot;
    public byte[] Name = [];
    public byte[] FileName = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.TypeSlot("slot", "type", ref TypeSlot, ctx.Types);
        c.AnsiString("name", ref Name);
        c.AnsiString("file", ref FileName);
    }
}

/// <summary><c>VisitFontFamily</c> (<c>0x0a0615a0</c>) and its <c>LoadFont</c> (<c>0x0a061300</c>):
/// a deferred cross-package font reference. The owning package name is only on the wire when the
/// font name is non-empty.</summary>
public sealed class MgbFontFamily : MgbRecord
{
    public uint NameId;
    public byte[] FontName = [];
    public byte[] PackageName = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("name", ref NameId);
        c.AnsiString("font", ref FontName);
        if (FontName.Length != 0)
        {
            c.AnsiString("PACKAGE", ref PackageName);
        }
        else if (c.IsReading)
        {
            PackageName = [];
        }
    }
}

/// <summary><c>VisitStringResource</c> (<c>0x0a0611c0</c>): one localised string.</summary>
public sealed class MgbStringResource : MgbRecord
{
    public uint NameId;

    /// <summary>UTF-16 text.</summary>
    public byte[] Value = [];

    public string Text
    {
        get => MgbText.Utf16(Value);
        set => Value = MgbText.ToUtf16(value);
    }

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("name", ref NameId);
        c.Utf16String("STRINGRESOURCE", ref Value);
    }
}

/// <summary>
/// <c>VisitStringTable</c> (<c>0x0a05e9a0</c>): the package's optional string table.
/// </summary>
/// <remarks>
/// <c>VisitPackage</c> builds this through a hardcoded <c>Factory</c> slot rather than the type
/// table, so no type byte identifies it. That is why earlier reversing passes could only describe it
/// as an anonymous "global focus area", and - because it is usually empty - measured it as a fixed
/// 8-byte record.
/// </remarks>
public sealed class MgbStringTable : MgbRecord
{
    public uint NameId;
    public List<MgbStringResource> Strings = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("name", ref NameId);
        SerializeList(c, ctx, "STRINGS", "STRING", Strings);
    }
}

/// <summary><c>VisitGenericObject</c> (<c>0x0a05de70</c>): a named object holding one typed link
/// list.</summary>
public sealed class MgbGenericObject : MgbRecord
{
    public uint NameId;
    public MgbFullLink Link = new();

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("name", ref NameId);
        using (c.Scope("LINK"))
        {
            Link.Serialize(c, ctx);
        }
    }
}

/// <summary><c>VisitGenericObjectTable</c> (<c>0x0a05e7c0</c>): the package's second anonymous
/// trailing table, built through its own hardcoded <c>Factory</c> slot.</summary>
public sealed class MgbGenericObjectTable : MgbRecord
{
    public uint NameId;
    public List<MgbGenericObject> Objects = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("name", ref NameId);
        SerializeList(c, ctx, "GENERICOBJECTS", "GENERICOBJECT", Objects);
    }
}

/// <summary>
/// A whole <c>.mgb</c> package: the header, then everything <c>VisitPackage</c>
/// (<c>0x0a0619e0</c>) reads.
/// </summary>
/// <remarks>
/// Everything <c>VisitPackage</c> does after the default-material name - <c>ResolveLinks</c>, the
/// duplication and instancing passes, the second <c>Allocate*PoolChunk</c> sweep - is in-memory
/// post-processing that reads no further bytes, so a package ends exactly there.
/// </remarks>
public sealed class MgbPackage
{
    public const uint ExpectedVersion = 0x1EAB90;
    private static readonly byte[] Magic = "MAGMA"u8.ToArray();

    /// <summary>Bytes 5-8. Only byte 8 (<c>0xAB</c>) is checked by the engine; 5-7 are consumed and
    /// ignored, so they are kept verbatim rather than reconstructed.</summary>
    public byte[] Sentinel = [0xCD, 0x00, 0x00, 0xAB];

    public uint Version = ExpectedVersion;

    /// <summary>Header byte 13. Read through the engine's bool slot; purpose never identified.</summary>
    public bool Flag;

    /// <summary>Big-endian content (console builds), selected by the sentinel byte.</summary>
    public bool Invert;

    public MgbTypeTable Types { get; } = new();

    /// <summary>65 per-type instance counts feeding the <c>Allocate*PoolChunk</c> family. Pure
    /// memory-pool pre-reservation - no effect on any later offset.</summary>
    public uint[] PoolCounts = new uint[65];

    public MgbUserData UserData = new();

    public ushort PageWidth;
    public ushort PageHeight;
    public ushort DisplayOffsetX;
    public ushort DisplayOffsetY;

    /// <summary>A second <c>u32</c> after the material count. It is *not* a loop count - it is
    /// forwarded to a setter - so it is preserved verbatim rather than derived.</summary>
    public uint MaterialExtra;

    public List<MgbMaterial> Materials = [];
    public List<MgbFontSubst> FontSubsts = [];
    public List<MgbFontRef> FontRefs = [];
    public List<MgbFontFamily> FontFamilies = [];
    public List<MgbArea> Areas = [];
    public MgbStringTable? StringTable;
    public MgbGenericObjectTable? GenericObjectTable;

    /// <summary>Empty when the length field is zero.</summary>
    public byte[] DefaultMaterialName = [];

    public static MgbPackage Read(byte[] bytes)
    {
        if (bytes.Length < 15)
        {
            throw new MgbFormatException("file is too small to be a .mgb package");
        }
        if (!bytes.AsSpan(0, 5).SequenceEqual(Magic))
        {
            throw new MgbFormatException("not a .mgb package (missing \"MAGMA\" magic)");
        }

        var package = new MgbPackage
        {
            Sentinel = bytes.AsSpan(5, 4).ToArray(),
        };

        // Byte 8 is the endian marker. When it isn't 0xAB the engine discards its reader and builds
        // a BinaryInvertReadSerializer instead, then re-checks byte 5 as a second sanity byte.
        package.Invert = bytes[8] != 0xAB;
        if (package.Invert && bytes[5] != 0xAB)
        {
            throw new MgbFormatException(
                $"endian marker is 0x{bytes[8]:X2} and the byte-swapped sanity byte is 0x{bytes[5]:X2}, " +
                "neither of which is 0xAB");
        }

        var codec = new MgbReadCodec(bytes, package.Invert);
        byte[] scratch = [];
        codec.Blob("magic", ref scratch, 9); // magic + sentinel, already validated above

        package.SerializeBody(codec);

        if (codec.Remaining != 0)
        {
            throw new MgbFormatException(
                $"package ends at offset {codec.Position} but the file is {bytes.Length} bytes - " +
                $"{codec.Remaining} trailing bytes were not consumed");
        }
        return package;
    }

    public byte[] Write()
    {
        var codec = new MgbWriteCodec(Invert);
        byte[] magic = [.. Magic, .. Sentinel];
        codec.Blob("magic", ref magic, 9);
        SerializeBody(codec);
        return codec.ToArray();
    }

    /// <summary>The single description of everything past the 9-byte magic+sentinel prefix, shared
    /// by <see cref="Read"/>, <see cref="Write"/> and both directions of <see cref="MgbXml"/>.</summary>
    internal void SerializeBody(IMgbCodec c)
    {
        c.U32("version", ref Version);
        if (Version != ExpectedVersion)
        {
            throw new MgbFormatException(
                $"unsupported .mgb version 0x{Version:X6} (expected 0x{ExpectedVersion:X6}) - this file " +
                "was written by a different Magma build than the one this decoder was derived from");
        }
        c.Bool("flag", ref Flag);

        // A single count byte, then count-1 raw ids: the fill loop runs slots 1..N-1, so slot 0 is
        // never populated and a body type byte B names table entry B-1.
        int typeCount = Types.RawIds.Count;
        using (c.ListScope("TYPES", ref typeCount, MgbCountWidth.U8Plus1))
        {
            if (c.IsReading)
            {
                Types.RawIds.Clear();
                for (int i = 0; i < typeCount; i++)
                {
                    Types.RawIds.Add(0);
                }
            }
            for (int i = 0; i < Types.RawIds.Count; i++)
            {
                using (c.Item("TYPE"))
                {
                    // A class name is a CRC32 of a string exactly like an object name, so the same
                    // verified name-or-hash rendering covers both - which is also why the ~35 ids
                    // no name is known for survive as #XXXXXXXX rather than being lost.
                    uint id = Types.RawIds[i];
                    c.NameId("id", ref id);
                    Types.RawIds[i] = id;
                }
            }
        }

        var ctx = new MgbContext(Types);

        c.U32Array("POOLCOUNTS", PoolCounts);

        using (c.Scope("USERDATA"))
        {
            UserData.Serialize(c, ctx);
        }

        c.U16("PAGESIZE.w", ref PageWidth);
        c.U16("PAGESIZE.h", ref PageHeight);
        c.U16("DISPLAYOFFSET.x", ref DisplayOffsetX);
        c.U16("DISPLAYOFFSET.y", ref DisplayOffsetY);

        int materialCount = Materials.Count;
        using (c.ListScope("MATERIALS", ref materialCount))
        {
            // Sits between the count and the items on the wire, so it belongs to the list element
            // rather than to the package.
            c.U32("materialExtra", ref MaterialExtra);
            if (c.IsReading)
            {
                Materials.Clear();
                for (int i = 0; i < materialCount; i++)
                {
                    Materials.Add(new MgbMaterial());
                }
            }
            foreach (MgbMaterial material in Materials)
            {
                using (c.Item("Material"))
                {
                    material.Serialize(c, ctx);
                }
            }
        }

        MgbRecordHelpers.List(c, ctx, "FONTSUBSTS", "FONTSUBST", FontSubsts);
        MgbRecordHelpers.List(c, ctx, "FONTS", "FONT", FontRefs);
        MgbRecordHelpers.List(c, ctx, "FONTFAMILIES", "FONTFAMILY", FontFamilies);

        MgbArea.SerializeAreaList(c, ctx, Areas);

        MgbRecordHelpers.Optional(c, ctx, "STRINGTABLE", ref StringTable);
        MgbRecordHelpers.Optional(c, ctx, "GENERICOBJECTTABLE", ref GenericObjectTable);
        c.AnsiString("DEFAULTMATERIAL", ref DefaultMaterialName);
    }

    /// <summary>A one-line summary for CLI/UI headers.</summary>
    public string Describe(int byteLength) =>
        $"MAGMA v0x{Version:X6}{(Invert ? " (big-endian)" : "")}, " +
        $"{Types.RawIds.Count} type-table entries, " +
        $"page {PageWidth}x{PageHeight}, {Materials.Count} material(s), " +
        $"{FontSubsts.Count}/{FontRefs.Count}/{FontFamilies.Count} font records, " +
        $"{Areas.Count} area(s), {byteLength:N0} bytes";
}
