using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using JackAll.Core.Format;

namespace JackAll.Tools.Hkx;

/// <summary>One saved value of an object: its path, the value as text, and the object a pointer leads to.</summary>
public sealed record HkxDumpLine(int Depth, string Path, string Value, HkxObject? Target);

/// <summary>
/// An <c>.hkx</c> as readable text: every object by number and each saved value under the path
/// <see cref="HkxEdit"/> takes, with pointers as <c>-&gt; #n</c>. Arrays of more than
/// <see cref="ShownElements"/> plain values print only their count.
/// </summary>
public static class HkxDump
{
    private const int ShownElements = 16;

    private static readonly HavokClasses Classes = HavokClasses.Default;

    public static string Write(HkxFile file)
    {
        List<HkxObject> objects = file.Objects();
        Dictionary<HkxObject, int> number = objects.Index().ToDictionary(p => p.Item, p => p.Index);
        var text = new StringBuilder();
        foreach ((int i, HkxObject o) in objects.Index())
        {
            text.Append('#').Append(i).Append(' ').AppendLine(o.Class.Name);
            foreach (HkxDumpLine line in Members(o, number))
            {
                text.Append(' ', 2 + 2 * line.Depth).Append(line.Path).Append(": ").AppendLine(line.Value);
            }
        }
        return text.ToString();
    }

    /// <summary>An object's saved values, an array's elements one level deeper than the array.</summary>
    public static IEnumerable<HkxDumpLine> Members(HkxObject o, IReadOnlyDictionary<HkxObject, int> numbers)
        => Slots(o, 0, o.Class, 0, "", numbers);

    private static IEnumerable<HkxDumpLine> Slots(HkxBlock block, int at, HkClass c, int depth, string prefix, IReadOnlyDictionary<HkxObject, int> numbers)
        => Classes.Slots(c)
            .Where(s => !s.Member.Ignored && s.Member.Type != HkType.FunctionPointer)
            .SelectMany(s => Value(block, at + s.Offset, s.Member, prefix + s.Path, depth, numbers));

    private static IEnumerable<HkxDumpLine> Value(HkxBlock block, int at, HkMember m, string name, int depth, IReadOnlyDictionary<HkxObject, int> numbers)
    {
        if (m.Type is not (HkType.Array or HkType.SimpleArray))
        {
            HkxObject? target = m.Type is HkType.Pointer or HkType.Variant ? (block.Refs.GetValueOrDefault(at) as HkxPointer)?.Target : null;
            yield return new HkxDumpLine(depth, name, Scalar(block, at, m.Type, m.Subtype, m.Enum, numbers), target);
            yield break;
        }

        int count = ByteCursor.I32(block.Bytes, at + 4);
        var elements = block.Refs.GetValueOrDefault(at) as HkxData;
        bool listed = elements is not null && (m.Subtype is HkType.Struct or HkType.Pointer || count <= ShownElements);
        yield return new HkxDumpLine(depth, name, elements is null || listed ? $"[{count}]" : $"[{count}] ...", null);
        if (!listed)
        {
            yield break;
        }

        int size = Classes.SizeOf(m.Subtype, HkType.Void, m.Class);
        for (int i = 0; i < count; i++)
        {
            IEnumerable<HkxDumpLine> lines = m.Subtype == HkType.Struct
                ? Slots(elements!.Block, i * size, Classes[m.Class!], depth + 1, $"[{i}].", numbers)
                : Value(elements!.Block, i * size, m with { Type = m.Subtype, Subtype = HkType.Void, Enum = null }, $"[{i}]", depth + 1, numbers);
            foreach (HkxDumpLine line in lines)
            {
                yield return line;
            }
        }
    }

    private static string Scalar(HkxBlock block, int at, HkType type, HkType subtype, HkEnum? hkEnum, IReadOnlyDictionary<HkxObject, int> numbers)
    {
        ReadOnlySpan<byte> b = block.Bytes.AsSpan(at);
        return type switch
        {
            HkType.Bool => (b[0] != 0).ToString(),
            HkType.Char or HkType.Int8 or HkType.UInt8 or HkType.Int16 or HkType.UInt16 or HkType.Int32
                => HkxEdit.Integer(b, type).ToString(CultureInfo.InvariantCulture),
            HkType.UInt32 or HkType.ULong => $"0x{ByteCursor.U32(b, 0):X8}",
            HkType.Int64 => BinaryPrimitives.ReadInt64LittleEndian(b).ToString(CultureInfo.InvariantCulture),
            HkType.UInt64 => $"0x{BinaryPrimitives.ReadUInt64LittleEndian(b):X16}",
            HkType.Real or HkType.Vector4 or HkType.Quaternion or HkType.Matrix3 or HkType.Rotation or HkType.QsTransform
                or HkType.Matrix4 or HkType.Transform => Reals(b, Classes.SizeOf(type, subtype, null) / 4),
            HkType.Enum or HkType.Flags => Enum(HkxEdit.Integer(b, subtype), hkEnum),
            HkType.Pointer or HkType.Variant => block.Refs.GetValueOrDefault(at) is HkxPointer p ? $"-> #{numbers[p.Target]} {p.Target.Class.Name}" : "null",
            HkType.CString => block.Refs.GetValueOrDefault(at) is HkxData s ? $"\"{s.Text}\"" : "null",
            _ => $"({type})",
        };
    }

    private static string Enum(int value, HkEnum? hkEnum)
        => hkEnum?.Items.FirstOrDefault(i => i.Value == value).Name is { } name ? $"{name} ({value})" : value.ToString(CultureInfo.InvariantCulture);

    private static string Reals(ReadOnlySpan<byte> b, int count)
    {
        var reals = new string[count];
        for (int i = 0; i < count; i++)
        {
            reals[i] = ByteCursor.F32(b, i * 4).ToString("G7", CultureInfo.InvariantCulture);
        }
        return string.Join(' ', reals);
    }
}
