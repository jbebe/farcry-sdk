using System.Globalization;
using System.Text;
using System.Xml;

namespace JackAll.Core.Format.Move;

/// <summary>
/// The leaf half of a MOVE document: one element per primitive, in both directions.
/// </summary>
/// <remarks>
/// Written for the fragment form (<see cref="MoveFragmentXml"/>). It repeats the leaf rules
/// <see cref="MoveXml"/> uses - a float is decimal only when re-parsing reproduces the same four
/// bytes, a string that is not clean ASCII goes out as hex - rather than being shared with it,
/// because the two forms differ in a way that cannot be factored out: the whole-file form annotates
/// integers with channel and enum names, and a fragment must not.
///
/// A fragment's text is compared against itself by <c>FragmentMerge</c> - the vanilla ancestor comes
/// from <c>Extract</c> and a mod's staged copy from <c>Canonicalize</c> - so anything decorative that
/// one emits and the other drops would make every fragment read as modified. Labels stay in the Move
/// tab and <c>move decode --names</c>, where they cost nothing.
///
/// Pointers are not handled here: how one is addressed is exactly what separates the two forms.
/// </remarks>
internal static class MoveXmlPrimitives
{
    /// <summary>Writes one non-pointer op. Returns false when the op is a pointer, which the caller
    /// must render in its own addressing scheme.</summary>
    public static bool TryWrite(XmlWriter writer, MoveOp op)
    {
        switch (op.Kind)
        {
            case MoveOpKind.NoVersion:
                writer.WriteStartElement("nover");
                writer.WriteAttributeString("n", op.Name);
                writer.WriteEndElement();
                return true;

            case MoveOpKind.Version:
                Value(writer, "ver", op.Name, op.Number.ToString(CultureInfo.InvariantCulture));
                return true;

            case MoveOpKind.F32:
                writer.WriteStartElement("f32");
                writer.WriteAttributeString("n", op.Name);
                Float(writer, op.Bytes!);
                writer.WriteEndElement();
                return true;

            case MoveOpKind.U8:
            case MoveOpKind.U32:
                Value(writer, op.Kind.ToString().ToLowerInvariant(), op.Name,
                    op.Number.ToString(CultureInfo.InvariantCulture));
                return true;

            case MoveOpKind.S32:
                Value(writer, "s32", op.Name,
                    unchecked((int)op.Number).ToString(CultureInfo.InvariantCulture));
                return true;

            case MoveOpKind.Str:
                writer.WriteStartElement("str");
                writer.WriteAttributeString("n", op.Name);
                if (MoveText.Printable(op.Bytes!) is { } clean)
                {
                    writer.WriteAttributeString("v", clean);
                }
                else
                {
                    writer.WriteAttributeString("hex", Convert.ToHexString(op.Bytes!));
                }

                writer.WriteEndElement();
                return true;

            case MoveOpKind.Data:
            case MoveOpKind.Raw:
                writer.WriteStartElement(op.Kind == MoveOpKind.Data ? "data" : "raw");
                writer.WriteAttributeString("n", op.Name);
                writer.WriteAttributeString("hex", Convert.ToHexString(op.Bytes!));
                writer.WriteEndElement();
                return true;

            default:
                return false;
        }
    }

    /// <summary>Reads one non-pointer element, or null when the name is not one of them.</summary>
    public static MoveOp? TryRead(XmlReader reader, string name)
    {
        string? hex = reader.GetAttribute("hex");
        string? value = reader.GetAttribute("v");
        switch (reader.Name)
        {
            case "nover":
                return MoveOp.Integer(MoveOpKind.NoVersion, name, 0);

            case "ver":
                return MoveOp.Integer(
                    MoveOpKind.Version, name, uint.Parse(value!, CultureInfo.InvariantCulture));

            case "u8":
                return MoveOp.Integer(
                    MoveOpKind.U8, name, uint.Parse(value!, CultureInfo.InvariantCulture));

            case "u32":
                return MoveOp.Integer(
                    MoveOpKind.U32, name, uint.Parse(value!, CultureInfo.InvariantCulture));

            case "s32":
                return MoveOp.Integer(
                    MoveOpKind.S32, name,
                    unchecked((uint)int.Parse(value!, CultureInfo.InvariantCulture)));

            case "f32":
                return MoveOp.Blob(MoveOpKind.F32, name, hex is not null
                    ? Convert.FromHexString(hex)
                    : BitConverter.GetBytes(
                        float.Parse(value!, NumberStyles.Float, CultureInfo.InvariantCulture)));

            case "str":
                return MoveOp.Blob(MoveOpKind.Str, name,
                    hex is not null ? Convert.FromHexString(hex) : Encoding.ASCII.GetBytes(value!));

            case "data":
                return MoveOp.Blob(MoveOpKind.Data, name, Convert.FromHexString(hex!));

            case "raw":
                return MoveOp.Blob(MoveOpKind.Raw, name, Convert.FromHexString(hex!));

            default:
                return null;
        }
    }

    private static void Value(XmlWriter writer, string element, string name, string value)
    {
        writer.WriteStartElement(element);
        writer.WriteAttributeString("n", name);
        writer.WriteAttributeString("v", value);
        writer.WriteEndElement();
    }

    /// <summary>Readable decimal when it round-trips, hex when it would not.</summary>
    private static void Float(XmlWriter writer, byte[] raw)
    {
        float value = BitConverter.ToSingle(raw);
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            && BitConverter.GetBytes(parsed).AsSpan().SequenceEqual(raw))
        {
            writer.WriteAttributeString("v", text);
            return;
        }

        writer.WriteAttributeString("hex", Convert.ToHexString(raw));
    }
}
