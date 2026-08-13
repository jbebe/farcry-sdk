using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JackAll.Core;

/// <summary>A fixed-size wire record whose sort key packs into one unsigned value; composite keys
/// put the major component in the high bits (<see cref="BinaryTable.PackKey"/>).</summary>
internal interface IKeyedRecord
{
    ulong Key { get; }
}

/// <summary>
/// The mechanics shared by the byte-backed table files (<see cref="Vfs.GameCache"/>,
/// <see cref="Xrefs.ReferenceIndex"/>): zero-copy record spans over the one file buffer, binary
/// searches over their sorted keys, and the count-prefixed section framing both files use.
/// </summary>
internal static class BinaryTable
{
    /// <summary>Zero-copy record view over a section of <paramref name="fileBytes"/>.</summary>
    public static ReadOnlySpan<T> RecordSpan<T>(byte[] fileBytes, (int Offset, int Count) section) where T : struct
        => MemoryMarshal.Cast<byte, T>(fileBytes.AsSpan(section.Offset, section.Count * Unsafe.SizeOf<T>()));

    /// <summary>Packs a two-part sort key (see <see cref="IKeyedRecord"/>).</summary>
    public static ulong PackKey(uint major, uint minor) => ((ulong)major << 32) | minor;

    /// <summary>Index of the record whose key equals <paramref name="key"/>, or -1. Assumes unique
    /// keys — for a table with equal-key runs, use <see cref="LowerBound"/> and walk the run.</summary>
    public static int Find<T>(ReadOnlySpan<T> records, ulong key) where T : struct, IKeyedRecord
    {
        int i = LowerBound(records, key);
        return i < records.Length && records[i].Key == key ? i : -1;
    }

    /// <summary>First index whose key is not less than <paramref name="key"/> - a lower bound rather
    /// than an exact-match search, so an equal run is entered at its start and can be walked.</summary>
    public static int LowerBound<T>(ReadOnlySpan<T> records, ulong key) where T : struct, IKeyedRecord
    {
        int lo = 0, hi = records.Length;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (records[mid].Key < key) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>Reads a section's record count and skips past its bytes, returning where they sit.
    /// A count that doesn't fit the remaining stream throws here — inside the callers' "corrupt file
    /// yields an empty instance" catch — rather than surfacing at first query, long after load.</summary>
    public static (int Offset, int Count) ReadSection<T>(BinaryReader reader) where T : struct
    {
        int count = reader.ReadInt32();
        long offset = reader.BaseStream.Position;
        long end = offset + (long)count * Unsafe.SizeOf<T>();
        if (count < 0 || end > reader.BaseStream.Length)
        {
            throw new InvalidDataException($"Section of {count} records overruns the file.");
        }
        reader.BaseStream.Position = end;
        return ((int)offset, count);
    }

    /// <summary>Writes a record count followed by the records' raw bytes - the framing
    /// <see cref="ReadSection{T}"/> reads back.</summary>
    public static void WriteSection<T>(BinaryWriter writer, ReadOnlySpan<T> records) where T : struct
    {
        writer.Write(records.Length);
        writer.Write(MemoryMarshal.AsBytes(records));
    }
}

/// <summary>Writes a file via a temp sibling and an atomic rename, so a crash mid-write can't leave
/// a torn file behind to be read back as garbage next launch.</summary>
internal static class AtomicFile
{
    public static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".writing";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }
}
