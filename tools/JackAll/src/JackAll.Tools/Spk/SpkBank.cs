using System.Buffers.Binary;
using System.Security.Cryptography;
using JackAll.Tools.Audio;
using JackAll.Tools.Sbao;

namespace JackAll.Tools.Spk;

public readonly record struct SpkPoint(float X, float Y);

/// <summary>One child of a composite: an event of a multi-event or switch event, or a resource of a
/// switch or random container. <see cref="Value"/> is the switch value or the Q16.16 weight;
/// <see cref="Extra"/> is a random entry's unidentified second word.</summary>
public sealed record SpkEntry(uint Ref, uint Value = 0, uint Extra = 0);

/// <summary>One curve of a multilayer layer: <see cref="Target"/> 0 maps the game parameter to dB,
/// 1 to a pitch ratio.</summary>
public sealed record SpkCurve(uint Target, uint Parameter, List<SpkPoint> Points);

public sealed record SpkLayer(uint Resource, List<SpkCurve> Curves);

/// <summary>What a record id is expected to point at.</summary>
public enum SpkReference
{
    Event,
    Resource,
    Audio,
    Rolloff,
}

/// <summary>One record of a <see cref="SpkBank"/> in editable form.</summary>
public sealed class SpkBankRecord
{
    public required uint Id { get; set; }

    public required SpkRecordType Type { get; set; }

    /// <summary>An event's type or a resource's kind: word [1].</summary>
    public uint Kind { get; set; }

    /// <summary>A record whose core or body this model does not read; <see cref="Data"/> is its payload.</summary>
    public bool Raw { get; set; }

    /// <summary>Core bytes 0x08-0x17, unidentified. Null derives them from the id.</summary>
    public byte[]? Key { get; set; }

    /// <summary>Null for the bank's own preamble.</summary>
    public uint[]? Preamble { get; set; }

    /// <summary>The sub-header words; the derived ones are recomputed on write.</summary>
    public uint[] Words { get; set; } = [];

    /// <summary>Derived words forced to a value the writer would not compute.</summary>
    public SortedDictionary<int, uint> Pins { get; } = [];

    public List<SpkEntry> Entries { get; set; } = [];

    public List<SpkLayer> Layers { get; set; } = [];

    /// <summary>A rolloff curve's (metres, dB) points.</summary>
    public List<SpkPoint> Points { get; set; } = [];

    /// <summary>The tail as stored, for one <see cref="Entries"/> and <see cref="Layers"/> cannot express.</summary>
    public byte[]? Tail { get; set; }

    /// <summary>An audio record's stream, or a raw record's whole payload.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>An IMA-ADPCM stream's sample rate, which the stream itself does not carry.</summary>
    public int? SampleRate { get; set; }

    /// <summary>The file an audio record's XML names; set, <see cref="SpkBankXml.ToXml"/> refers to it
    /// instead of writing the stream out.</summary>
    public string? File { get; set; }

    public bool IsEvent => !Raw && Type == SpkRecordType.SimpleFixed68;

    public bool IsResource => !Raw && Type == SpkRecordType.TransformedFixed128;

    public bool IsAudio => !Raw && Type == SpkRecordType.FlatCopy;

    public bool IsRolloff => !Raw && Type == SpkRecordType.SelfReferential;

    public SpkLayout? Layout => IsEvent ? SpkLayout.ForEvent(Kind) : IsResource ? SpkLayout.ForResource(Kind) : null;

    public uint Word(int index) => index < Words.Length ? Words[index] : 0;

    /// <summary>The ids this record points at, and what each should be.</summary>
    public IEnumerable<(uint Id, SpkReference Kind)> References()
    {
        SpkLayout? layout = Layout;
        IEnumerable<(uint, SpkReference)> links = layout switch
        {
            _ when layout == SpkLayout.Play => [(Word(2), SpkReference.Resource), (Word(7), SpkReference.Rolloff)],
            _ when layout == SpkLayout.StopNGo => [(Word(2), SpkReference.Event), (Word(3), SpkReference.Event)],
            _ when layout == SpkLayout.SwitchEvent => [(Word(4), SpkReference.Event), .. Children(SpkReference.Event)],
            _ when layout == SpkLayout.MultiEvent => Children(SpkReference.Event),
            _ when layout == SpkLayout.OtherEvent => Kind switch
            {
                (uint)SpkEventType.NoOpLinked => [(Word(2), SpkReference.Event)],
                5 or 6 or 7 or 9 => [(Word(2), SpkReference.Resource)],
                _ => [],
            },
            _ when layout == SpkLayout.Sample => [(Word(7), SpkReference.Audio)],
            _ when layout == SpkLayout.Switch => [(Word(9), SpkReference.Resource), .. Children(SpkReference.Resource)],
            _ when layout == SpkLayout.Random => Children(SpkReference.Resource),
            _ when layout == SpkLayout.Multilayer => Layers.Select(l => (l.Resource, SpkReference.Resource)),
            _ => [],
        };
        return links.Where(l => l.Item1 is not (0 or SpkLayout.NoId));
    }

    private IEnumerable<(uint, SpkReference)> Children(SpkReference kind) => Entries.Select(e => (e.Ref, kind));
}

/// <summary>
/// An .spk bank as editable records: named words, child lists and audio instead of payload bytes.
/// </summary>
/// <remarks>
/// Writing derives every word the rest of the bank determines (own id, type, child counts and
/// offsets, a sample's lengths, rate, channels and codec from its audio), so an author edits meaning
/// and never an offset. What the derivation does not reproduce in a shipped bank is kept as a pin or
/// a raw tail, so <see cref="Parse"/> then <see cref="Write"/> returns the input byte for byte.
/// Layouts: docs/docs/file-formats/spk.md.
/// </remarks>
public sealed class SpkBank
{
    private const uint CoreMagic = 0x10001F02;
    private const uint CoreTrailer = 2;
    private const int KeyOffset = 0x08, KeyLength = 16;
    private const int LayerSize = 28, CurveSize = 24, PointSize = 8;

    /// <summary>A retail IMA-ADPCM sample declares this many frames fewer than its stream holds (29 or 30).</summary>
    private const int ImaFrameShortfall = 30;

    private const uint OggCodec = 4, ImaCodec = 3;

    /// <summary>The bank's own id plus every parent that pulls it in; see the spk page.</summary>
    public uint[] Preamble { get; set; } = [];

    public List<SpkBankRecord> Records { get; } = [];

    public SpkBankRecord? Find(uint id) => Records.Find(r => r.Id == id);

    public static SpkBank Parse(byte[] data)
    {
        SpkPackage package = SpkPackage.Parse(data);
        var bank = new SpkBank { Preamble = [.. package.Records.FirstOrDefault()?.PreambleWords ?? []] };
        foreach (SpkRecord record in package.Records)
        {
            SpkBankRecord read = Read(record);
            if (!record.PreambleWords.SequenceEqual(bank.Preamble))
            {
                read.Preamble = [.. record.PreambleWords];
            }
            bank.Records.Add(read);
        }

        // An IMA-ADPCM stream's rate lives only in the samples that play it.
        foreach (SpkBankRecord sample in bank.Records.Where(r => r.Layout == SpkLayout.Sample))
        {
            if (bank.Find(sample.Word(7)) is { IsAudio: true } audio && DescribeAudio(audio) is { Ogg: false })
            {
                audio.SampleRate ??= (int)sample.Word(SpkLayout.SampleRate);
            }
        }

        for (int i = 0; i < bank.Records.Count; i++)
        {
            bank.PinStored(bank.Records[i], package.Records[i].Payload);
        }
        return bank;
    }

    public byte[] Write()
    {
        var output = new List<byte>();
        Append(output, SpkPackage.Magic);
        Append(output, (uint)Records.Count);
        Records.ForEach(r => Append(output, r.Id));
        foreach (SpkBankRecord record in Records)
        {
            uint[] preamble = record.Preamble ?? Preamble;
            Append(output, (uint)preamble.Length);
            Array.ForEach(preamble, w => Append(output, w));
            byte[] payload = Payload(record);
            Append(output, (uint)payload.Length);
            output.AddRange(payload);
            output.AddRange(new byte[(4 - output.Count % 4) % 4]);
        }
        return [.. output];
    }

    /// <summary>The words <see cref="Write"/> stores for this record: its own, then the derived ones, then its pins.</summary>
    public uint[] DerivedWords(SpkBankRecord record)
    {
        SpkLayout layout = record.Layout ?? throw new InvalidOperationException("Only events and resources have words.");
        uint[] words = new uint[record.IsEvent ? SpkLayout.EventWords : SpkLayout.ResourceWords];
        record.Words.AsSpan(0, Math.Min(record.Words.Length, words.Length)).CopyTo(words);
        foreach (int index in layout.Derived)
        {
            words[index] = 0;
        }

        words[0] = record.Id;
        words[1] = record.Kind;
        int children = record.Layers.Count + record.Entries.Count;
        if (layout == SpkLayout.MultiEvent)
        {
            words[3] = (uint)children;
        }
        else if (layout == SpkLayout.SwitchEvent)
        {
            words[6] = (uint)children;
        }
        else if (layout == SpkLayout.Sample)
        {
            DeriveSample(words, Find(words[7]));
        }
        else if (layout.Derived.Contains(7))
        {
            words[7] = (uint)children;
        }

        foreach ((int index, uint value) in record.Pins)
        {
            words[index] = value;
        }
        return words;
    }

    /// <summary>What a sample's audio record holds, or null when it is not an audio record in this bank.</summary>
    public static (bool Ogg, int Channels, int SampleRate, long Frames)? DescribeAudio(SpkBankRecord? audio)
    {
        if (audio is not { IsAudio: true, Data: var data })
        {
            return null;
        }

        if (SbaoAudio.TryReadVorbisId(data) is { } vorbis)
        {
            return (true, vorbis.Channels, vorbis.SampleRate, LastGranule(data));
        }

        if (data.Length < ImaAdpcm.HeaderSize || data[0] != ImaAdpcm.ExpectedVersion)
        {
            return null;
        }
        int channels = data[0x0C] == 0 ? 1 : 2;
        long frames = Math.Max(0, (data.Length - ImaAdpcm.HeaderSize) * 2L / channels - ImaFrameShortfall);
        return (false, channels, audio.SampleRate ?? 0, frames);
    }

    private void DeriveSample(uint[] words, SpkBankRecord? audio)
    {
        if (DescribeAudio(audio) is not { } info)
        {
            return;
        }

        uint length = (uint)audio!.Data.Length;
        uint frames = (uint)info.Frames;
        bool loop = words[13] == 1;
        words[SpkLayout.SampleByteLength] = length;
        words[SpkLayout.SampleChannels] = (uint)info.Channels;
        words[SpkLayout.SampleRate] = (uint)info.SampleRate;
        words[SpkLayout.SampleByteRate] = frames == 0 ? 0 : (uint)((long)length * info.SampleRate / frames);
        words[SpkLayout.SampleOneShotFrames] = loop ? 0 : frames;
        words[SpkLayout.SampleOneShotBytes] = loop ? 0 : length;
        words[SpkLayout.SampleLoopFrames] = loop ? frames : 0;
        words[SpkLayout.SampleLoopBytes] = loop ? length : 0;
        words[SpkLayout.SampleCodec] = info.Ogg ? OggCodec : ImaCodec;
    }

    /// <summary>The granule position of an Ogg stream's last page: its length in frames.</summary>
    private static long LastGranule(byte[] ogg)
    {
        for (int i = ogg.AsSpan().LastIndexOf("OggS"u8); i >= 0; i = ogg.AsSpan(0, i).LastIndexOf("OggS"u8))
        {
            if (i + 14 <= ogg.Length && ogg[i + 4] == 0)
            {
                return BinaryPrimitives.ReadInt64LittleEndian(ogg.AsSpan(i + 6));
            }
        }
        return 0;
    }

    private static SpkBankRecord Read(SpkRecord record)
    {
        byte[] payload = record.Payload;
        var read = new SpkBankRecord { Id = record.Id, Type = (SpkRecordType)(record.Core?.RawType ?? 0) };
        bool standard = record.Core is { HasStandardDeclaredSize: true, ReservedZero18: 0, ReservedZero1C: 0, ReservedTwo24: CoreTrailer }
            && BinaryPrimitives.ReadUInt32LittleEndian(payload) == CoreMagic;
        byte[] body = standard ? payload[SpkRecordCore.Size..] : [];
        read.Key = standard ? payload[KeyOffset..(KeyOffset + KeyLength)] : null;

        bool understood = standard && read.Type switch
        {
            SpkRecordType.FlatCopy => Assign(() => read.Data = body),
            SpkRecordType.SelfReferential => ReadRolloff(read, body),
            SpkRecordType.SimpleFixed68 => ReadWords(read, body, SpkLayout.EventWords),
            SpkRecordType.TransformedFixed128 => ReadWords(read, body, SpkLayout.ResourceWords),
            _ => false,
        };
        if (!understood)
        {
            return new SpkBankRecord { Id = record.Id, Type = read.Type, Raw = true, Data = payload };
        }
        return read;
    }

    private static bool Assign(Action set)
    {
        set();
        return true;
    }

    private static bool ReadRolloff(SpkBankRecord read, byte[] body)
    {
        if (body.Length < 8 || U32(body, 0) != 0 || U32(body, 4) * (long)PointSize != body.Length - 8)
        {
            return false;
        }
        read.Points = [.. Enumerable.Range(0, (int)U32(body, 4)).Select(i => Point(body, 8 + i * PointSize))];
        return true;
    }

    private static bool ReadWords(SpkBankRecord read, byte[] body, int wordCount)
    {
        if (body.Length < wordCount * 4)
        {
            return false;
        }

        read.Words = [.. Enumerable.Range(0, wordCount).Select(i => U32(body, i * 4))];
        read.Kind = read.Words[1];
        byte[] tail = body[(wordCount * 4)..];
        if (!ReadChildren(read, tail) && tail.Length > 0)
        {
            read.Tail = tail;
        }
        return true;
    }

    /// <summary>Reads a tail in the layout <see cref="Tail"/> writes; false when it is not one.</summary>
    private static bool ReadChildren(SpkBankRecord read, byte[] tail)
    {
        SpkLayout layout = read.Layout!;
        int stride = layout == SpkLayout.MultiEvent ? 4
            : layout == SpkLayout.SwitchEvent ? 12
            : layout == SpkLayout.Switch ? 8
            : layout == SpkLayout.Random ? 16
            : 0;
        if (stride != 0)
        {
            if (tail.Length % stride != 0)
            {
                return false;
            }
            read.Entries = [.. Enumerable.Range(0, tail.Length / stride).Select(i => Entry(layout, tail, i * stride))];
            return true;
        }

        if (layout != SpkLayout.Multilayer)
        {
            return false;
        }

        try
        {
            read.Layers = [.. Enumerable.Range(0, (int)read.Words[7]).Select(i => Layer(tail, i * LayerSize))];
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static SpkEntry Entry(SpkLayout layout, byte[] tail, int at) =>
        layout == SpkLayout.MultiEvent ? new SpkEntry(U32(tail, at))
        : layout == SpkLayout.SwitchEvent ? new SpkEntry(U32(tail, at), U32(tail, at + 8))
        : layout == SpkLayout.Switch ? new SpkEntry(U32(tail, at), U32(tail, at + 4))
        : new SpkEntry(U32(tail, at), U32(tail, at + 4), U32(tail, at + 8));

    private static SpkLayer Layer(byte[] tail, int at)
    {
        uint curveCount = U32(tail, at + 4);
        int curves = (int)U32(tail, at + 8);
        return new SpkLayer(U32(tail, at), [.. Enumerable.Range(0, (int)curveCount).Select(j =>
        {
            int curve = curves + j * CurveSize;
            int points = (int)U32(tail, curve + 12);
            return new SpkCurve(U32(tail, curve), U32(tail, curve + 4),
                [.. Enumerable.Range(0, (int)U32(tail, curve + 8)).Select(k => Point(tail, points + k * PointSize))]);
        })]);
    }

    /// <summary>Keeps what the derivation does not reproduce: a mismatched tail raw, mismatched words pinned.</summary>
    private void PinStored(SpkBankRecord record, byte[] payload)
    {
        if (record.Layout is null)
        {
            return;
        }

        byte[] body = payload[SpkRecordCore.Size..];
        int wordBytes = record.Words.Length * 4;
        if (record.Tail is null && !Tail(record).AsSpan().SequenceEqual(body.AsSpan(wordBytes)))
        {
            record.Tail = body[wordBytes..];
            record.Entries = [];
            record.Layers = [];
        }

        uint[] derived = DerivedWords(record);
        for (int i = 0; i < derived.Length; i++)
        {
            if (derived[i] != record.Words[i])
            {
                record.Pins[i] = record.Words[i];
            }
        }
    }

    private byte[] Payload(SpkBankRecord record)
    {
        if (record.Raw)
        {
            return record.Data;
        }

        var payload = new List<byte>();
        Append(payload, CoreMagic);
        Append(payload, (uint)SpkRecordCore.Size);
        payload.AddRange(record.Key ?? DerivedKey(record.Id));
        Append(payload, 0);
        Append(payload, 0);
        Append(payload, (uint)record.Type);
        Append(payload, CoreTrailer);

        if (record.IsAudio)
        {
            payload.AddRange(record.Data);
        }
        else if (record.IsRolloff)
        {
            Append(payload, 0);
            Append(payload, (uint)record.Points.Count);
            record.Points.ForEach(p => AppendPoint(payload, p));
        }
        else
        {
            Array.ForEach(DerivedWords(record), w => Append(payload, w));
            payload.AddRange(Tail(record));
        }
        return [.. payload];
    }

    private static byte[] Tail(SpkBankRecord record)
    {
        if (record.Tail is { } stored)
        {
            return stored;
        }

        var tail = new List<byte>();
        SpkLayout layout = record.Layout!;
        foreach (SpkEntry entry in record.Entries)
        {
            Append(tail, entry.Ref);
            if (layout == SpkLayout.SwitchEvent)
            {
                Append(tail, 0);
                Append(tail, entry.Value);
            }
            else if (layout == SpkLayout.Switch)
            {
                Append(tail, entry.Value);
            }
            else if (layout == SpkLayout.Random)
            {
                Append(tail, entry.Value);
                Append(tail, entry.Extra);
                Append(tail, 0);
            }
        }

        // Layers, then every curve in layer order, then every point; offsets count from the tail's start.
        int curveStart = record.Layers.Count * LayerSize;
        int pointStart = curveStart + record.Layers.Sum(l => l.Curves.Count) * CurveSize;
        var curves = new List<byte>();
        var points = new List<byte>();
        foreach (SpkLayer layer in record.Layers)
        {
            Append(tail, layer.Resource);
            Append(tail, (uint)layer.Curves.Count);
            Append(tail, (uint)(curveStart + curves.Count));
            Append(tail, SpkLayout.NoId);
            Append(tail, 0);
            Append(tail, 0);
            Append(tail, 0);
            foreach (SpkCurve curve in layer.Curves)
            {
                Append(curves, curve.Target);
                Append(curves, curve.Parameter);
                Append(curves, (uint)curve.Points.Count);
                Append(curves, (uint)(pointStart + points.Count));
                Append(curves, 0);
                Append(curves, 0);
                curve.Points.ForEach(p => AppendPoint(points, p));
            }
        }
        return [.. tail, .. curves, .. points];
    }

    /// <summary>Stands in for the unidentified core bytes of a new record: stable per id, so a rebuild is identical.</summary>
    private static byte[] DerivedKey(uint id)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, id);
        return SHA256.HashData(bytes)[..KeyLength];
    }

    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));

    private static SpkPoint Point(byte[] data, int at) => new(
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at, 4)),
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 4, 4)));

    private static void Append(List<byte> output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        output.AddRange(bytes);
    }

    private static void AppendPoint(List<byte> output, SpkPoint point)
    {
        Append(output, BitConverter.SingleToUInt32Bits(point.X));
        Append(output, BitConverter.SingleToUInt32Bits(point.Y));
    }
}
