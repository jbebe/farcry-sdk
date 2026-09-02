using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;

namespace JackAll.Core.Format.Move;

/// <summary>Converts a MOVE graph to and from an editable XML document.</summary>
/// <remarks>
/// An interchange format, not one the game loads - the same relationship <c>.fcb</c> has with the
/// XML Gibbed's converter produces.
///
/// It is deliberately not <c>movemgrnamed.bin</c>, the engine's own authoring form. That addresses
/// objects by GUID rather than by stream position and no shipped executable can read it, so it
/// cannot be the basis for something that builds a loadable file. What this borrows from the engine
/// is the vocabulary: every field name is the debug string the matching Transfer call passes.
///
/// Both directions stream, because the base graph is 25 MB of XML.
/// See docs/docs/file-formats/move.md.
/// </remarks>
public static class MoveXml
{
    private const string RootName = "MoveGraph";

    /// <summary>Renders a binary MOVE graph as XML.</summary>
    public static string Decode(byte[] data, MoveLabels? labels = null)
        => ToXml(MoveCodec.Load(data), labels);

    /// <summary>Builds a document produced by <see cref="Decode"/> back into a binary graph.</summary>
    public static byte[] Encode(string xml) => MoveCodec.Save(FromXml(xml));

    public static string ToXml(MoveFile file, MoveLabels? labels = null)
    {
        StringBuilder text = new();
        XmlWriterSettings settings = new() { Indent = true, IndentChars = "  " };
        using (XmlWriter writer = XmlWriter.Create(text, settings))
        {
            writer.WriteStartElement(RootName);
            writer.WriteAttributeString("type", FourCc(file.Type));
            writer.WriteAttributeString("version", file.Version.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("flags", "0x" + file.Flags.ToString("X8"));
            foreach (MoveOp op in file.Root.Ops)
            {
                Write(writer, op, labels, null);
            }

            writer.WriteEndElement();
        }

        return text.ToString();
    }

    private static void Write(
        XmlWriter writer, MoveOp op, MoveLabels? labels, uint? channel)
    {
        switch (op.Kind)
        {
            case MoveOpKind.PointerNew:
                MoveObject target = op.Target!;
                writer.WriteStartElement("obj");
                writer.WriteAttributeString("n", op.Name);
                writer.WriteAttributeString("class", target.ClassName);
                writer.WriteAttributeString("id", target.Index.ToString(CultureInfo.InvariantCulture));
                uint? inner = target.Field("m_eValueID");
                foreach (MoveOp child in target.Ops)
                {
                    Write(writer, child, labels, inner);
                }

                writer.WriteEndElement();
                return;

            case MoveOpKind.PointerRef:
                writer.WriteStartElement("ref");
                writer.WriteAttributeString("n", op.Name);
                writer.WriteAttributeString(
                    "id", op.Target!.Index.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                return;

            case MoveOpKind.PointerNull:
                WriteNamed(writer, "null", op.Name);
                return;

            case MoveOpKind.NoVersion:
                WriteNamed(writer, "nover", op.Name);
                return;

            case MoveOpKind.Version:
                WriteValue(writer, "ver", op.Name, op.Number.ToString(CultureInfo.InvariantCulture));
                return;

            case MoveOpKind.F32:
                writer.WriteStartElement("f32");
                writer.WriteAttributeString("n", op.Name);
                WriteFloat(writer, op.Bytes!);
                writer.WriteEndElement();
                return;

            case MoveOpKind.U8:
            case MoveOpKind.U32:
                WriteInteger(writer, op, op.Number.ToString(CultureInfo.InvariantCulture), labels, channel);
                return;

            case MoveOpKind.S32:
                WriteInteger(
                    writer, op,
                    unchecked((int)op.Number).ToString(CultureInfo.InvariantCulture), labels, channel);
                return;

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
                return;

            default:
                writer.WriteStartElement(op.Kind == MoveOpKind.Data ? "data" : "raw");
                writer.WriteAttributeString("n", op.Name);
                writer.WriteAttributeString("hex", Convert.ToHexString(op.Bytes!));
                writer.WriteEndElement();
                return;
        }
    }

    private static void WriteNamed(XmlWriter writer, string element, string name)
    {
        writer.WriteStartElement(element);
        writer.WriteAttributeString("n", name);
        writer.WriteEndElement();
    }

    private static void WriteValue(XmlWriter writer, string element, string name, string value)
    {
        writer.WriteStartElement(element);
        writer.WriteAttributeString("n", name);
        writer.WriteAttributeString("v", value);
        writer.WriteEndElement();
    }

    private static void WriteInteger(
        XmlWriter writer, MoveOp op, string value,
        MoveLabels? labels, uint? channel)
    {
        writer.WriteStartElement(op.Kind.ToString().ToLowerInvariant());
        writer.WriteAttributeString("n", op.Name);
        writer.WriteAttributeString("v", value);
        Annotate(writer, op, labels, channel);
        writer.WriteEndElement();
    }

    /// <summary>Informational channel and enum labels; <see cref="FromXml"/> ignores them.</summary>
    private static void Annotate(
        XmlWriter writer, MoveOp op, MoveLabels? labels, uint? channel)
    {
        if (labels is null)
        {
            return;
        }

        if (labels.PathOf(op.Name, op.Number) is { } path)
        {
            writer.WriteAttributeString("path", path);
            return;
        }

        IReadOnlyList<MoveChannel>? channels = labels.Channels;
        if (channels is null)
        {
            return;
        }

        if (op.Name == "m_eValueID" && op.Number < channels.Count)
        {
            writer.WriteAttributeString("channel", channels[(int)op.Number].Name);
            return;
        }

        if (op.Name != "m_Value" || channel is not { } id || id >= channels.Count)
        {
            return;
        }

        IReadOnlyList<string>? values = channels[(int)id].Values;
        int index = unchecked((int)op.Number);
        if (values is not null && index >= 0 && index < values.Count)
        {
            writer.WriteAttributeString("enum", values[index]);
        }
    }

    /// <summary>Readable decimal when it round-trips, hex when it would not.</summary>
    private static void WriteFloat(XmlWriter writer, byte[] raw)
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

    private static string FourCc(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        return Encoding.Latin1.GetString(buffer).TrimEnd('\0');
    }

    public static MoveFile FromXml(string xml)
    {
        MoveFile file = new();
        Dictionary<int, MoveObject> byId = [];
        Stack<MoveObject> stack = new();
        MoveObject current = file.Root;

        using XmlReader reader = XmlReader.Create(
            new StringReader(xml), new XmlReaderSettings { IgnoreWhitespace = true });

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Name == "obj")
                {
                    current = stack.Pop();
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.Name == RootName)
            {
                ReadHeader(reader, file);
                continue;
            }

            string name = reader.GetAttribute("n") ?? string.Empty;
            if (reader.Name == "obj")
            {
                MoveObject created = new(reader.GetAttribute("class")
                    ?? throw new MoveFormatException("an <obj> has no class"))
                {
                    Index = int.Parse(reader.GetAttribute("id")!, CultureInfo.InvariantCulture),
                };
                byId[created.Index] = created;
                current.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerNew, name, created));
                if (!reader.IsEmptyElement)
                {
                    stack.Push(current);
                    current = created;
                }

                continue;
            }

            current.Ops.Add(ReadOp(reader, name, byId));
        }

        file.Objects.AddRange(byId.OrderBy(p => p.Key).Select(p => p.Value));
        return file;
    }

    private static void ReadHeader(XmlReader reader, MoveFile file)
    {
        byte[] type = Encoding.Latin1.GetBytes((reader.GetAttribute("type") ?? "MVM").PadRight(4, '\0'));
        file.Type = BinaryPrimitives.ReadUInt32LittleEndian(type);
        file.Version = uint.Parse(reader.GetAttribute("version")!, CultureInfo.InvariantCulture);
        string flags = reader.GetAttribute("flags")!;
        file.Flags = flags.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(flags[2..], 16)
            : uint.Parse(flags, CultureInfo.InvariantCulture);
    }

    private static MoveOp ReadOp(XmlReader reader, string name, Dictionary<int, MoveObject> byId)
    {
        string? hex = reader.GetAttribute("hex");
        string? value = reader.GetAttribute("v");
        switch (reader.Name)
        {
            case "ref":
                int id = int.Parse(reader.GetAttribute("id")!, CultureInfo.InvariantCulture);
                return MoveOp.Pointer(MoveOpKind.PointerRef, name, byId[id]);

            case "null":
                return MoveOp.Pointer(MoveOpKind.PointerNull, name, null);

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
                byte[] raw = hex is not null
                    ? Convert.FromHexString(hex)
                    : BitConverter.GetBytes(
                        float.Parse(value!, NumberStyles.Float, CultureInfo.InvariantCulture));
                return MoveOp.Blob(MoveOpKind.F32, name, raw);

            case "str":
                return MoveOp.Blob(
                    MoveOpKind.Str, name,
                    hex is not null ? Convert.FromHexString(hex) : Encoding.ASCII.GetBytes(value!));

            case "data":
                return MoveOp.Blob(MoveOpKind.Data, name, Convert.FromHexString(hex!));

            case "raw":
                return MoveOp.Blob(MoveOpKind.Raw, name, Convert.FromHexString(hex!));

            default:
                throw new MoveFormatException($"unexpected element <{reader.Name}>");
        }
    }
}
