using System.Xml.Linq;

namespace JackAll.Tools.Mgb;

/// <summary>
/// Rebuilds a package from XML, driven by the same <c>Serialize</c> descriptions as the binary
/// reader.
/// </summary>
/// <remarks>
/// Deliberately strict. Magma's own XML loader treats every element as optional and degrades
/// silently when one is missing, which turns a typo into a subtly wrong screen rather than an
/// error. This one names the element and the field it could not find, and refuses anything it did
/// not consume.
/// </remarks>
public sealed class MgbXmlReadCodec : IMgbCodec
{
    private sealed class Frame(XElement element)
    {
        public readonly XElement Element = element;
        public readonly List<XElement> Children = [.. element.Elements()];
        public int NextChild;
    }

    private readonly Stack<Frame> _stack;
    private readonly XElement _root;
    private readonly HashSet<XElement> _visited = [];
    private readonly Dictionary<XElement, HashSet<string>> _used = [];

    /// <summary>When set, every name the document spells out is offered to it as the walk goes by.
    /// The binary keeps only hashes, so this is the one chance to keep them.</summary>
    public MgbNameLookup? CollectNamesInto { get; init; }

    /// <param name="root">The document element.</param>
    /// <param name="rootAttributes">Attributes the caller handles itself, exempted from the
    /// leftover check <see cref="Finish"/> runs.</param>
    public MgbXmlReadCodec(XElement root, params string[] rootAttributes)
    {
        _root = root;
        _visited.Add(root);
        _used[root] = [.. rootAttributes];
        _stack = new Stack<Frame>([new Frame(root)]);
    }

    /// <summary>
    /// Reports anything in the document the walk never looked at.
    /// </summary>
    /// <remarks>
    /// Runs as a separate pass, on the success path only, rather than as each scope closes. Doing
    /// it in <c>Dispose</c> meant that when a field was genuinely missing, the leftover complaint
    /// fired while the real exception was still unwinding and replaced it - so the author was told
    /// a list had the wrong length instead of which attribute they had misspelled.
    /// </remarks>
    public void Finish() => Validate(_root);

    private void Validate(XElement element)
    {
        HashSet<string> used = _used.GetValueOrDefault(element) ?? [];
        foreach (XAttribute attribute in element.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration && !used.Contains(attribute.Name.LocalName))
            {
                throw new MgbFormatException(
                    $"<{element.Name}> has an attribute this format does not define: " +
                    $"'{attribute.Name.LocalName}'");
            }
        }
        foreach (XElement child in element.Elements())
        {
            if (!_visited.Contains(child))
            {
                throw new MgbFormatException(
                    $"<{element.Name}> contains <{child.Name}>, which this format does not define there");
            }
            Validate(child);
        }
    }

    public bool IsReading => true;

    public int Position => 0;

    private Frame Current => _stack.Peek();

    private string Path() => string.Join('/', _stack.Reverse().Select(f => f.Element.Name.LocalName));

    private void MarkUsed(string name)
    {
        if (!_used.TryGetValue(Current.Element, out HashSet<string>? used))
        {
            used = [];
            _used[Current.Element] = used;
        }
        used.Add(name);
    }

    private string Read(string name)
    {
        XAttribute? attribute = Current.Element.Attribute(name);
        if (attribute is null)
        {
            throw new MgbFormatException($"<{Path()}> is missing the required attribute '{name}'");
        }
        MarkUsed(name);
        return attribute.Value;
    }

    private string? ReadOptional(string name)
    {
        XAttribute? attribute = Current.Element.Attribute(name);
        if (attribute is null)
        {
            return null;
        }
        MarkUsed(name);
        return attribute.Value;
    }

    private T Guard<T>(string name, Func<string, T> parse)
    {
        string text = Read(name);
        try
        {
            return parse(text);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new MgbFormatException(
                $"<{Path()}> attribute '{name}' has the unusable value \"{text}\"");
        }
    }

    private IDisposable Push(XElement element)
    {
        _visited.Add(element);
        _stack.Push(new Frame(element));
        return new Popper(this);
    }

    private sealed class Popper(MgbXmlReadCodec codec) : IDisposable
    {
        public void Dispose() => codec._stack.Pop();
    }

    public IDisposable Scope(string name)
    {
        XElement? child = Current.Element.Element(name);
        if (child is null)
        {
            throw new MgbFormatException($"<{Path()}> is missing the required element <{name}>");
        }
        return Push(child);
    }

    public IDisposable Item(string name)
    {
        Frame frame = Current;
        if (frame.NextChild >= frame.Children.Count)
        {
            throw new MgbFormatException($"<{Path()}> ran out of <{name}> items");
        }
        XElement child = frame.Children[frame.NextChild++];
        if (child.Name.LocalName != name)
        {
            throw new MgbFormatException(
                $"<{Path()}> item {frame.NextChild} is <{child.Name.LocalName}>, expected <{name}>");
        }
        return Push(child);
    }

    public IDisposable ListScope(string name, ref int count, MgbCountWidth width = MgbCountWidth.U32)
    {
        XElement? child = Current.Element.Element(name);
        if (child is null)
        {
            throw new MgbFormatException($"<{Path()}> is missing the required list <{name}>");
        }
        IDisposable scope = Push(child);
        count = Current.Children.Count;
        return scope;
    }

    public int Count(string name, int count, MgbCountWidth width = MgbCountWidth.U32)
    {
        string? text = ReadOptional(name);
        return text is null ? 0 : MgbXmlValue.Tokens(text).Length;
    }

    public bool Gate(string name, bool present) => Current.Element.Element(name) is not null;

    public void U8(string name, ref byte value)
        => value = checked((byte)Guard(name, MgbXmlValue.ParseInteger));

    public void U16(string name, ref ushort value)
        => value = checked((ushort)Guard(name, MgbXmlValue.ParseInteger));

    public void U32(string name, ref uint value) => value = Guard(name, MgbXmlValue.ParseInteger);

    public void F32Bits(string name, ref uint bits) => bits = Guard(name, MgbXmlValue.ParseFloat);

    public void Bool(string name, ref bool value)
        => value = Guard(name, t => t switch
        {
            "true" => true,
            "false" => false,
            _ => throw new FormatException(),
        });

    public void AnsiString(string name, ref byte[] value)
        => value = Guard(name, MgbXmlValue.ParseAnsi);

    public void Utf16String(string name, ref byte[] value)
        => value = Guard(name, MgbXmlValue.ParseUtf16);

    public void Blob(string name, ref byte[] value, int byteCount)
    {
        value = Guard(name, ParseBlob);
        CheckBlobLength(name, value, byteCount);
    }

    private static byte[] ParseBlob(string text) =>
        text.StartsWith(MgbXmlValue.Base64Prefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(text[MgbXmlValue.Base64Prefix.Length..])
            : throw new FormatException();

    private void CheckBlobLength(string name, byte[] value, int byteCount)
    {
        if (value.Length != byteCount)
        {
            throw new MgbFormatException(
                $"<{Path()}> attribute '{name}' decodes to {value.Length} bytes but {byteCount} " +
                "were expected");
        }
    }

    public void NameId(string name, ref uint hash) => hash = Guard(name, ParseName);

    /// <summary>Parses a name-or-hash, remembering the name when it was written as one.</summary>
    private uint ParseName(string text)
    {
        if (!text.StartsWith('#'))
        {
            CollectNamesInto?.Offer(text);
        }
        return MgbXmlValue.ParseName(text);
    }

    public void EnumU32(string name, ref uint value, MgbEnum group)
        => value = Guard(name, t => MgbXmlValue.ParseEnum(t, group));

    public void ColorU32(string name, ref uint argb) => argb = Guard(name, MgbXmlValue.ParseColor);

    public void TypeSlot(string slotName, string className, ref byte slot, MgbTypeTable types)
    {
        // The decorative class name is consumed so the leftover-attribute check stays quiet, but it
        // is never trusted: the raw slot is what reproduces the file.
        ReadOptional(className);
        slot = checked((byte)Guard(slotName, MgbXmlValue.ParseInteger));
    }

    public void OptionalU32(string name, ref uint? value)
    {
        string? text = ReadOptional(name);
        value = text is null ? null : MgbXmlValue.ParseInteger(text);
    }

    public void OptionalNameId(string name, ref uint? value)
    {
        string? text = ReadOptional(name);
        value = text is null ? null : ParseName(text);
    }

    public void OptionalBlob(string name, ref byte[]? value, int byteCount)
    {
        string? text = ReadOptional(name);
        if (text is null)
        {
            value = null;
            return;
        }
        value = ParseBlob(text);
        CheckBlobLength(name, value, byteCount);
    }

    public void U16Array(string name, ushort[] values) => FillArray(name, values.Length, (i, text) =>
        values[i] = checked((ushort)MgbXmlValue.ParseInteger(text)));

    public void U32Array(string name, uint[] values) => FillArray(name, values.Length, (i, text) =>
        values[i] = MgbXmlValue.ParseInteger(text));

    public void F32BitsArray(string name, uint[] values) => FillArray(name, values.Length, (i, text) =>
        values[i] = MgbXmlValue.ParseFloat(text));

    public void U32Items(string name, List<uint> values) => FillArray(name, values.Count, (i, text) =>
        values[i] = MgbXmlValue.ParseInteger(text));

    public void NameIdItems(string name, List<uint> values) => FillArray(name, values.Count, (i, text) =>
        values[i] = ParseName(text));

    private void FillArray(string name, int expected, Action<int, string> assign)
    {
        if (expected == 0)
        {
            // A zero-length run writes an empty attribute, and an absent one means the same thing.
            ReadOptional(name);
            return;
        }
        string[] tokens = MgbXmlValue.Tokens(Read(name));
        if (tokens.Length != expected)
        {
            throw new MgbFormatException(
                $"<{Path()}> attribute '{name}' has {tokens.Length} values but {expected} were expected");
        }
        for (int i = 0; i < expected; i++)
        {
            try
            {
                assign(i, tokens[i]);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new MgbFormatException(
                    $"<{Path()}> attribute '{name}' value {i + 1} is unusable: \"{tokens[i]}\"");
            }
        }
    }
}
