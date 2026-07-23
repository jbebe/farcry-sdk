using System.Buffers.Binary;

namespace JackAll.Core.Format;

/// <summary>One sub-object packed inside an .spk - a hash-identified, variable-length record. The
/// payload's own internal layout (observed: a small format code, a length field, and a 16-byte
/// high-entropy block) is not decoded here - see <see cref="SpkPackage"/>'s remarks.</summary>
public sealed class SpkRecord
{
    public required uint Id { get; init; }
    public required IReadOnlyList<uint> PreambleWords { get; init; }
    public required byte[] Payload { get; init; }
}

/// <summary>
/// Reads the container structure of a Far Cry 2 .spk sound-bank file - the header, the record count,
/// the id table, and each record's own preamble/size/payload framing - for preview purposes. Every
/// record's payload is kept as an opaque byte blob: unlike .xbg/.xbm (which have a fully-documented
/// community parser to port), .spk's per-record payload is registered by the engine as a raw
/// {id, pointer, size} triple at load time and only interpreted later, lazily, by whatever actually
/// triggers playback - a part of the pipeline this class doesn't reach into.
///
/// Traced live via GhidraMCP against Dunia.dll (client): the real (non-stub) parser is reached through
/// a virtual call inside the sound-resource loader, at the vtable slot resolved from
/// research/knowledge.md's already-documented `GetFileNameFromSoundId`/`CSoundLoader` naming path.
/// Container layout, confirmed against real shipped .spk files byte-for-byte:
/// <code>
/// u32 magic = 0x53504B01   ("KPS" + a version byte, reversed-FourCC like .xbg/.xbm)
/// u32 count
/// u32[count] ids           // one hash-style id per record, same table order as the records below
/// count x {
///     u32 preambleWordCount (N)
///     u32[N] preambleWords  // meaning not established - often echoes this record's own id
///     u32 size
///     u8[size] payload      // 4-byte aligned before the next record
/// }
/// </code>
/// </summary>
public sealed class SpkPackage
{
    public const uint Magic = 0x53504B01;

    public required IReadOnlyList<SpkRecord> Records { get; init; }

    public static SpkPackage Parse(byte[] data)
    {
        if (data.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0)) != Magic)
        {
            throw new InvalidDataException(
                "Not a Far Cry 2 .spk (no 0x53504B01 header) - this viewer doesn't support this file's format.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        if (count > 1_000_000)
        {
            throw new InvalidDataException($"Implausible record count {count}.");
        }

        int idTableStart = 8;
        int recordsStart = idTableStart + checked((int)count * 4);
        if (recordsStart > data.Length)
        {
            throw new InvalidDataException("Truncated id table.");
        }

        var ids = new uint[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(idTableStart + i * 4));
        }

        var records = new List<SpkRecord>((int)count);
        int pos = recordsStart;
        for (int i = 0; i < count; i++)
        {
            uint preambleWordCount = ReadU32(data, ref pos);
            if (preambleWordCount > 4096)
            {
                throw new InvalidDataException($"Implausible preamble word count {preambleWordCount} in record {i}.");
            }

            var preamble = new uint[preambleWordCount];
            for (int w = 0; w < preambleWordCount; w++)
            {
                preamble[w] = ReadU32(data, ref pos);
            }

            uint size = ReadU32(data, ref pos);
            if (size > int.MaxValue || pos + (long)size > data.Length)
            {
                throw new InvalidDataException($"Truncated payload in record {i} (wanted {size} bytes at 0x{pos:X}).");
            }

            byte[] payload = data[pos..(pos + (int)size)];
            pos += (int)size;
            pos += (4 - pos % 4) % 4; // next record is 4-byte aligned

            records.Add(new SpkRecord { Id = ids[i], PreambleWords = preamble, Payload = payload });
        }

        return new SpkPackage { Records = records };
    }

    private static uint ReadU32(byte[] data, ref int pos)
    {
        if (pos < 0 || pos + 4 > data.Length)
        {
            throw new InvalidDataException($"Ran out of bytes at offset 0x{pos:X} (needed 4).");
        }

        uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
        pos += 4;
        return v;
    }
}
