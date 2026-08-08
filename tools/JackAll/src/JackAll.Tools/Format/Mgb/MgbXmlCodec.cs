using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace JackAll.Tools.Format.Mgb;

/// <summary>
/// Shared value formatting for the two XML codecs.
/// </summary>
/// <remarks>
/// Every rule here exists because byte-exact round-tripping demands it, not for readability. A
/// value is rendered in its friendly form only when that form provably reverses; otherwise it falls
/// back to something that always does. That is why floats can come out as hex, strings as base64,
/// and enum values as bare numbers - each is the escape hatch for content the pretty form cannot
/// carry.
/// </remarks>
internal static class MgbXmlValue
{
    /// <summary>Marks a payload that could not be carried as XML text.</summary>
    public const string Base64Prefix = "base64:";

    public static string Float(uint bits)
    {
        float value = BitConverter.UInt32BitsToSingle(bits);
        string text = value.ToString("R", CultureInfo.InvariantCulture);

        // Shortest-round-trippable covers ordinary values, but not a NaN payload or anything else
        // whose decimal spelling is lossy - those keep their exact bits as hex.
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            && BitConverter.SingleToUInt32Bits(parsed) == bits)
        {
            return text;
        }
        return "0x" + bits.ToString("X8", CultureInfo.InvariantCulture);
    }

    public static uint ParseFloat(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        return BitConverter.SingleToUInt32Bits(
            float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    /// <summary>ANSI bytes as text where every byte survives an XML attribute, else base64.</summary>
    public static string Ansi(byte[] bytes)
    {
        string text = MgbText.Ansi(bytes);
        return IsXmlSafe(text) && !text.StartsWith(Base64Prefix, StringComparison.Ordinal)
            ? text
            : Base64Prefix + Convert.ToBase64String(bytes);
    }

    public static byte[] ParseAnsi(string text) =>
        text.StartsWith(Base64Prefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(text[Base64Prefix.Length..])
            : MgbText.ToAnsi(text);

    /// <summary>UTF-16 bytes as text, but only when re-encoding reproduces them exactly - which
    /// rules out lone surrogates and anything else the decoder would normalise.</summary>
    public static string Utf16(byte[] bytes)
    {
        string text = MgbText.Utf16(bytes);
        if (IsXmlSafe(text)
            && !text.StartsWith(Base64Prefix, StringComparison.Ordinal)
            && MgbText.ToUtf16(text).AsSpan().SequenceEqual(bytes))
        {
            return text;
        }
        return Base64Prefix + Convert.ToBase64String(bytes);
    }

    public static byte[] ParseUtf16(string text) =>
        text.StartsWith(Base64Prefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(text[Base64Prefix.Length..])
            : MgbText.ToUtf16(text);

    /// <summary>
    /// Whether every character survives a round trip through an XML attribute value.
    /// </summary>
    /// <remarks>
    /// Anything below <c>0x20</c> is excluded rather than escaped. Tab, newline and carriage return
    /// are legal XML characters, but attribute-value normalisation rewrites them to spaces on the
    /// way back in, so a string carrying one would silently change. Base64 is the honest answer.
    ///
    /// Surrogates are not screened here. A well-formed pair is ordinary text and should stay
    /// readable; a lone one is caught by the caller's re-encode check, since decoding it yields
    /// U+FFFD and re-encoding no longer matches the original bytes.
    /// </remarks>
    private static bool IsXmlSafe(string text)
    {
        foreach (char ch in text)
        {
            if (ch < 0x20)
            {
                return false;
            }
        }
        return true;
    }

    public static string Hash(uint value) => "#" + value.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>A name only if it re-hashes to the value it claims to stand for; otherwise the raw
    /// hash. This is what makes the substitution safe: a wrong candidate cannot get written.</summary>
    public static string Name(uint hash, MgbNameLookup? names)
    {
        string? candidate = names?.Resolve(hash);
        if (candidate is null
            || candidate.StartsWith('#')
            || MgbTypeTable.Hash(candidate) != hash)
        {
            return Hash(hash);
        }
        return candidate;
    }

    public static uint ParseName(string text) =>
        text.StartsWith('#')
            ? uint.Parse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : MgbTypeTable.Hash(text);

    /// <summary>The value's name when the table has one, otherwise the plain number - so a value
    /// outside the table (or one carrying bits the engine masks off) is never truncated.</summary>
    public static string Enum(uint value, MgbEnum group)
    {
        string? name = group.NameFor(value);
        if (name is null || !group.TryValueFor(name, out uint back) || back != value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        return name;
    }

    public static uint ParseEnum(string text, MgbEnum group) =>
        group.TryValueFor(text, out uint value)
            ? value
            : uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

    public static string Color(uint argb) => argb.ToString("X8", CultureInfo.InvariantCulture);

    public static uint ParseColor(string text) =>
        uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public static string Integer(uint value) => value.ToString(CultureInfo.InvariantCulture);

    public static uint ParseInteger(string text) =>
        uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

    public static string[] Tokens(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}

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
        if (value.Length != byteCount)
        {
            throw new MgbFormatException(
                $"<{Path()}> attribute '{name}' decodes to {value.Length} bytes but {byteCount} " +
                "were expected");
        }
    }

    private static byte[] ParseBlob(string text) =>
        text.StartsWith(MgbXmlValue.Base64Prefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(text[MgbXmlValue.Base64Prefix.Length..])
            : throw new FormatException();

    public void NameId(string name, ref uint hash) => hash = Guard(name, MgbXmlValue.ParseName);

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
        value = text is null ? null : MgbXmlValue.ParseName(text);
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
        if (value.Length != byteCount)
        {
            throw new MgbFormatException(
                $"<{Path()}> attribute '{name}' decodes to {value.Length} bytes but {byteCount} " +
                "were expected");
        }
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
        values[i] = MgbXmlValue.ParseName(text));

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
