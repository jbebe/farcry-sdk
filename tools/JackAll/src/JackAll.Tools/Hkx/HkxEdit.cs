using System.Buffers.Binary;
using JackAll.Core.Format;

namespace JackAll.Tools.Hkx;

/// <summary>
/// Reads and writes an object's members by <see cref="HkSlot.Path"/>, such as <c>motion.motionState.transform</c>
/// or <c>entities[1]</c>. An index after an array member reaches into its elements, as in
/// <c>childInfo[2].shape</c>. Values are written as the 32-bit little-endian layout stores them.
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
        (HkxBlock block, int at, HkSlot s) = Locate(o, path);
        return Reals(block.Bytes.AsSpan(at, s.Size));
    }

    public static void SetReals(this HkxObject o, string path, params float[] values)
    {
        (HkxBlock block, int at, _) = Locate(o, path, values.Length * 4);
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(block.Bytes.AsSpan(at + i * 4), values[i]);
        }
    }

    /// <summary>An integer, enum or bool member, widened.</summary>
    public static int GetInt(this HkxObject o, string path)
    {
        (HkxBlock block, int at, HkSlot s) = Locate(o, path);
        return Integer(block.Bytes.AsSpan(at), s.Member.Type is HkType.Enum or HkType.Flags ? s.Member.Subtype : s.Member.Type);
    }

    /// <summary>Writes a one-byte member: a bool, an 8-bit integer or enum, or a packed <c>hkUFloat8</c>.</summary>
    public static void SetByte(this HkxObject o, string path, byte value)
    {
        (HkxBlock block, int at, _) = Locate(o, path, 1);
        block.Bytes[at] = value;
    }

    public static HkxObject? GetPointer(this HkxObject o, string path)
    {
        (HkxBlock block, int at, _) = Locate(o, path, 4);
        return block.Refs.GetValueOrDefault(at) is HkxPointer p ? p.Target : null;
    }

    public static void SetPointer(this HkxObject o, string path, HkxObject? target)
    {
        (HkxBlock block, int at, _) = Locate(o, path, 4);
        if (target is null)
        {
            block.Refs.Remove(at);
        }
        else
        {
            block.Refs[at] = new HkxPointer(target);
        }
    }

    public static string? GetString(this HkxObject o, string path)
    {
        (HkxBlock block, int at, _) = Locate(o, path, 4);
        return (block.Refs.GetValueOrDefault(at) as HkxData)?.Text;
    }

    public static void SetString(this HkxObject o, string path, string text)
    {
        (HkxBlock block, int at, _) = Locate(o, path, 4);
        block.Refs[at] = HkxData.FromText(text);
    }

    public static int Count(this HkxObject o, string path) => Array(o, path).Count;

    /// <summary>What an array of pointers points at, in order.</summary>
    public static List<HkxObject?> GetPointers(this HkxObject o, string path)
    {
        (_, _, _, HkxBlock? elements, int count) = Array(o, path);
        return [.. Enumerable.Range(0, count).Select(i => (elements?.Refs.GetValueOrDefault(i * 4) as HkxPointer)?.Target)];
    }

    /// <summary>Every float an array's elements hold, one element after another: reals, vectors, or structs of them.</summary>
    public static float[] GetRealArray(this HkxObject o, string path)
    {
        (_, _, HkSlot s, HkxBlock? elements, _) = Array(o, path);
        HkMember m = s.Member;
        bool reals = m.Subtype == HkType.Struct
            ? HavokClasses.Default.Slots(HavokClasses.Default[m.Class!]).All(e => IsReal(e.Member.Type))
            : IsReal(m.Subtype);
        return reals ? Reals(elements?.Bytes ?? []) : throw new ArgumentException($"'{path}' does not hold reals.");
    }

    /// <summary>An array of integers or enums, widened.</summary>
    public static int[] GetIntArray(this HkxObject o, string path)
    {
        (_, _, HkSlot s, HkxBlock? elements, int count) = Array(o, path);
        int size = HavokClasses.Default.SizeOf(s.Member.Subtype, HkType.Void, null);
        return [.. Enumerable.Range(0, count).Select(i => Integer(elements!.Bytes.AsSpan(i * size), s.Member.Subtype))];
    }

    /// <summary>Puts an element into an array of pointers at <paramref name="index"/>, moving those from there on up.</summary>
    public static void Insert(this HkxObject o, string path, int index, HkxObject element)
    {
        (HkxBlock block, int at, HkSlot s, HkxBlock? old, int count) = Array(o, path);
        var elements = new HkxBlock(new byte[(count + 1) * 4]);
        foreach ((int offset, HkxRef r) in old?.Refs ?? [])
        {
            elements.Refs[offset < index * 4 ? offset : offset + 4] = r;
        }
        elements.Refs[index * 4] = new HkxPointer(element);
        block.Refs[at] = new HkxData(elements);
        BinaryPrimitives.WriteInt32LittleEndian(block.Bytes.AsSpan(at + 4), count + 1);
        if (s.Member.Type == HkType.Array)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.Bytes.AsSpan(at + 8), (uint)(count + 1) | SavedArrayFlags);
        }
    }

    public static void Append(this HkxObject o, string path, HkxObject element) => o.Insert(path, o.Count(path), element);

    internal static int Integer(ReadOnlySpan<byte> b, HkType type) => type switch
    {
        HkType.Bool or HkType.UInt8 => b[0],
        HkType.Char or HkType.Int8 => (sbyte)b[0],
        HkType.Int16 => BinaryPrimitives.ReadInt16LittleEndian(b),
        HkType.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(b),
        _ => ByteCursor.I32(b, 0),
    };

    internal static bool IsReal(HkType type) => type is HkType.Real or HkType.Vector4 or HkType.Quaternion or HkType.Matrix3
        or HkType.Rotation or HkType.QsTransform or HkType.Matrix4 or HkType.Transform;

    internal static float[] Reals(ReadOnlySpan<byte> bytes)
    {
        var reals = new float[bytes.Length / 4];
        for (int i = 0; i < reals.Length; i++)
        {
            reals[i] = ByteCursor.F32(bytes, i * 4);
        }
        return reals;
    }

    /// <summary>The data an array or string slot owns.</summary>
    internal static HkxBlock? Elements(HkxBlock block, int at) => (block.Refs.GetValueOrDefault(at) as HkxData)?.Block;

    private static (HkxBlock Block, int At, HkSlot Slot, HkxBlock? Elements, int Count) Array(HkxObject o, string path)
    {
        (HkxBlock block, int at, HkSlot slot) = Locate(o, path);
        return slot.Member.Type is HkType.Array or HkType.SimpleArray
            ? (block, at, slot, Elements(block, at), ByteCursor.I32(block.Bytes, at + 4))
            : throw new ArgumentException($"'{path}' is not an array.");
    }

    // The member, checked to hold at least `size` bytes.
    private static (HkxBlock Block, int At, HkSlot Slot) Locate(HkxObject o, string path, int size = 0)
    {
        (HkxBlock Block, int At, HkSlot Slot) located = Locate(o, o.Class, 0, path)
            ?? throw new KeyNotFoundException($"{o.Class.Name} has no member '{path}'.");
        return size <= located.Slot.Size ? located
            : throw new ArgumentException($"'{path}' holds {located.Slot.Size} bytes, not {size}.");
    }

    // The block holding the member and its offset there: a slot of the struct at `at`, or one inside an array's elements.
    private static (HkxBlock Block, int At, HkSlot Slot)? Locate(HkxBlock block, HkClass c, int at, string path)
    {
        HavokClasses classes = HavokClasses.Default;
        IReadOnlyList<HkSlot> slots = classes.Slots(c);
        if (slots.FirstOrDefault(s => s.Path == path) is { } exact)
        {
            return (block, at + exact.Offset, exact);
        }

        for (int open = path.IndexOf('['); open > 0; open = path.IndexOf('[', open + 1))
        {
            int close = path.IndexOf(']', open);
            if (close < 0 || slots.FirstOrDefault(s => s.Path == path[..open]) is not { Member.Type: HkType.Array or HkType.SimpleArray } array)
            {
                continue;
            }

            int index = int.Parse(path[(open + 1)..close]);
            string rest = path[(close + 1)..].TrimStart('.');
            HkMember element = array.Member.Element;
            int size = classes.SizeOf(element.Type, HkType.Void, element.Class);
            if (index < 0 || index >= ByteCursor.I32(block.Bytes, at + array.Offset + 4) || Elements(block, at + array.Offset) is not { } elements)
            {
                throw new ArgumentOutOfRangeException(nameof(path), $"'{path[..close]}]' is past the end of the array.");
            }
            if (element.Type == HkType.Struct)
            {
                return Locate(elements, classes[element.Class!], index * size, rest);
            }
            return rest.Length == 0 ? (elements, index * size, new HkSlot(path, index * size, size, element)) : null;
        }
        return null;
    }
}
