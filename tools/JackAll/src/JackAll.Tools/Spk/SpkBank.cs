using System.Buffers.Binary;
using System.Security.Cryptography;
using JackAll.Core.Format;
using JackAll.Tools.Audio;
using JackAll.Tools.Sbao;

namespace JackAll.Tools.Spk;

public readonly record struct SpkPoint(float X, float Y);

/// <summary>One child of a composite: an event of a multi-event or switch event, or a resource of a
/// switch or random container. <see cref="Value"/> is the switch value or the Q16.16 weight;
/// <see cref="Extra"/> is a random entry's flag letting it play twice in a row.</summary>
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

    /// <summary>Non-zero bytes aligning the next record, kept while the payload keeps its length; null pads with zeros.</summary>
    public byte[]? Padding { get; set; }

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

    /// <summary>The XML element this record is written as.</summary>
    public string Element => Raw ? "Record" : IsAudio ? "Audio" : IsRolloff ? "Rolloff" : Layout!.Element;

    public uint Word(int index) => index < Words.Length ? Words[index] : 0;

    /// <summary>The ids this record points at, and what each should be.</summary>
    public IEnumerable<(uint Id, SpkReference Kind)> References()
    {
        if (Layout is not { } layout)
        {
            return [];
        }

        IEnumerable<(uint, SpkReference)> fields = layout.Fields
            .Select(f => (f.Index, Refers: f.Refers ?? (layout == SpkLayout.OtherEvent ? SpkLayout.TargetOf(Kind) : null)))
            .Where(f => f.Refers is not null)
            .Select(f => (Word(f.Index), f.Refers!.Value));
        IEnumerable<(uint, SpkReference)> children = layout.Children is { } shape
            ? Entries.Select(e => (e.Ref, shape.Refers))
            : Layers.Select(l => (l.Resource, SpkReference.Resource));
        return fields.Concat(children).Where(l => l.Item1 is not (0 or SpkLayout.NoId));
    }
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

    /// <summary>The samples that play an audio record.</summary>
    public IEnumerable<SpkBankRecord> SamplesPlaying(uint audioId) =>
        Records.Where(r => r.Layout == SpkLayout.Sample && r.Word(SpkLayout.SampleAudio) == audioId);

    /// <summary>Swaps an audio record's stream; the samples playing it re-derive their audio words from it.</summary>
    public void ReplaceAudio(SpkBankRecord audio, byte[] stream, int? sampleRate)
    {
        audio.Data = stream;
        audio.SampleRate = sampleRate;
        foreach (SpkBankRecord sample in SamplesPlaying(audio.Id))
        {
            foreach (int index in SpkLayout.Sample.Derived)
            {
                sample.Pins.Remove(index);
            }
        }
    }

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
            if (record.Padding.Any(b => b != 0))
            {
                read.Padding = record.Padding;
            }
            bank.Records.Add(read);
        }

        // An IMA-ADPCM stream's rate lives only in the samples that play it.
        foreach (SpkBankRecord audio in bank.Records.Where(r => DescribeAudio(r) is { Ogg: false }))
        {
            audio.SampleRate = bank.SamplesPlaying(audio.Id).Select(s => (int?)s.Word(SpkLayout.SampleRate)).FirstOrDefault();
        }

        for (int i = 0; i < bank.Records.Count; i++)
        {
            bank.PinStored(bank.Records[i], package.Records[i].Payload);
        }
        return bank;
    }

    public byte[] Write()
    {
        var output = new ByteWriter();
        output.WriteU32(SpkPackage.Magic);
        output.WriteU32((uint)Records.Count);
        Records.ForEach(r => output.WriteU32(r.Id));
        foreach (SpkBankRecord record in Records)
        {
            uint[] preamble = record.Preamble ?? Preamble;
            output.WriteU32((uint)preamble.Length);
            output.WriteU32Array(preamble);
            byte[] payload = Payload(record);
            output.WriteU32((uint)payload.Length);
            output.WriteRaw(payload);
            if (record.Padding is { } padding && (output.Length + padding.Length) % 4 == 0)
            {
                output.WriteRaw(padding);
            }
            else
            {
                output.Align(4, AlignFill.Zero);
            }
        }
        return output.ToArray();
    }

    /// <summary>The words <see cref="Write"/> stores for this record: its own, then the derived ones, then its pins.</summary>
    public uint[] DerivedWords(SpkBankRecord record)
    {
        SpkLayout layout = record.Layout ?? throw new InvalidOperationException("Only events and resources have words.");
        uint[] words = new uint[layout.WordCount];
        record.Words.AsSpan(0, Math.Min(record.Words.Length, words.Length)).CopyTo(words);
        foreach (int index in layout.Derived)
        {
            words[index] = 0;
        }

        words[0] = record.Id;
        words[1] = record.Kind;
        if (layout.ChildCount is { } count)
        {
            words[count] = (uint)(record.Entries.Count + record.Layers.Count);
        }
        if (layout == SpkLayout.Sample)
        {
            DeriveSample(words, Find(words[SpkLayout.SampleAudio]));
        }

        foreach ((int index, uint value) in record.Pins)
        {
            words[index] = value;
        }
        return words;
    }

    /// <summary>What a sample's audio record holds, or null when it is not an audio record.</summary>
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
        int channels = ImaAdpcm.Channels(data);
        long frames = Math.Max(0, (data.Length - ImaAdpcm.HeaderSize) * 2L / channels - ImaFrameShortfall);
        return (false, channels, audio.SampleRate ?? 0, frames);
    }

    private static void DeriveSample(uint[] words, SpkBankRecord? audio)
    {
        if (DescribeAudio(audio) is not { } info)
        {
            return;
        }

        uint length = (uint)audio!.Data.Length;
        uint frames = (uint)info.Frames;
        bool loop = words[SpkLayout.SampleLoop] == 1;
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
        var type = (SpkRecordType)(record.Core?.RawType ?? 0);
        var raw = new SpkBankRecord { Id = record.Id, Type = type, Raw = true, Data = payload };
        if (record.Core is not { HasStandardDeclaredSize: true, ReservedZero18: 0, ReservedZero1C: 0, ReservedTwo24: CoreTrailer }
            || ByteCursor.U32(payload, 0) != CoreMagic)
        {
            return raw;
        }

        var read = new SpkBankRecord { Id = record.Id, Type = type, Key = payload[KeyOffset..(KeyOffset + KeyLength)] };
        byte[] body = payload[SpkRecordCore.Size..];
        bool understood = type switch
        {
            SpkRecordType.FlatCopy => ReadAudio(read, body),
            SpkRecordType.SelfReferential => ReadRolloff(read, body),
            SpkRecordType.SimpleFixed68 => ReadWords(read, body, SpkLayout.EventWords),
            SpkRecordType.TransformedFixed128 => ReadWords(read, body, SpkLayout.ResourceWords),
            _ => false,
        };
        return understood ? read : raw;
    }

    private static bool ReadAudio(SpkBankRecord read, byte[] body)
    {
        read.Data = body;
        return true;
    }

    private static bool ReadRolloff(SpkBankRecord read, byte[] body)
    {
        if (body.Length < 8 || ByteCursor.U32(body, 0) != 0 || ByteCursor.U32(body, 4) * (long)PointSize != body.Length - 8)
        {
            return false;
        }
        read.Points = [.. Enumerable.Range(0, (int)ByteCursor.U32(body, 4)).Select(i => Point(body, 8 + i * PointSize))];
        return true;
    }

    private static bool ReadWords(SpkBankRecord read, byte[] body, int wordCount)
    {
        if (body.Length < wordCount * 4)
        {
            return false;
        }

        read.Words = [.. Enumerable.Range(0, wordCount).Select(i => ByteCursor.U32(body, i * 4))];
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
        if (layout.Children is { } shape)
        {
            if (tail.Length % shape.Stride != 0)
            {
                return false;
            }
            read.Entries = [.. Enumerable.Range(0, tail.Length / shape.Stride).Select(i => i * shape.Stride).Select(at =>
                new SpkEntry(ByteCursor.U32(tail, at), Column(tail, at, shape.ValueAt), Column(tail, at, shape.ExtraAt)))];
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

    private static uint Column(byte[] tail, int entry, int at) => at < 0 ? 0 : ByteCursor.U32(tail, entry + at);

    private static SpkLayer Layer(byte[] tail, int at)
    {
        uint curveCount = ByteCursor.U32(tail, at + 4);
        int curves = (int)ByteCursor.U32(tail, at + 8);
        return new SpkLayer(ByteCursor.U32(tail, at), [.. Enumerable.Range(0, (int)curveCount).Select(j =>
        {
            int curve = curves + j * CurveSize;
            int points = (int)ByteCursor.U32(tail, curve + 12);
            return new SpkCurve(ByteCursor.U32(tail, curve), ByteCursor.U32(tail, curve + 4),
                [.. Enumerable.Range(0, (int)ByteCursor.U32(tail, curve + 8)).Select(k => Point(tail, points + k * PointSize))]);
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

        var payload = new ByteWriter();
        payload.WriteU32(CoreMagic);
        payload.WriteU32(SpkRecordCore.Size);
        payload.WriteRaw(record.Key ?? DerivedKey(record.Id));
        payload.WriteU32(0);
        payload.WriteU32(0);
        payload.WriteU32((uint)record.Type);
        payload.WriteU32(CoreTrailer);

        if (record.IsAudio)
        {
            payload.WriteRaw(record.Data);
        }
        else if (record.IsRolloff)
        {
            payload.WriteU32(0);
            payload.WriteU32((uint)record.Points.Count);
            record.Points.ForEach(p => WritePoint(payload, p));
        }
        else
        {
            payload.WriteU32Array(DerivedWords(record));
            payload.WriteRaw(Tail(record));
        }
        return payload.ToArray();
    }

    private static byte[] Tail(SpkBankRecord record)
    {
        if (record.Tail is { } stored)
        {
            return stored;
        }

        var tail = new ByteWriter();
        if (record.Layout!.Children is { } shape)
        {
            foreach (SpkEntry entry in record.Entries)
            {
                byte[] row = new byte[shape.Stride];
                BinaryPrimitives.WriteUInt32LittleEndian(row, entry.Ref);
                if (shape.ValueAt >= 0)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(shape.ValueAt), entry.Value);
                }
                if (shape.ExtraAt >= 0)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(shape.ExtraAt), entry.Extra);
                }
                tail.WriteRaw(row);
            }
            return tail.ToArray();
        }

        // Layers, then every curve in layer order, then every point; offsets count from the tail's start.
        int curveStart = record.Layers.Count * LayerSize;
        int pointStart = curveStart + record.Layers.Sum(l => l.Curves.Count) * CurveSize;
        var curves = new ByteWriter();
        var points = new ByteWriter();
        foreach (SpkLayer layer in record.Layers)
        {
            tail.WriteU32(layer.Resource);
            tail.WriteU32((uint)layer.Curves.Count);
            tail.WriteU32((uint)(curveStart + curves.Length));
            tail.WriteU32Array([SpkLayout.NoId, 0, 0, 0]);
            foreach (SpkCurve curve in layer.Curves)
            {
                curves.WriteU32Array([curve.Target, curve.Parameter, (uint)curve.Points.Count, (uint)(pointStart + points.Length), 0, 0]);
                curve.Points.ForEach(p => WritePoint(points, p));
            }
        }
        tail.WriteRaw(curves.ToArray());
        tail.WriteRaw(points.ToArray());
        return tail.ToArray();
    }

    /// <summary>Stands in for the unidentified core bytes of a new record: stable per id, so a rebuild is identical.</summary>
    private static byte[] DerivedKey(uint id)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, id);
        return SHA256.HashData(bytes)[..KeyLength];
    }

    private static SpkPoint Point(byte[] data, int at) => new(ByteCursor.F32(data, at), ByteCursor.F32(data, at + 4));

    private static void WritePoint(ByteWriter output, SpkPoint point)
    {
        output.WriteF32(point.X);
        output.WriteF32(point.Y);
    }
}
