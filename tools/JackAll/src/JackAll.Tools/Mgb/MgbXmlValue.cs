using System.Globalization;

namespace JackAll.Tools.Mgb;

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
