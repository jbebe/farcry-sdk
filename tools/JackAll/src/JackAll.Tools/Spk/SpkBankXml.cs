using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Tools.Audio;

namespace JackAll.Tools.Spk;

/// <summary>
/// The editable text form of a <see cref="SpkBank"/>: one element per record, audio beside it as
/// files.
/// </summary>
/// <remarks>
/// A word the layout names is an attribute (<c>sound</c>, <c>gainDb</c>, <c>loop</c>); any other word
/// is written as <c>wN</c> only where it differs from its default, and a derived word only where it is
/// pinned. Audio is <c>.ogg</c> (stored verbatim), <c>.ima</c> (a raw IMA-ADPCM stream, which needs
/// <c>rate</c>) or <c>.wav</c> (16-bit PCM, encoded to IMA-ADPCM).
/// </remarks>
public static partial class SpkBankXml
{
    /// <summary>A raw blob larger than this goes to a file instead of a hex attribute.</summary>
    private const int InlineBytesLimit = 4096;

    /// <summary>Weights summing to within this of 1.0 are probabilities rather than relative weights.</summary>
    private const double ProbabilityTolerance = 0.001;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [GeneratedRegex(@"^w(\d+)$")]
    private static partial Regex WordAttribute();

    /// <summary>The bank as XML; <paramref name="writeFile"/> receives each audio stream or large blob by file name.</summary>
    public static string ToXml(SpkBank bank, Action<string, byte[]> writeFile)
    {
        var root = new XElement("SoundBank", new XAttribute("preamble", Ids(bank.Preamble)));
        foreach (SpkBankRecord record in bank.Records)
        {
            root.Add(ToElement(record, writeFile));
        }
        return FragmentXml.Render(root, "\t");
    }

    /// <summary>Reads what <see cref="ToXml"/> writes; <paramref name="readFile"/> resolves a file name the XML refers to.</summary>
    public static SpkBank FromXml(string xml, Func<string, byte[]> readFile)
    {
        XElement root = XDocument.Parse(xml).Root ?? throw new InvalidDataException("The sound bank document is empty.");
        if (root.Name.LocalName != "SoundBank")
        {
            throw new InvalidDataException($"Expected <SoundBank>, found <{root.Name.LocalName}>.");
        }

        var bank = new SpkBank { Preamble = ParseIds(root.Attribute("preamble")?.Value) };
        var wavs = new Dictionary<SpkBankRecord, WavAudio.Pcm16Audio>();
        foreach (XElement element in root.Elements())
        {
            var reader = new AttributeReader(element);
            SpkBankRecord record = FromElement(reader, readFile, wavs);
            reader.RejectUnread();
            if (bank.Find(record.Id) is not null)
            {
                throw new InvalidDataException($"Record 0x{record.Id:x8} appears twice.");
            }
            bank.Records.Add(record);
        }

        // A .wav loops seamlessly only when encoded knowing it loops, which the samples playing it say.
        foreach ((SpkBankRecord audio, WavAudio.Pcm16Audio pcm) in wavs)
        {
            bool looping = bank.Loops(audio.Id);
            audio.Data = ImaAdpcm.Encode(pcm.Samples, pcm.Channels, looping);
            audio.SampleRate = pcm.SampleRate;
        }
        return bank;
    }

    /// <summary>The file name <see cref="ToXml"/> gives an audio record's stream.</summary>
    public static string AudioFileName(SpkBankRecord audio) =>
        $"{audio.Id:x8}{(SpkBank.DescribeAudio(audio) is { Ogg: true } ? ".ogg" : ".ima")}";

    private static XElement ToElement(SpkBankRecord record, Action<string, byte[]> writeFile)
    {
        var element = new XElement(record.Element, new XAttribute("id", Id(record.Id)));
        if (record.Preamble is { } preamble)
        {
            element.Add(new XAttribute("preamble", Ids(preamble)));
        }
        if (record.Padding is { } padding)
        {
            element.Add(new XAttribute("pad", Convert.ToHexString(padding)));
        }

        if (record.Raw)
        {
            element.Add(new XAttribute("type", Id((uint)record.Type)));
            AddBytes(element, "data", record.Data, $"{record.Id:x8}.bin", writeFile);
            return element;
        }

        if (record.Key is { } key)
        {
            element.Add(new XAttribute("key", Convert.ToHexString(key)));
        }

        if (record.IsAudio)
        {
            string file = record.File ?? AudioFileName(record);
            if (record.File is null)
            {
                writeFile(file, record.Data);
            }
            element.Add(new XAttribute("file", file));
            if (SpkBank.DescribeAudio(record) is { Ogg: false } && record.SampleRate is { } rate)
            {
                element.Add(new XAttribute("rate", rate));
            }
            return element;
        }

        if (record.IsRolloff)
        {
            element.Add(record.Points.Select(p => new XElement("Point",
                new XAttribute("m", Float(p.X)), new XAttribute("db", Float(p.Y)))));
            return element;
        }

        SpkLayout layout = record.Layout!;
        if (layout.KindAttribute is { } kindAttribute)
        {
            element.Add(new XAttribute(kindAttribute, record.Kind));
        }

        foreach (SpkWordField field in layout.Fields.Where(f => f.Format != SpkWordFormat.Weight))
        {
            uint value = record.Word(field.Index);
            if (value != layout.Default(field.Index))
            {
                element.Add(new XAttribute(field.Name, FormatWord(field.Format, value)));
            }
        }

        for (int i = 0; i < record.Words.Length; i++)
        {
            uint? value = record.Pins.TryGetValue(i, out uint pin) ? pin
                : !layout.IsNamed(i) && record.Words[i] != layout.Default(i) ? record.Words[i]
                : null;
            if (value is { } word)
            {
                element.Add(new XAttribute($"w{i}", Id(word)));
            }
        }

        if (record.Tail is { } tail)
        {
            AddBytes(element, "tail", tail, $"{record.Id:x8}.tail.bin", writeFile);
            return element;
        }

        if (layout == SpkLayout.Random)
        {
            AddWeights(element, record);
            return element;
        }

        if (layout.Children is { } shape)
        {
            element.Add(record.Entries.Select(e => new XElement(shape.Element,
                shape.ValueAt >= 0 ? new XAttribute("value", Id(e.Value)) : null,
                new XAttribute(shape.RefAttribute, Id(e.Ref)))));
        }
        element.Add(record.Layers.Select(l => new XElement("Layer", new XAttribute("resource", Id(l.Resource)),
            l.Curves.Select(c => new XElement("Curve",
                new XAttribute("target", c.Target switch { 0 => "volume", 1 => "pitch", _ => c.Target.ToString(Invariant) }),
                new XAttribute("parameter", Id(c.Parameter)),
                c.Points.Select(p => new XElement("Point", new XAttribute("x", Float(p.X)), new XAttribute("y", Float(p.Y)))))))));
        return element;
    }

    /// <summary>
    /// Writes a random container's weights the shortest way that reads back exactly: nothing when they
    /// are equal, else probabilities, else raw Q16.16.
    /// </summary>
    private static void AddWeights(XElement element, SpkBankRecord record)
    {
        uint silence = record.Word(8);
        uint[] raw = [.. record.Entries.Select(e => e.Value)];
        int slots = raw.Length + (silence == 0 ? 0 : 1);
        bool equal = raw.Length > 0 && raw.All(w => w == raw[0]) && (silence == 0 || silence == raw[0])
            && raw[0] == SpkLayout.One / (uint)slots;

        string?[] weights;
        string? silenceText;
        if (equal)
        {
            weights = new string?[raw.Length];
            silenceText = silence == 0 ? null : "1";
        }
        else
        {
            weights = [.. raw.Select(Probability)];
            silenceText = silence == 0 ? null : Probability(silence);
            if (!ReadWeights(weights, silenceText).SequenceEqual([.. raw, silence]))
            {
                weights = [.. raw.Select(Id)];
                silenceText = Id(silence);
            }
        }

        if (silenceText is not null)
        {
            element.Add(new XAttribute("silence", silenceText));
        }
        for (int i = 0; i < raw.Length; i++)
        {
            SpkEntry entry = record.Entries[i];
            element.Add(new XElement("Choice", new XAttribute("resource", Id(entry.Ref)),
                weights[i] is { } weight ? new XAttribute("weight", weight) : null,
                entry.Extra != 0 ? new XAttribute("repeat", FormatWord(SpkWordFormat.Bool, entry.Extra)) : null));
        }
    }

    private static string Probability(uint raw) => (raw / (double)SpkLayout.One).ToString("0.######", Invariant);

    /// <summary>Q16.16 weights for the entries and then silence: raw when written as hex, else
    /// probabilities when they sum to 1.0, else relative weights normalized to 1.0.</summary>
    private static uint[] ReadWeights(IReadOnlyList<string?> weights, string? silence)
    {
        string?[] all = [.. weights, silence];
        if (all.Any(w => w?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true))
        {
            return [.. all.Select(w => w is null ? 0 : ParseUInt(w))];
        }

        double[] values = [.. weights.Select(w => w is null ? 1.0 : ParseDouble(w)), silence is null ? 0 : ParseDouble(silence)];
        double total = values.Sum();
        if (total <= 0 || values.Any(v => v < 0))
        {
            throw new InvalidDataException("A random container's weights must be non-negative and not all zero.");
        }
        return Math.Abs(total - 1) <= ProbabilityTolerance
            ? [.. values.Select(v => (uint)Math.Round(v * SpkLayout.One))]
            : [.. values.Select(v => (uint)Math.Floor(v * SpkLayout.One / total))];
    }

    private static SpkBankRecord FromElement(AttributeReader reader, Func<string, byte[]> readFile,
        Dictionary<SpkBankRecord, WavAudio.Pcm16Audio> wavs)
    {
        XElement element = reader.Element;
        string name = element.Name.LocalName;
        var record = new SpkBankRecord
        {
            Id = ParseUInt(reader.Required("id")), Type = SpkRecordType.SimpleFixed68,
            Preamble = reader.Optional("preamble") is { } preamble ? ParseIds(preamble) : null,
            Padding = reader.Optional("pad") is { } pad ? Convert.FromHexString(pad) : null,
        };
        uint id = record.Id;
        if (name == "Record")
        {
            record.Raw = true;
            record.Type = (SpkRecordType)ParseUInt(reader.Required("type"));
            record.Data = ReadBytes(reader, "data", readFile);
            return record;
        }

        if (reader.Optional("key") is { } key)
        {
            record.Key = Convert.FromHexString(key);
        }

        if (name == "Audio")
        {
            record.Type = SpkRecordType.FlatCopy;
            string file = reader.Required("file");
            byte[] bytes = readFile(file);
            record.File = file;
            record.SampleRate = reader.Optional("rate") is { } rate ? int.Parse(rate, Invariant) : null;
            if (Path.GetExtension(file).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                wavs[record] = WavAudio.ReadPcm16(bytes);
            }
            else
            {
                record.Data = bytes;
            }
            return record;
        }

        if (name == "Rolloff")
        {
            record.Type = SpkRecordType.SelfReferential;
            record.Points = [.. Children(reader, "Point").Select(p => new SpkPoint(
                ParseFloat(p.Required("m")), ParseFloat(p.Required("db"))))];
            return record;
        }

        SpkLayout layout = SpkLayout.All.FirstOrDefault(l => l.Element == name)
            ?? throw new InvalidDataException($"<{name}> is not a sound bank record.");
        record.Type = layout.IsEvent ? SpkRecordType.SimpleFixed68 : SpkRecordType.TransformedFixed128;
        record.Kind = layout.Kind ?? ParseUInt(reader.Required(layout.KindAttribute!));
        record.Words = layout.NewWords();

        foreach (SpkWordField field in layout.Fields.Where(f => f.Format != SpkWordFormat.Weight))
        {
            if (reader.Optional(field.Name) is { } text)
            {
                record.Words[field.Index] = ParseWord(field.Format, text);
            }
        }

        foreach (XAttribute attribute in element.Attributes())
        {
            if (WordAttribute().Match(attribute.Name.LocalName) is { Success: true } match)
            {
                int index = int.Parse(match.Groups[1].Value, Invariant);
                if (index >= record.Words.Length)
                {
                    throw new InvalidDataException($"<{name} id=\"{Id(id)}\"> has no word {index}.");
                }
                uint value = ParseUInt(reader.Required(attribute.Name.LocalName));
                if (layout.Derived.Contains(index))
                {
                    record.Pins[index] = value;
                }
                else
                {
                    record.Words[index] = value;
                }
            }
        }

        if (reader.Optional("tail") is not null || reader.Optional("tailFile") is not null)
        {
            record.Tail = ReadBytes(reader, "tail", readFile);
            return record;
        }

        if (layout == SpkLayout.Random)
        {
            AttributeReader[] choices = [.. Children(reader, "Choice")];
            uint[] raw = ReadWeights([.. choices.Select(c => c.Optional("weight"))], reader.Optional("silence"));
            record.Words[8] = raw[^1];
            record.Entries = [.. choices.Select((c, i) => new SpkEntry(ParseUInt(c.Required("resource")), raw[i],
                c.Optional("repeat") is { } repeat ? ParseWord(SpkWordFormat.Bool, repeat) : 0))];
        }
        else if (layout.Children is { } shape)
        {
            record.Entries = [.. Children(reader, shape.Element).Select(c => new SpkEntry(
                ParseUInt(c.Required(shape.RefAttribute)), shape.ValueAt >= 0 ? ParseUInt(c.Required("value")) : 0))];
        }
        else if (layout == SpkLayout.Multilayer)
        {
            record.Layers = [.. Children(reader, "Layer").Select(l => new SpkLayer(ParseUInt(l.Required("resource")),
                [.. Children(l, "Curve").Select(c => new SpkCurve(
                    c.Required("target") switch { "volume" => 0, "pitch" => 1, var t => ParseUInt(t) },
                    ParseUInt(c.Required("parameter")),
                    [.. Children(c, "Point").Select(p => new SpkPoint(ParseFloat(p.Required("x")), ParseFloat(p.Required("y"))))]))]))];
        }
        else if (element.HasElements)
        {
            throw new InvalidDataException($"<{name} id=\"{Id(id)}\"> takes no child elements.");
        }
        return record;
    }

    private static AttributeReader[] Children(AttributeReader parent, string name) =>
        [.. parent.Element.Elements().Select(child => child.Name.LocalName == name ? parent.Child(child)
            : throw new InvalidDataException(
                $"<{parent.Element.Name.LocalName}> takes <{name}> children, not <{child.Name.LocalName}>."))];

    private static void AddBytes(XElement element, string attribute, byte[] bytes, string fileName, Action<string, byte[]> writeFile)
    {
        if (bytes.Length <= InlineBytesLimit)
        {
            element.Add(new XAttribute(attribute, Convert.ToHexString(bytes)));
            return;
        }
        writeFile(fileName, bytes);
        element.Add(new XAttribute(attribute + "File", fileName));
    }

    private static byte[] ReadBytes(AttributeReader reader, string attribute, Func<string, byte[]> readFile) =>
        reader.Optional(attribute) is { } hex ? Convert.FromHexString(hex)
        : readFile(reader.Required(attribute + "File"));

    private static string FormatWord(SpkWordFormat format, uint value) => format switch
    {
        SpkWordFormat.Q16 => SpkLayout.FromQ16(value).ToString("R", Invariant),
        SpkWordFormat.Bool => value == 1 ? "true" : Id(value),
        _ => Id(value),
    };

    private static uint ParseWord(SpkWordFormat format, string text) => format switch
    {
        SpkWordFormat.Q16 => SpkLayout.ToQ16(ParseDouble(text)),
        SpkWordFormat.Bool => text switch { "true" => 1u, "false" => 0u, _ => ParseUInt(text) },
        _ => ParseUInt(text),
    };

    private static string Id(uint value) => $"0x{value:x8}";

    private static string Ids(IEnumerable<uint> ids) => string.Join(" ", ids.Select(Id));

    private static uint[] ParseIds(string? text) =>
        [.. (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(ParseUInt)];

    private static string Float(float value) => value.ToString("R", Invariant);

    private static float ParseFloat(string text) => float.Parse(text, Invariant);

    private static double ParseDouble(string text) => double.Parse(text, Invariant);

    /// <summary>A word as hex (<c>0x</c>) or as a decimal, signed or not.</summary>
    private static uint ParseUInt(string text) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.Parse(text.AsSpan(2), NumberStyles.HexNumber, Invariant)
            : text.StartsWith('-') ? (uint)int.Parse(text, Invariant) : uint.Parse(text, Invariant);

    /// <summary>Reads an element's attributes and fails on any left unread, here or in a child, so a
    /// misspelt one is an error.</summary>
    private sealed class AttributeReader(XElement element)
    {
        private readonly HashSet<string> _read = [];
        private readonly List<AttributeReader> _children = [];

        public XElement Element => element;

        public AttributeReader Child(XElement child)
        {
            var reader = new AttributeReader(child);
            _children.Add(reader);
            return reader;
        }

        public string? Optional(string name)
        {
            _read.Add(name);
            return element.Attribute(name)?.Value;
        }

        public string Required(string name) => Optional(name)
            ?? throw new InvalidDataException($"<{element.Name.LocalName}> needs a {name} attribute.");

        public void RejectUnread()
        {
            string[] unread = [.. element.Attributes().Select(a => a.Name.LocalName).Where(n => !_read.Contains(n))];
            if (unread.Length > 0)
            {
                throw new InvalidDataException(
                    $"<{element.Name.LocalName}> does not take {string.Join(", ", unread)}.");
            }
            _children.ForEach(c => c.RejectUnread());
        }
    }
}
