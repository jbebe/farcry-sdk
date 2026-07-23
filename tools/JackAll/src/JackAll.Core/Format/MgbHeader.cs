namespace JackAll.Core.Format;

/// <summary>One entry in a .mgb file's own type table: a raw CRC32(ClassName) id, resolved to a
/// known name where <see cref="MgbTypeTable"/> recognizes it.</summary>
public readonly record struct MgbTypeEntry(uint RawId, string? Name);

/// <summary>
/// The confirmed-byte-for-byte portion of a .mgb (Magma UI binary) file: magic, version, and type
/// table. See reverse/dunia/mgb_format.md, "Header - confirmed byte-for-byte".
/// </summary>
/// <remarks>
/// Deliberately stops at the header. Everything past it - the actual widget/animation tree - starts
/// with <c>VisitPackage</c>'s own material/font/area preamble, which hasn't been reverse-engineered to
/// byte precision yet (only its rough shape is documented); parsing past this point without that would
/// desync on the very first file and produce garbage, not a partial result. Extend this decoder only
/// once that gap is closed - see the doc's "Not yet traced" section.
/// </remarks>
public sealed record MgbHeader
{
    /// <summary>The format/build version every known .mgb file must match - see the doc for why this
    /// is also the version <c>.mgb.desc</c>'s XML loader checks.</summary>
    public const uint ExpectedVersion = 0x1EAB90;

    public required uint Version { get; init; }

    /// <summary>Byte 13, read via the reader's <c>ReadBool</c> slot. Purpose not identified.</summary>
    public required byte FlagByte { get; init; }

    public required IReadOnlyList<MgbTypeEntry> Types { get; init; }

    /// <summary>File offset where the header ends and the (currently undecoded) body begins.</summary>
    public required int HeaderLength { get; init; }

    public static MgbHeader Decode(byte[] mgb)
    {
        if (mgb.Length < 15)
        {
            throw new InvalidDataException("File too small to be a .mgb package.");
        }
        if (mgb[0] != (byte)'M' || mgb[1] != (byte)'A' || mgb[2] != (byte)'G' || mgb[3] != (byte)'M' || mgb[4] != (byte)'A')
        {
            throw new InvalidDataException("Not a .mgb package (missing \"MAGMA\" magic).");
        }
        if (mgb[8] != 0xAB)
        {
            throw new InvalidDataException(
                "Unsupported .mgb sentinel byte at offset 8 - the engine's fallback reader path for " +
                "this case isn't reverse-engineered.");
        }

        uint version = (uint)(mgb[9] | (mgb[10] << 8) | (mgb[11] << 16) | (mgb[12] << 24));
        if (version != ExpectedVersion)
        {
            throw new InvalidDataException(
                $"Unsupported .mgb version 0x{version:X6} (expected 0x{ExpectedVersion:X6}) - this file " +
                "was saved by a different Magma build than the one this parser was reverse-engineered against.");
        }

        byte flagByte = mgb[13];
        byte typeCountByte = mgb[14];
        if (typeCountByte == 0)
        {
            throw new InvalidDataException("Type-table count byte is 0 - corrupt .mgb header.");
        }

        int entryCount = typeCountByte - 1;
        const int typeTableOffset = 15;
        int typeTableEnd = typeTableOffset + entryCount * 4;
        if (mgb.Length < typeTableEnd)
        {
            throw new InvalidDataException("File is smaller than its own declared type table.");
        }

        var types = new MgbTypeEntry[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            int offset = typeTableOffset + i * 4;
            uint rawId = (uint)(mgb[offset] | (mgb[offset + 1] << 8) | (mgb[offset + 2] << 16) | (mgb[offset + 3] << 24));
            types[i] = new MgbTypeEntry(rawId, MgbTypeTable.Resolve(rawId));
        }

        return new MgbHeader
        {
            Version = version,
            FlagByte = flagByte,
            Types = types,
            HeaderLength = typeTableEnd,
        };
    }
}
