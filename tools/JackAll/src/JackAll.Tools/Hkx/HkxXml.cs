using System.Buffers.Binary;
using System.Globalization;
using System.Xml.Linq;

namespace JackAll.Tools.Hkx;

/// <summary>
/// An <c>.hkx</c> as editable XML: each object by the number <c>hkx dump</c> gives it, and each saved member under the
/// path <see cref="HkxEdit"/> takes. Pointers are <c>ref</c> numbers, arrays list their elements, and members Havok
/// does not save are left out. Every shipped file comes back from its XML byte for byte.
/// </summary>
public static class HkxXml
{
    // A saved hkArray's capacity word carries these with its count; an unsaved one is empty with only the first.
    private const uint SavedArrayFlags = 0xC0000000, UnsavedArrayFlags = 0x80000000;

    private static readonly HavokClasses Classes = HavokClasses.Default;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string ToXml(HkxFile file)
    {
        Dictionary<HkxObject, int> numbers = HkxDump.Numbers(file);
        var root = new XElement("hkx",
            new XAttribute("userTag", file.UserTag),
            new XAttribute("prefix", Convert.ToHexString(file.Prefix)),
            new XAttribute("root", numbers[file.Root]),
            new XElement("classNames", string.Join(' ', file.ClassNames)));
        if (file.Types.Bytes.Length > 0)
        {
            root.Add(new XElement("types",
                new XAttribute("locals", file.Types.Locals), new XAttribute("globals", file.Types.Globals), new XAttribute("virtuals", file.Types.Virtuals),
                Convert.ToHexString(file.Types.Bytes)));
        }
        foreach ((HkxObject o, int n) in numbers.OrderBy(p => p.Value))
        {
            root.Add(new XElement("object", new XAttribute("id", n), new XAttribute("class", o.Class.Name), Members(o, 0, o.Class, numbers)));
        }
        return new XDocument(root).ToString();
    }

    public static HkxFile FromXml(string xml)
    {
        XElement root = XDocument.Parse(xml).Root ?? throw new FormatException("The XML has no root element.");
        Dictionary<int, HkxObject> objects = root.Elements("object").ToDictionary(
            e => Int(e, "id"),
            e => HkxEdit.Create(Text(e, "class")));
        foreach (XElement e in root.Elements("object"))
        {
            HkxObject o = objects[Int(e, "id")];
            Fill(o, 0, o.Class, e, objects);
        }
        XElement? types = root.Element("types");
        return new HkxFile
        {
            Prefix = Convert.FromHexString(Text(root, "prefix")),
            UserTag = uint.Parse(Text(root, "userTag"), Invariant),
            ClassNames = [.. (root.Element("classNames")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)],
            Types = types is null
                ? new HkxRawSection([], 0, 0, 0)
                : new HkxRawSection(Convert.FromHexString(types.Value), Int(types, "locals"), Int(types, "globals"), Int(types, "virtuals")),
            Root = Find(objects, Int(root, "root")),
        };
    }

    private static IEnumerable<XElement> Members(HkxBlock block, int at, HkClass c, IReadOnlyDictionary<HkxObject, int> numbers)
        => Classes.Slots(c).Where(s => !s.Member.Ignored && s.Member.Type is not (HkType.FunctionPointer or HkType.Zero or HkType.Void))
            .Select(s => Member(block, at + s.Offset, s, numbers));

    private static XElement Member(HkxBlock block, int at, HkSlot s, IReadOnlyDictionary<HkxObject, int> numbers)
    {
        HkMember m = s.Member;
        var name = new XAttribute("name", s.Path);
        if (m.Type is not (HkType.Array or HkType.SimpleArray))
        {
            var e = new XElement("value", name);
            switch (m.Type)
            {
                case HkType.Pointer or HkType.Variant:
                    if (block.Refs.GetValueOrDefault(at) is HkxPointer p)
                    {
                        e.Add(new XAttribute("ref", numbers[p.Target]));
                    }
                    if (block.Refs.GetValueOrDefault(at + 4) is HkxVariantClass { TypesOffset: { } types })
                    {
                        e.Add(new XAttribute("types", types));
                    }
                    break;
                case HkType.CString:
                    if (block.Refs.GetValueOrDefault(at) is HkxData d)
                    {
                        e.Value = d.Text;
                    }
                    else
                    {
                        e.Add(new XAttribute("null", true));
                    }
                    break;
                default:
                    e.Value = Scalar(block.Bytes.AsSpan(at, s.Size), m);
                    break;
            }
            return e;
        }

        var array = new XElement("array", name);
        int count = BinaryPrimitives.ReadInt32LittleEndian(block.Bytes.AsSpan(at + 4));
        if (block.Refs.GetValueOrDefault(at) is not HkxData { Block: var elements } || count == 0)
        {
            return array;
        }
        HkMember element = m.Element;
        int size = Classes.SizeOf(element.Type, HkType.Void, element.Class);
        switch (element.Type)
        {
            case HkType.Struct:
                for (int i = 0; i < count; i++)
                {
                    array.Add(new XElement("item", Members(elements, i * size, Classes[element.Class!], numbers)));
                }
                break;
            case HkType.Pointer:
                array.Add(new XAttribute("refs", string.Join(' ', Enumerable.Range(0, count)
                    .Select(i => elements.Refs.GetValueOrDefault(i * size) is HkxPointer p ? numbers[p.Target].ToString(Invariant) : "-"))));
                break;
            case HkType.UInt8 or HkType.Int8:
                array.Value = Convert.ToHexString(elements.Bytes);
                break;
            default:
                array.Value = string.Join(' ', Enumerable.Range(0, count).Select(i => Scalar(elements.Bytes.AsSpan(i * size, size), element)));
                break;
        }
        return array;
    }

    // A value as text: reals and vectors as floats that round-trip, enums by name, unsigned 32-bit words in hex.
    private static string Scalar(ReadOnlySpan<byte> b, HkMember m)
    {
        if (m.Type is HkType.Enum or HkType.Flags)
        {
            int value = HkxEdit.Integer(b, m.Subtype);
            return m.Type == HkType.Enum && m.Enum?.Items.FirstOrDefault(i => i.Value == value).Name is { } named ? named : value.ToString(Invariant);
        }
        return m.Type switch
        {
            HkType.Bool => (b[0] != 0) ? "true" : "false",
            HkType.UInt32 or HkType.ULong => $"0x{BinaryPrimitives.ReadUInt32LittleEndian(b):X8}",
            HkType.Int64 => BinaryPrimitives.ReadInt64LittleEndian(b).ToString(Invariant),
            HkType.UInt64 => $"0x{BinaryPrimitives.ReadUInt64LittleEndian(b):X16}",
            _ when HkxEdit.IsReal(m.Type) => string.Join(' ', HkxEdit.Reals(b).Select(r => r.ToString("R", Invariant))),
            _ => HkxEdit.Integer(b, m.Type).ToString(Invariant),
        };
    }

    // Writes each member element of `e` into the struct of class `c` at `at`, and marks the unsaved arrays as Havok does.
    private static void Fill(HkxBlock block, int at, HkClass c, XElement e, IReadOnlyDictionary<int, HkxObject> objects)
    {
        IReadOnlyList<HkSlot> slots = Classes.Slots(c);
        foreach (HkSlot s in slots.Where(s => s.Member.Ignored && s.Member.Type == HkType.Array))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.Bytes.AsSpan(at + s.Offset + 8), UnsavedArrayFlags);
        }
        foreach (XElement member in e.Elements())
        {
            string path = Text(member, "name");
            HkSlot s = slots.FirstOrDefault(s => s.Path == path) ?? throw new FormatException($"{c.Name} has no member '{path}'.");
            if (member.Name == "array")
            {
                Array(block, at + s.Offset, s, member, objects);
            }
            else
            {
                Value(block, at + s.Offset, s, member, objects);
            }
        }
    }

    private static void Value(HkxBlock block, int at, HkSlot s, XElement e, IReadOnlyDictionary<int, HkxObject> objects)
    {
        HkMember m = s.Member;
        switch (m.Type)
        {
            case HkType.Pointer or HkType.Variant:
                if (e.Attribute("ref") is { } reference)
                {
                    block.Refs[at] = new HkxPointer(Find(objects, int.Parse(reference.Value, Invariant)));
                    if (m.Type == HkType.Variant)
                    {
                        block.Refs[at + 4] = new HkxVariantClass(e.Attribute("types") is { } types ? int.Parse(types.Value, Invariant) : null);
                    }
                }
                break;
            case HkType.CString:
                if (e.Attribute("null") is null)
                {
                    block.Refs[at] = HkxData.FromText(e.Value);
                }
                break;
            default:
                Parse(e.Value, m, s.Path).CopyTo(block.Bytes.AsSpan(at));
                break;
        }
    }

    private static void Array(HkxBlock block, int at, HkSlot s, XElement e, IReadOnlyDictionary<int, HkxObject> objects)
    {
        HkMember element = s.Member.Element;
        int size = Classes.SizeOf(element.Type, HkType.Void, element.Class);
        HkxBlock elements;
        switch (element.Type)
        {
            case HkType.Struct:
            {
                List<XElement> items = [.. e.Elements("item")];
                elements = new HkxBlock(new byte[items.Count * size]);
                for (int i = 0; i < items.Count; i++)
                {
                    Fill(elements, i * size, Classes[element.Class!], items[i], objects);
                }
                break;
            }
            case HkType.Pointer:
            {
                string[] refs = (e.Attribute("refs")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                elements = new HkxBlock(new byte[refs.Length * size]);
                for (int i = 0; i < refs.Length; i++)
                {
                    if (refs[i] != "-")
                    {
                        elements.Refs[i * size] = new HkxPointer(Find(objects, int.Parse(refs[i], Invariant)));
                    }
                }
                break;
            }
            case HkType.UInt8 or HkType.Int8:
                elements = new HkxBlock(Convert.FromHexString(e.Value.Trim()));
                break;
            default:
            {
                string[] words = e.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                int per = HkxEdit.IsReal(element.Type) ? size / 4 : 1;
                elements = new HkxBlock(new byte[words.Length / per * size]);
                for (int i = 0; i < words.Length / per; i++)
                {
                    Parse(string.Join(' ', words[(i * per)..((i + 1) * per)]), element, s.Path).CopyTo(elements.Bytes.AsSpan(i * size));
                }
                break;
            }
        }

        int count = elements.Bytes.Length / size;
        if (count > 0)
        {
            block.Refs[at] = new HkxData(elements);
        }
        BinaryPrimitives.WriteInt32LittleEndian(block.Bytes.AsSpan(at + 4), count);
        if (s.Member.Type == HkType.Array)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.Bytes.AsSpan(at + 8), (uint)count | SavedArrayFlags);
        }
    }

    /// <summary>A value's bytes from its text: floats for reals and vectors, a name or number for an enum, true or false, or an integer in decimal or 0x hex.</summary>
    public static byte[] Parse(string text, HkMember m, string path)
    {
        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int size = Classes.SizeOf(m.Type, m.Subtype, m.Class);
        var bytes = new byte[size];
        if (HkxEdit.IsReal(m.Type))
        {
            if (words.Length != size / 4)
            {
                throw new FormatException($"'{path}' takes {size / 4} numbers, not {words.Length}.");
            }
            for (int i = 0; i < words.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), float.Parse(words[i], NumberStyles.Float, Invariant));
            }
            return bytes;
        }
        string word = words.Length == 1 ? words[0] : throw new FormatException($"'{path}' takes one value, not '{text}'.");
        long value = m.Type switch
        {
            HkType.Bool => word switch { "true" or "1" => 1, "false" or "0" => 0, _ => throw new FormatException($"'{path}' takes true or false, not '{word}'.") },
            HkType.Enum when m.Enum?.Items.FirstOrDefault(i => i.Name == word) is { Name: not null } named => named.Value,
            _ => Integer(word, path),
        };
        ulong raw = (ulong)value;
        for (int i = 0; i < size; i++)
        {
            bytes[i] = (byte)(raw >> (8 * i));
        }
        return bytes;
    }

    private static long Integer(string word, string path)
        => word.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && ulong.TryParse(word[2..], NumberStyles.HexNumber, Invariant, out ulong hex) ? (long)hex
            : long.TryParse(word, NumberStyles.Integer, Invariant, out long n) ? n
            : throw new FormatException($"'{path}' takes a number, not '{word}'.");

    private static HkxObject Find(IReadOnlyDictionary<int, HkxObject> objects, int id)
        => objects.TryGetValue(id, out HkxObject? o) ? o : throw new FormatException($"There is no object {id}.");

    private static int Int(XElement e, string attribute) => int.Parse(Text(e, attribute), Invariant);

    private static string Text(XElement e, string attribute)
        => e.Attribute(attribute)?.Value ?? throw new FormatException($"<{e.Name}> needs a {attribute} attribute.");
}
