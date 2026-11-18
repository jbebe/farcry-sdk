using System.Buffers.Binary;

namespace JackAll.Core.Format;

/// <summary>One record of a .spk sound bank as the container frames it.</summary>
public sealed record SpkFrame(uint Id, uint[] Preamble, byte[] Payload, byte[] Padding);

/// <summary>
/// The container of a Far Cry 2 .spk sound bank: the magic, the record count and the id table, then per
/// record its preamble words, its size and its payload, 4-byte aligned. JackAll.Tools reads the payloads.
/// </summary>
public static class SpkFraming
{
    public const uint Magic = 0x53504B01;

    public static IReadOnlyList<SpkFrame> Read(byte[] data)
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

        var frames = new List<SpkFrame>((int)count);
        var cursor = new ByteCursor(data);
        cursor.Position = recordsStart;
        for (int i = 0; i < count; i++)
        {
            uint preambleWordCount = cursor.ReadU32();
            if (preambleWordCount > 4096)
            {
                throw new InvalidDataException($"Implausible preamble word count {preambleWordCount} in record {i}.");
            }

            var preamble = new uint[preambleWordCount];
            for (int w = 0; w < preambleWordCount; w++)
            {
                preamble[w] = cursor.ReadU32();
            }

            uint size = cursor.ReadU32();
            if (size > int.MaxValue || cursor.Position + (long)size > data.Length)
            {
                throw new InvalidDataException(
                    $"Truncated payload in record {i} (wanted {size} bytes at 0x{cursor.Position:X}).");
            }

            byte[] payload = cursor.ReadBytes((int)size);
            // next record is 4-byte aligned
            byte[] padding = cursor.ReadBytes(Math.Min(PadLength(cursor.Position), cursor.Remaining));
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(idTableStart + i * 4));
            frames.Add(new SpkFrame(id, preamble, payload, padding));
        }
        return frames;
    }

    private static int PadLength(int length) => (4 - length % 4) % 4;
}
