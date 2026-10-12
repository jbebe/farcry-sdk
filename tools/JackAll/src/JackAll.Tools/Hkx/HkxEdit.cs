using System.Buffers.Binary;
using JackAll.Core.Format;

namespace JackAll.Tools.Hkx;

/// <summary>
/// Reads and writes an object's members by <see cref="HkSlot.Path"/>, such as <c>motion.motionState.transform</c>
/// or <c>entities[1]</c>: the paths <see cref="HkxDump"/> prints. Values are written as the 32-bit little-endian
/// layout stores them.
/// </summary>
public static class HkxEdit
{
    /// <summary>Arrays a packfile saves carry this in their capacity word, beside the element count.</summary>
    private const uint SavedArrayFlags = 0xC0000000;

    public static HkxObject Create(string className)
    {
        HkClass c = HavokClasses.Default[className];
        return new HkxObject(c, new byte[c.Size]);
    }

    /// <summary>Every float the member holds: one for a real, four for a vector, sixteen for a transform.</summary>
    public static float[] GetReals(this HkxObject o, string path)
    {
        HkSlot s = Slot(o, path);
        return [.. Enumerable.Range(0, s.Size / 4).Select(i => ByteCursor.F32(o.Bytes, s.Offset + i * 4))];
    }

    public static void SetReals(this HkxObject o, string path, params float[] values)
    {
        int at = Locate(o, path, values.Length * 4);
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(o.Bytes.AsSpan(at + i * 4), values[i]);
        }
    }

    /// <summary>Writes a one-byte member: a bool, an 8-bit integer or enum, or a packed <c>hkUFloat8</c>.</summary>
    public static void SetByte(this HkxObject o, string path, byte value) => o.Bytes[Locate(o, path, 1)] = value;

    public static HkxObject? GetPointer(this HkxObject o, string path)
        => o.Refs.GetValueOrDefault(Locate(o, path, 4)) is HkxPointer p ? p.Target : null;

    public static void SetPointer(this HkxObject o, string path, HkxObject? target)
    {
        int at = Locate(o, path, 4);
        if (target is null)
        {
            o.Refs.Remove(at);
        }
        else
        {
            o.Refs[at] = new HkxPointer(target);
        }
    }

    public static string? GetString(this HkxObject o, string path) => (o.Refs.GetValueOrDefault(Locate(o, path, 4)) as HkxData)?.Text;

    public static void SetString(this HkxObject o, string path, string text) => o.Refs[Locate(o, path, 4)] = HkxData.FromText(text);

    /// <summary>What an array of pointers points at, in order.</summary>
    public static List<HkxObject?> GetPointers(this HkxObject o, string path)
    {
        int at = Locate(o, path, 12);
        HkxBlock? elements = (o.Refs.GetValueOrDefault(at) as HkxData)?.Block;
        return [.. Enumerable.Range(0, ByteCursor.I32(o.Bytes, at + 4))
            .Select(i => (elements?.Refs.GetValueOrDefault(i * 4) as HkxPointer)?.Target)];
    }

    /// <summary>Puts an element into an array of pointers at <paramref name="index"/>, moving those from there on up.</summary>
    public static void Insert(this HkxObject o, string path, int index, HkxObject element)
    {
        int at = Locate(o, path, 12);
        int count = ByteCursor.I32(o.Bytes, at + 4);
        var elements = new HkxBlock(new byte[(count + 1) * 4]);
        foreach ((int offset, HkxRef r) in (o.Refs.GetValueOrDefault(at) as HkxData)?.Block.Refs ?? [])
        {
            elements.Refs[offset < index * 4 ? offset : offset + 4] = r;
        }
        elements.Refs[index * 4] = new HkxPointer(element);
        o.Refs[at] = new HkxData(elements);
        BinaryPrimitives.WriteInt32LittleEndian(o.Bytes.AsSpan(at + 4), count + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(o.Bytes.AsSpan(at + 8), (uint)(count + 1) | SavedArrayFlags);
    }

    public static void Append(this HkxObject o, string path, HkxObject element)
        => o.Insert(path, ByteCursor.I32(o.Bytes, Locate(o, path, 12) + 4), element);

    private static HkSlot Slot(HkxObject o, string path)
        => HavokClasses.Default.Slots(o.Class).FirstOrDefault(s => s.Path == path) ?? throw new KeyNotFoundException($"{o.Class.Name} has no member '{path}'.");

    // The member's offset in the object, checked to hold `size` bytes.
    private static int Locate(HkxObject o, string path, int size)
    {
        HkSlot s = Slot(o, path);
        return size <= s.Size ? s.Offset : throw new ArgumentException($"'{path}' holds {s.Size} bytes, not {size}.");
    }
}
