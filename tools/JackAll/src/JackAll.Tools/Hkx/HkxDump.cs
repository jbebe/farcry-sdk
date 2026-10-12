using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using JackAll.Core.Format;

namespace JackAll.Tools.Hkx;

/// <summary>
/// An <c>.hkx</c> as readable text: every object by number and each saved value under the path
/// <see cref="HkxEdit"/> takes, with pointers as <c>-&gt; #n</c>. Arrays of more than
/// <see cref="ShownElements"/> plain values print only their count.
/// </summary>
public static class HkxDump
{
    private const int ShownElements = 16;

    public static string Write(HkxFile file)
    {
        HavokClasses classes = HavokClasses.Default;
        List<HkxObject> objects = file.Objects();
        Dictionary<HkxObject, int> number = objects.Index().ToDictionary(p => p.Item, p => p.Index);
        var text = new StringBuilder();

        foreach ((int i, HkxObject o) in objects.Index())
        {
            text.Append('#').Append(i).Append(' ').AppendLine(o.Class.Name);
            Slots(o, 0, o.Class, "  ", "");
        }
        return text.ToString();

        void Slots(HkxBlock block, int at, HkClass c, string indent, string prefix)
        {
            foreach (HkSlot s in classes.Slots(c).Where(s => !s.Member.Ignored && s.Member.Type != HkType.FunctionPointer))
            {
                Value(block, at + s.Offset, s.Member.Type, s.Member.Subtype, s.Member.Class, s.Member.Enum, prefix + s.Path, indent);
            }
        }

        void Value(HkxBlock block, int at, HkType type, HkType subtype, string? className, HkEnum? hkEnum, string name, string indent)
        {
            if (type is not (HkType.Array or HkType.SimpleArray))
            {
                text.Append(indent).Append(name).Append(": ").AppendLine(Scalar(block, at, type, subtype, hkEnum));
                return;
            }

            int count = ByteCursor.I32(block.Bytes, at + 4);
            text.Append(indent).Append(name).Append(": [").Append(count).Append(']');
            if (block.Refs.GetValueOrDefault(at) is not HkxData elements)
            {
                text.AppendLine();
                return;
            }
            if (subtype is not (HkType.Struct or HkType.Pointer) && count > ShownElements)
            {
                text.AppendLine(" ...");
                return;
            }
            text.AppendLine();
            int size = classes.SizeOf(subtype, HkType.Void, className);
            for (int i = 0; i < count; i++)
            {
                if (subtype == HkType.Struct)
                {
                    Slots(elements.Block, i * size, classes[className!], indent + "  ", $"[{i}].");
                }
                else
                {
                    Value(elements.Block, i * size, subtype, HkType.Void, className, null, $"[{i}]", indent + "  ");
                }
            }
        }

        string Scalar(HkxBlock block, int at, HkType type, HkType subtype, HkEnum? hkEnum)
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
                    or HkType.Matrix4 or HkType.Transform => Reals(b, classes.SizeOf(type, subtype, null) / 4),
                HkType.Enum or HkType.Flags => Enum(HkxEdit.Integer(b, subtype), hkEnum),
                HkType.Pointer or HkType.Variant => block.Refs.GetValueOrDefault(at) is HkxPointer p ? $"-> #{number[p.Target]} {p.Target.Class.Name}" : "null",
                HkType.CString => block.Refs.GetValueOrDefault(at) is HkxData s ? $"\"{s.Text}\"" : "null",
                _ => $"({type})",
            };
        }
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
