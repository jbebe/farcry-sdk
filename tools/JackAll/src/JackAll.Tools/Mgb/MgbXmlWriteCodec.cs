using System.Xml.Linq;

namespace JackAll.Tools.Mgb;

/// <summary>
/// Renders a package as XML, driven by the same <c>Serialize</c> descriptions as the binary writer.
/// </summary>
public sealed class MgbXmlWriteCodec(XElement root, MgbNameLookup? names = null) : IMgbCodec
{
    private readonly Stack<XElement> _stack = new([root]);
    private readonly MgbNameLookup? _names = names;

    public bool IsReading => false;

    public int Position => 0;

    private XElement Current => _stack.Peek();

    private void Set(string name, string value)
    {
        // XElement.Add throws on a duplicate attribute, which is exactly the guard wanted: two
        // fields of one record sharing a name would otherwise silently overwrite each other.
        try
        {
            Current.Add(new XAttribute(name, value));
        }
        catch (InvalidOperationException)
        {
            throw new MgbFormatException(
                $"two fields of <{Current.Name}> both write the attribute '{name}'");
        }
    }

    private IDisposable Push(string name)
    {
        var child = new XElement(name);
        Current.Add(child);
        _stack.Push(child);
        return new Popper(_stack);
    }

    private sealed class Popper(Stack<XElement> stack) : IDisposable
    {
        public void Dispose() => stack.Pop();
    }

    public IDisposable Scope(string name) => Push(name);

    public IDisposable Item(string name) => Push(name);

    public IDisposable ListScope(string name, ref int count, MgbCountWidth width = MgbCountWidth.U32)
        => Push(name);

    public int Count(string name, int count, MgbCountWidth width = MgbCountWidth.U32) => count;

    public bool Gate(string name, bool present) => present;

    public void U8(string name, ref byte value) => Set(name, MgbXmlValue.Integer(value));

    public void U16(string name, ref ushort value) => Set(name, MgbXmlValue.Integer(value));

    public void U32(string name, ref uint value) => Set(name, MgbXmlValue.Integer(value));

    public void F32Bits(string name, ref uint bits) => Set(name, MgbXmlValue.Float(bits));

    public void Bool(string name, ref bool value) => Set(name, value ? "true" : "false");

    public void AnsiString(string name, ref byte[] value) => Set(name, MgbXmlValue.Ansi(value));

    public void Utf16String(string name, ref byte[] value) => Set(name, MgbXmlValue.Utf16(value));

    public void Blob(string name, ref byte[] value, int byteCount)
        => Set(name, MgbXmlValue.Base64Prefix + Convert.ToBase64String(value));

    public void NameId(string name, ref uint hash) => Set(name, MgbXmlValue.Name(hash, _names));

    public void EnumU32(string name, ref uint value, MgbEnum group)
        => Set(name, MgbXmlValue.Enum(value, group));

    public void ColorU32(string name, ref uint argb) => Set(name, MgbXmlValue.Color(argb));

    public void TypeSlot(string slotName, string className, ref byte slot, MgbTypeTable types)
    {
        // The slot stays authoritative - several slots can resolve to one class, so rebuilding it
        // from the name would not reproduce the file. The name is decoration.
        Set(slotName, MgbXmlValue.Integer(slot));
        string? resolved = types.NameForSlot(slot);
        if (resolved is not null)
        {
            Set(className, resolved);
        }
    }

    public void OptionalU32(string name, ref uint? value)
    {
        if (value.HasValue)
        {
            Set(name, MgbXmlValue.Integer(value.Value));
        }
    }

    public void OptionalNameId(string name, ref uint? value)
    {
        if (value.HasValue)
        {
            Set(name, MgbXmlValue.Name(value.Value, _names));
        }
    }

    public void OptionalBlob(string name, ref byte[]? value, int byteCount)
    {
        if (value is not null)
        {
            Set(name, MgbXmlValue.Base64Prefix + Convert.ToBase64String(value));
        }
    }

    public void U16Array(string name, ushort[] values)
        => Set(name, string.Join(' ', values.Select(v => MgbXmlValue.Integer(v))));

    public void U32Array(string name, uint[] values)
        => Set(name, string.Join(' ', values.Select(MgbXmlValue.Integer)));

    public void F32BitsArray(string name, uint[] values)
        => Set(name, string.Join(' ', values.Select(MgbXmlValue.Float)));

    public void U32Items(string name, List<uint> values)
        => Set(name, string.Join(' ', values.Select(MgbXmlValue.Integer)));

    public void NameIdItems(string name, List<uint> values)
        => Set(name, string.Join(' ', values.Select(v => MgbXmlValue.Name(v, _names))));
}
