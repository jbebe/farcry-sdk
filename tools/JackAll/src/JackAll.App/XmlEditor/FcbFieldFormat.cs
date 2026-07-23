using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.XmlEditor;

/// <summary>
/// Formats a <see cref="FcbValueCodec"/>-decoded value for display in an editable text field, and
/// parses a user's edit back to the same native type — the text-level counterpart to
/// <see cref="FcbValueCodec"/>'s byte-level codec. Every numeric/hex/string leaf in the property grid
/// (a top-level scalar, one vector component, one matrix cell, one array item) goes through this, so
/// validation behaves identically no matter how deeply nested the field is.
/// </summary>
public static class FcbFieldFormat
{
    public static string Format(FcbMemberType type, object value) => type switch
    {
        FcbMemberType.String => (string)value,
        FcbMemberType.Hash => ((uint)value).ToString("X8", CultureInfo.InvariantCulture),
        FcbMemberType.Enum => ((uint)value).ToString(CultureInfo.InvariantCulture),
        FcbMemberType.Float => ((float)value).ToString(CultureInfo.InvariantCulture),
        FcbMemberType.Int8 => ((sbyte)value).ToString(CultureInfo.InvariantCulture),
        FcbMemberType.UInt8 => ((byte)value).ToString(CultureInfo.InvariantCulture),
        FcbMemberType.Int16 => ((short)value).ToString(CultureInfo.InvariantCulture),
        FcbMemberType.UInt16 => ((ushort)value).ToString(CultureInfo.InvariantCulture),
        FcbMemberType.Int32 => ((int)value).ToString(CultureInfo.InvariantCulture),
        FcbMemberType.UInt32 => ((uint)value).ToString(CultureInfo.InvariantCulture),
        FcbMemberType.Int64 => ((long)value).ToString(CultureInfo.InvariantCulture),
        FcbMemberType.UInt64 => ((ulong)value).ToString(CultureInfo.InvariantCulture),
        FcbMemberType.BinHex => Convert.ToHexString((byte[])value),
        // Same nested-document convention FcbXml.ToXml uses for its own <value type="Rml"> text: shown
        // indented in the multi-line RmlTextEditor (see XmlEditorTabView's ScalarField template) when
        // the bytes decode as a well-formed .rml document, opaque hex otherwise (a value that isn't
        // actually an embedded document, or was hand-edited into something else).
        FcbMemberType.Rml => FcbXml.TryDecodeRmlValue((byte[])value) is { } rml
            ? rml.ToString()
            : Convert.ToHexString((byte[])value),
        FcbMemberType.Vector2 or FcbMemberType.Vector3 or FcbMemberType.Vector4
            => string.Join(", ", ((float[])value).Select(v => v.ToString(CultureInfo.InvariantCulture))),
        _ => throw new NotSupportedException($"'{type}' is not a scalar text field."),
    };

    /// <summary>The value a brand-new array item/vector component starts out as, before the user has
    /// typed anything — zero, empty, or all-zero bytes depending on the type.</summary>
    public static object DefaultValue(FcbMemberType type) => type switch
    {
        FcbMemberType.String => string.Empty,
        FcbMemberType.Hash or FcbMemberType.Enum
            or FcbMemberType.UInt8 or FcbMemberType.UInt16 or FcbMemberType.UInt32 or FcbMemberType.UInt64 => 0u,
        FcbMemberType.Float => 0f,
        FcbMemberType.Int8 => (sbyte)0,
        FcbMemberType.Int16 => (short)0,
        FcbMemberType.Int32 => 0,
        FcbMemberType.Int64 => 0L,
        FcbMemberType.BinHex or FcbMemberType.Rml => Array.Empty<byte>(),
        FcbMemberType.Vector2 => new float[2],
        FcbMemberType.Vector3 => new float[3],
        FcbMemberType.Vector4 => new float[4],
        _ => throw new NotSupportedException($"'{type}' is not a scalar text field."),
    };

    private static int VectorComponentCount(FcbMemberType type) => type switch
    {
        FcbMemberType.Vector2 => 2,
        FcbMemberType.Vector3 => 3,
        FcbMemberType.Vector4 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a vector type."),
    };

    /// <summary>
    /// Parses <paramref name="text"/> back to <paramref name="type"/>'s native value, or returns false
    /// with a short, field-level <paramref name="error"/> message on anything that wouldn't survive
    /// <see cref="FcbValueCodec.Encode"/> — a non-numeric Float, a Hash with the wrong number of hex
    /// digits, an odd-length Rml hex string, an out-of-range integer for its declared width, and so on.
    /// </summary>
    public static bool TryParse(FcbMemberType type, string text, out object value, out string? error)
    {
        text = text.Trim();
        switch (type)
        {
            case FcbMemberType.String:
                value = text;
                error = null;
                return true;

            case FcbMemberType.Hash:
                if (text.Length is < 1 or > 8 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash))
                {
                    return Fail(out value, out error, "Expected 1-8 hex digits.");
                }
                value = hash;
                error = null;
                return true;

            case FcbMemberType.Enum or FcbMemberType.UInt32:
                if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint u32))
                {
                    return Fail(out value, out error, "Expected a whole number, 0 to 4294967295.");
                }
                value = u32;
                error = null;
                return true;

            case FcbMemberType.Float:
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                {
                    return Fail(out value, out error, "Expected a number.");
                }
                value = f;
                error = null;
                return true;

            case FcbMemberType.Int8:
                if (!sbyte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte i8))
                {
                    return Fail(out value, out error, "Expected a whole number, -128 to 127.");
                }
                value = i8;
                error = null;
                return true;

            case FcbMemberType.UInt8:
                if (!byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte u8))
                {
                    return Fail(out value, out error, "Expected a whole number, 0 to 255.");
                }
                value = u8;
                error = null;
                return true;

            case FcbMemberType.Int16:
                if (!short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out short i16))
                {
                    return Fail(out value, out error, "Expected a whole number, -32768 to 32767.");
                }
                value = i16;
                error = null;
                return true;

            case FcbMemberType.UInt16:
                if (!ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort u16))
                {
                    return Fail(out value, out error, "Expected a whole number, 0 to 65535.");
                }
                value = u16;
                error = null;
                return true;

            case FcbMemberType.Int32:
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i32))
                {
                    return Fail(out value, out error, "Expected a whole number, -2147483648 to 2147483647.");
                }
                value = i32;
                error = null;
                return true;

            case FcbMemberType.Int64:
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i64))
                {
                    return Fail(out value, out error, "Expected a whole number.");
                }
                value = i64;
                error = null;
                return true;

            case FcbMemberType.UInt64:
                if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong u64))
                {
                    return Fail(out value, out error, "Expected a non-negative whole number.");
                }
                value = u64;
                error = null;
                return true;

            case FcbMemberType.Vector2 or FcbMemberType.Vector3 or FcbMemberType.Vector4:
                int expected = VectorComponentCount(type);
                string[] parts = text.Split(',');
                if (parts.Length != expected)
                {
                    return Fail(out value, out error,
                        $"Expected {expected} comma-separated numbers, e.g. \"{string.Join(",", Enumerable.Repeat("0", expected))}\" - got {parts.Length}.");
                }

                var components = new float[expected];
                for (int i = 0; i < expected; i++)
                {
                    string part = parts[i].Trim();
                    if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]))
                    {
                        return Fail(out value, out error, $"'{part}' (component {i + 1}) isn't a number.");
                    }
                }
                value = components;
                error = null;
                return true;

            case FcbMemberType.BinHex:
                return TryParseHex(text, out value, out error);

            case FcbMemberType.Rml:
                // Mirrors FcbXml.ReadRml's own dual shape: text that looks like an XML document is
                // parsed and re-encoded as one (always in the base game's padded shape - see
                // FcbXml.TryDecodeRmlValue's remarks), anything else falls back to the same opaque-hex
                // editing BinHex uses.
                if (text.StartsWith('<'))
                {
                    try
                    {
                        value = FcbXml.EncodeRmlValue(XElement.Parse(text));
                        error = null;
                        return true;
                    }
                    catch (XmlException)
                    {
                        return Fail(out value, out error, "Not valid XML.");
                    }
                }
                return TryParseHex(text, out value, out error);

            default:
                throw new NotSupportedException($"'{type}' is not a scalar text field.");
        }
    }

    private static bool TryParseHex(string text, out object value, out string? error)
    {
        if (text.Length % 2 != 0)
        {
            return Fail(out value, out error, "Hex data needs an even number of digits.");
        }
        try
        {
            value = Convert.FromHexString(text);
            error = null;
            return true;
        }
        catch (FormatException)
        {
            return Fail(out value, out error, "Not valid hex - use only 0-9 and A-F.");
        }
    }

    private static bool Fail(out object value, out string? error, string message)
    {
        value = null!;
        error = message;
        return false;
    }
}
