namespace JackAll.Core.Format.Fcb;

/// <summary>
/// Converts one <see cref="FcbObject"/> value's raw bytes to and from a native .NET representation,
/// for a structured (not text) editor — a property grid binds directly against what
/// <see cref="TryDecode"/> returns, and <see cref="Encode"/> packs an edited value straight back to
/// the same bytes <see cref="FcbDocument"/> would have written.
/// </summary>
/// <remarks>
/// Mirrors <see cref="FcbXml"/>'s private <c>TryWriteValue</c>/<c>ReadValue</c> byte layouts exactly
/// (same field order, same little-endian count-prefixed arrays, same row-major <c>Matrix4</c>) rather
/// than routing through XML text — a property grid edits one field at a time and needs a fast, direct
/// byte↔native-value path, not a throwaway XML element per keystroke. Correctness against the real
/// binary format is verified independently in <c>FcbValueCodecTests</c> by decoding and re-encoding
/// every value in real shipped fragments and asserting the bytes come back identical.
///
/// Decoded shapes, by <see cref="FcbMemberType"/>:
///   <see cref="FcbMemberType.BinHex"/>, <see cref="FcbMemberType.Rml"/> → <c>byte[]</c> (edited as a
///     hex string - same "just bytes" treatment either way, per design: Rml isn't decoded into its
///     own structure, but there's no reason its hex can't be hand-edited like any other hex value).
///   <see cref="FcbMemberType.String"/> → <c>string</c>.
///   <see cref="FcbMemberType.Bool"/>/<see cref="FcbMemberType.Bool16"/>/<see cref="FcbMemberType.Bool32"/> → <c>bool</c>.
///   <see cref="FcbMemberType.Enum"/> → <c>uint</c> (the raw numeric value - no enum-name table exists
///     to resolve it any further than <see cref="FcbXml"/> itself does).
///   <see cref="FcbMemberType.Hash"/> → <c>uint</c>.
///   Every other integer/float scalar → its natural CLR numeric type (<c>sbyte</c>/<c>byte</c>/
///     <c>short</c>/<c>ushort</c>/<c>int</c>/<c>uint</c>/<c>long</c>/<c>ulong</c>/<c>float</c>).
///   <see cref="FcbMemberType.Vector2"/>/<see cref="FcbMemberType.Vector3"/>/<see cref="FcbMemberType.Vector4"/> → <c>float[]</c> (length 2/3/4).
///   <see cref="FcbMemberType.Matrix4"/> → <c>float[]</c> (length 16, row-major - row 0 is indices 0-3, etc.).
///   <see cref="FcbMemberType.UInt32Array"/>/<see cref="FcbMemberType.Int32Array"/>/<see cref="FcbMemberType.FloatArray"/> → <c>uint[]</c>/<c>int[]</c>/<c>float[]</c>.
///   <see cref="FcbMemberType.HashArray"/> → <c>uint[]</c> (same representation as UInt32Array - the
///     property grid formats items as hex instead of decimal, nothing about the byte shape differs).
///   <see cref="FcbMemberType.Bool32Array"/> → <c>bool[]</c>.
///   <see cref="FcbMemberType.Vector3Array"/> → <c>float[][]</c> (each inner array length 3).
/// </remarks>
public static class FcbValueCodec
{
    /// <summary>
    /// Decodes <paramref name="value"/> for <paramref name="type"/>, or returns false on a shape
    /// mismatch (wrong byte length for a fixed-size type, a malformed count-prefixed array, etc.) -
    /// the same "doesn't actually match its declared type" case <see cref="FcbXml"/> falls back to
    /// BinHex for, which is exactly what a caller here should do too: show the raw hex editor instead.
    /// </summary>
    public static bool TryDecode(FcbMemberType type, byte[] value, out object decoded)
    {
        try
        {
            switch (type)
            {
                case FcbMemberType.BinHex:
                case FcbMemberType.Rml:
                    decoded = value;
                    return true;

                case FcbMemberType.String:
                    if (value.Length < 1 || value[^1] != 0) break;
                    decoded = System.Text.Encoding.UTF8.GetString(value, 0, value.Length - 1);
                    return true;

                case FcbMemberType.Hash:
                    if (value.Length != 4) break;
                    decoded = BitConverter.ToUInt32(value, 0);
                    return true;

                case FcbMemberType.Enum:
                    if (value.Length != 4) break;
                    decoded = BitConverter.ToUInt32(value, 0);
                    return true;

                case FcbMemberType.Bool:
                    if (value.Length != 1) break;
                    decoded = value[0] != 0;
                    return true;

                case FcbMemberType.Bool16:
                    if (value.Length != 2) break;
                    decoded = BitConverter.ToUInt16(value, 0) != 0;
                    return true;

                case FcbMemberType.Bool32:
                    if (value.Length != 4) break;
                    decoded = BitConverter.ToUInt32(value, 0) != 0;
                    return true;

                case FcbMemberType.Float:
                    if (value.Length != 4) break;
                    decoded = BitConverter.ToSingle(value, 0);
                    return true;

                case FcbMemberType.Int8:
                    if (value.Length != 1) break;
                    decoded = (sbyte)value[0];
                    return true;

                case FcbMemberType.UInt8:
                    if (value.Length != 1) break;
                    decoded = value[0];
                    return true;

                case FcbMemberType.Int16:
                    if (value.Length != 2) break;
                    decoded = BitConverter.ToInt16(value, 0);
                    return true;

                case FcbMemberType.UInt16:
                    if (value.Length != 2) break;
                    decoded = BitConverter.ToUInt16(value, 0);
                    return true;

                case FcbMemberType.Int32:
                    if (value.Length != 4) break;
                    decoded = BitConverter.ToInt32(value, 0);
                    return true;

                case FcbMemberType.UInt32:
                    if (value.Length != 4) break;
                    decoded = BitConverter.ToUInt32(value, 0);
                    return true;

                case FcbMemberType.Int64:
                    if (value.Length != 8) break;
                    decoded = BitConverter.ToInt64(value, 0);
                    return true;

                case FcbMemberType.UInt64:
                    if (value.Length != 8) break;
                    decoded = BitConverter.ToUInt64(value, 0);
                    return true;

                case FcbMemberType.Vector2:
                    if (value.Length != 4 * 2) break;
                    decoded = ReadFloats(value, 0, 2);
                    return true;

                case FcbMemberType.Vector3:
                    if (value.Length != 4 * 3) break;
                    decoded = ReadFloats(value, 0, 3);
                    return true;

                case FcbMemberType.Vector4:
                    if (value.Length != 4 * 4) break;
                    decoded = ReadFloats(value, 0, 4);
                    return true;

                case FcbMemberType.Matrix4:
                    if (value.Length != 4 * 16) break;
                    decoded = ReadFloats(value, 0, 16);
                    return true;

                case FcbMemberType.UInt32Array:
                    if (!TryReadFixedArray(value, 4, (v, o) => (object)BitConverter.ToUInt32(v, o), out object[] uints)) break;
                    decoded = Array.ConvertAll(uints, u => (uint)u);
                    return true;

                case FcbMemberType.HashArray:
                    if (!TryReadFixedArray(value, 4, (v, o) => (object)BitConverter.ToUInt32(v, o), out object[] hashes)) break;
                    decoded = Array.ConvertAll(hashes, h => (uint)h);
                    return true;

                case FcbMemberType.Int32Array:
                    if (!TryReadFixedArray(value, 4, (v, o) => (object)BitConverter.ToInt32(v, o), out object[] ints)) break;
                    decoded = Array.ConvertAll(ints, i => (int)i);
                    return true;

                case FcbMemberType.FloatArray:
                    if (!TryReadFixedArray(value, 4, (v, o) => (object)BitConverter.ToSingle(v, o), out object[] floats)) break;
                    decoded = Array.ConvertAll(floats, f => (float)f);
                    return true;

                case FcbMemberType.Bool32Array:
                    if (!TryReadFixedArray(value, 4, (v, o) => (object)(BitConverter.ToUInt32(v, o) != 0), out object[] bools)) break;
                    decoded = Array.ConvertAll(bools, b => (bool)b);
                    return true;

                case FcbMemberType.Vector3Array:
                    if (!TryReadFixedArray(value, 4 * 3, (v, o) => (object)ReadFloats(v, o, 3), out object[] vectors)) break;
                    decoded = Array.ConvertAll(vectors, v => (float[])v);
                    return true;
            }
        }
        catch
        {
            // A caught exception here (out-of-range index, etc.) means the same thing as any other
            // shape mismatch above - fall through to the "not decodable as this type" result.
        }

        decoded = Array.Empty<byte>();
        return false;
    }

    /// <summary>
    /// Packs <paramref name="value"/> (of whatever native type <see cref="TryDecode"/> would have
    /// produced for <paramref name="type"/>) back to bytes. Callers are expected to always pass a
    /// value that started life as a <see cref="TryDecode"/> result and was only ever edited in place
    /// (e.g. a property row's own float/bool/array field) - an <see cref="InvalidCastException"/> here
    /// means a caller passed the wrong CLR type for <paramref name="type"/>, a programming error, not
    /// something a property grid needs to recover from at run time.
    /// </summary>
    public static byte[] Encode(FcbMemberType type, object value) => type switch
    {
        FcbMemberType.BinHex or FcbMemberType.Rml => (byte[])value,
        FcbMemberType.String => NullTerminate(System.Text.Encoding.UTF8.GetBytes((string)value)),
        FcbMemberType.Hash => BitConverter.GetBytes((uint)value),
        FcbMemberType.Enum => BitConverter.GetBytes((uint)value),
        FcbMemberType.Bool => [(byte)((bool)value ? 1 : 0)],
        FcbMemberType.Bool16 => BitConverter.GetBytes((ushort)((bool)value ? 1 : 0)),
        FcbMemberType.Bool32 => BitConverter.GetBytes((uint)((bool)value ? 1 : 0)),
        FcbMemberType.Float => BitConverter.GetBytes((float)value),
        FcbMemberType.Int8 => [(byte)(sbyte)value],
        FcbMemberType.UInt8 => [(byte)value],
        FcbMemberType.Int16 => BitConverter.GetBytes((short)value),
        FcbMemberType.UInt16 => BitConverter.GetBytes((ushort)value),
        FcbMemberType.Int32 => BitConverter.GetBytes((int)value),
        FcbMemberType.UInt32 => BitConverter.GetBytes((uint)value),
        FcbMemberType.Int64 => BitConverter.GetBytes((long)value),
        FcbMemberType.UInt64 => BitConverter.GetBytes((ulong)value),
        FcbMemberType.Vector2 => WriteFloats((float[])value),
        FcbMemberType.Vector3 => WriteFloats((float[])value),
        FcbMemberType.Vector4 => WriteFloats((float[])value),
        FcbMemberType.Matrix4 => WriteFloats((float[])value),
        FcbMemberType.UInt32Array => WriteFixedArray((uint[])value, 4, (buf, o, v) => BitConverter.GetBytes(v).CopyTo(buf, o)),
        FcbMemberType.HashArray => WriteFixedArray((uint[])value, 4, (buf, o, v) => BitConverter.GetBytes(v).CopyTo(buf, o)),
        FcbMemberType.Int32Array => WriteFixedArray((int[])value, 4, (buf, o, v) => BitConverter.GetBytes(v).CopyTo(buf, o)),
        FcbMemberType.FloatArray => WriteFixedArray((float[])value, 4, (buf, o, v) => BitConverter.GetBytes(v).CopyTo(buf, o)),
        FcbMemberType.Bool32Array => WriteFixedArray((bool[])value, 4, (buf, o, v) => BitConverter.GetBytes((uint)(v ? 1 : 0)).CopyTo(buf, o)),
        FcbMemberType.Vector3Array => WriteFixedArray((float[][])value, 4 * 3, (buf, o, v) => WriteFloats(v).CopyTo(buf, o)),
        _ => throw new NotSupportedException($"Unsupported FCB value type '{type}'."),
    };

    private static float[] ReadFloats(byte[] value, int offset, int count)
    {
        var result = new float[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = BitConverter.ToSingle(value, offset + (i * 4));
        }
        return result;
    }

    private static byte[] WriteFloats(float[] values)
    {
        var result = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            BitConverter.GetBytes(values[i]).CopyTo(result, i * 4);
        }
        return result;
    }

    private static bool TryReadFixedArray(byte[] value, int itemSize, Func<byte[], int, object> readItem, out object[] items)
    {
        if (value.Length < 4)
        {
            items = [];
            return false;
        }

        int count = BitConverter.ToInt32(value, 0);
        if (count < 0 || value.Length != 4 + (count * itemSize))
        {
            items = [];
            return false;
        }

        items = new object[count];
        for (int i = 0, offset = 4; i < count; i++, offset += itemSize)
        {
            items[i] = readItem(value, offset);
        }
        return true;
    }

    private static byte[] WriteFixedArray<T>(T[] values, int itemSize, Action<byte[], int, T> writeItem)
    {
        var result = new byte[4 + (values.Length * itemSize)];
        BitConverter.GetBytes(values.Length).CopyTo(result, 0);
        for (int i = 0; i < values.Length; i++)
        {
            writeItem(result, 4 + (i * itemSize), values[i]);
        }
        return result;
    }

    private static byte[] NullTerminate(byte[] utf8)
    {
        byte[] result = new byte[utf8.Length + 1];
        utf8.CopyTo(result, 0);
        return result;
    }
}
