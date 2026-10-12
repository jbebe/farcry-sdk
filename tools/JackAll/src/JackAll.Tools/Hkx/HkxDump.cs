using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using JackAll.Core.Format;

namespace JackAll.Tools.Hkx;

/// <summary>
/// One saved value of an object: its path as listed (an array element's relative to its array), the value as text,
/// the object a pointer leads to, and the member with the full path <see cref="HkxEdit"/> takes.
/// </summary>
public sealed record HkxDumpLine(int Depth, string Path, string Value, HkxObject? Target, HkMember Member, string FullPath);

/// <summary>
/// An <c>.hkx</c> as readable text: every object by number and each saved value under the path
/// <see cref="HkxEdit"/> takes, with pointers as <c>-&gt; #n</c>. An array's elements are listed under it,
/// and arrays of more than <see cref="ShownElements"/> plain values print only their count.
/// </summary>
public static class HkxDump
{
    private const int ShownElements = 16;

    private static readonly HavokClasses Classes = HavokClasses.Default;

    public static string Write(HkxFile file)
    {
        Dictionary<HkxObject, int> numbers = Numbers(file);
        var text = new StringBuilder();
        foreach ((HkxObject o, int i) in numbers.OrderBy(p => p.Value))
        {
            text.Append('#').Append(i).Append(' ').AppendLine(o.Class.Name);
            foreach (HkxDumpLine line in Members(o, numbers))
            {
                text.Append(' ', 2 + 2 * line.Depth).Append(line.Path).Append(": ").AppendLine(line.Value);
            }
        }
        return text.ToString();
    }

    /// <summary>Each object's number, in the order the writer places them.</summary>
    public static Dictionary<HkxObject, int> Numbers(HkxFile file) => file.Objects().Index().ToDictionary(p => p.Item, p => p.Index);

    /// <summary>An object's saved values, an array's elements one level deeper than the array.</summary>
    public static IEnumerable<HkxDumpLine> Members(HkxObject o, IReadOnlyDictionary<HkxObject, int> numbers)
        => Slots(o, 0, o.Class, 0, ("", ""), numbers);

    // `prefix` is what goes before each slot's path as listed, and in full.
    private static IEnumerable<HkxDumpLine> Slots(HkxBlock block, int at, HkClass c, int depth, (string Listed, string Full) prefix,
        IReadOnlyDictionary<HkxObject, int> numbers)
        => Classes.Slots(c)
            .Where(s => !s.Member.Ignored && s.Member.Type != HkType.FunctionPointer)
            .SelectMany(s => Value(block, at + s.Offset, s.Member, (prefix.Listed + s.Path, prefix.Full + s.Path), depth, numbers));

    private static IEnumerable<HkxDumpLine> Value(HkxBlock block, int at, HkMember m, (string Listed, string Full) name, int depth,
        IReadOnlyDictionary<HkxObject, int> numbers)
    {
        if (m.Type is not (HkType.Array or HkType.SimpleArray))
        {
            HkxObject? target = m.Type is HkType.Pointer or HkType.Variant ? (block.Refs.GetValueOrDefault(at) as HkxPointer)?.Target : null;
            string value = target is null ? Scalar(block, at, m.Type, m.Subtype, m.Enum) : $"-> #{numbers[target]} {target.Class.Name}";
            yield return new HkxDumpLine(depth, name.Listed, value, target, m, name.Full);
            yield break;
        }

        int count = ByteCursor.I32(block.Bytes, at + 4);
        HkxBlock? elements = HkxEdit.Elements(block, at);
        bool listed = m.Subtype is HkType.Struct or HkType.Pointer || count <= ShownElements;
        yield return new HkxDumpLine(depth, name.Listed, listed || elements is null ? $"[{count}]" : $"[{count}] ...", null, m, name.Full);
        if (!listed || elements is null)
        {
            yield break;
        }

        HkMember element = m.Element;
        int size = Classes.SizeOf(element.Type, HkType.Void, element.Class);
        for (int i = 0; i < count; i++)
        {
            IEnumerable<HkxDumpLine> lines = element.Type == HkType.Struct
                ? Slots(elements, i * size, Classes[element.Class!], depth + 1, ($"[{i}].", $"{name.Full}[{i}]."), numbers)
                : Value(elements, i * size, element, ($"[{i}]", $"{name.Full}[{i}]"), depth + 1, numbers);
            foreach (HkxDumpLine line in lines)
            {
                yield return line;
            }
        }
    }

    private static string Scalar(HkxBlock block, int at, HkType type, HkType subtype, HkEnum? hkEnum)
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
            HkType.Enum or HkType.Flags => Enum(HkxEdit.Integer(b, subtype), hkEnum),
            HkType.Pointer or HkType.Variant => "null",
            HkType.CString => block.Refs.GetValueOrDefault(at) is HkxData s ? $"\"{s.Text}\"" : "null",
            _ when HkxEdit.IsReal(type)
                => string.Join(' ', HkxEdit.Reals(b[..Classes.SizeOf(type, subtype, null)]).Select(r => r.ToString("G7", CultureInfo.InvariantCulture))),
            _ => $"({type})",
        };
    }

    private static string Enum(int value, HkEnum? hkEnum)
        => hkEnum?.Items.FirstOrDefault(i => i.Value == value).Name is { } name ? $"{name} ({value})" : value.ToString(CultureInfo.InvariantCulture);
}
