using System.Buffers.Binary;
using JackAll.Core.Format;
using JackAll.Tools.Audio;
using JackAll.Tools.Sbao;

namespace JackAll.Tools.Spk;

/// <summary>The seven record-type constants read from a record's core (offset 0x20). Only
/// <see cref="SimpleFixed68"/>, <see cref="TransformedFixed128"/>, <see cref="FlatCopy"/>, and
/// <see cref="SelfReferential"/> have ever been observed in a real shipped `.spk`; the other three
/// are confirmed structurally (decompiled handlers) but don't appear anywhere in a real install, and
/// <see cref="Streamed"/> is rejected outright when loading bank/atomic data - it only ever exists as
/// a standalone `.sbao`/`.bao` file (see <see cref="SpkPackage"/>'s remarks).</summary>
public enum SpkRecordType : uint
{
    SimpleFixed68 = 0x10000000,
    TransformedFixed128 = 0x20000000,
    FlatCopy = 0x30000000,
    LargeFixed256 = 0x40000000,
    Streamed = 0x50000000,
    CountPrefixedList = 0x60000000,
    SelfReferential = 0x70000000,
}

/// <summary>The 40-byte core every record's payload begins with, regardless of type. Four fields
/// (`Unknown08`/`Unknown0C`/`Unknown10`/`Unknown14`) remain unidentified - confirmed not a CRC32 or
/// Adler32 checksum of the rest of the payload, otherwise unknown. The three reserved fields are
/// exposed even though their value never varies in any real record, so a hand-crafted or corrupt file
/// with something unexpected there is visible rather than silently ignored.</summary>
public sealed class SpkRecordCore
{
    public required uint DeclaredSize { get; init; }
    public required uint RawType { get; init; }
    public required uint Unknown08 { get; init; }
    public required uint Unknown0C { get; init; }
    public required uint Unknown10 { get; init; }
    public required uint Unknown14 { get; init; }
    public required uint ReservedZero18 { get; init; }
    public required uint ReservedZero1C { get; init; }
    public required uint ReservedTwo24 { get; init; }

    /// <summary>Size of the core itself, in bytes.</summary>
    public const int Size = 0x28;

    /// <summary>The record's own self-declared struct size - always exactly <see cref="Size"/> (40) in
    /// every real record; a hardcoded companion constant rather than an actual variable length.</summary>
    public bool HasStandardDeclaredSize => DeclaredSize == Size;

    public SpkRecordType? Type => Enum.IsDefined(typeof(SpkRecordType), RawType) ? (SpkRecordType)RawType : null;
}

/// <summary>The <see cref="SimpleFixed68SubHeader.EventType"/> values the engine's own post-load fixup
/// (`FUN_10a3ebd0` in `Dunia.dll`) switches on. Anything outside this set is rejected there with
/// "ERROR: Cannot init binary event, unknown event type." - which is where the name comes from: a
/// `SimpleFixed68` record is a *binary event object*, not a sound. Only the two composite kinds carry
/// anything after the 68-byte sub-header; see <see cref="SimpleFixed68SubHeader.ChildIds"/>.</summary>
public enum SpkEventType : uint
{
    /// <summary>Plays the sound resource in word[2]; word[7] is resolved too. 91% of real records.</summary>
    Leaf = 1,

    /// <summary>Resolves word[2] but declines to play - the play dispatcher no-ops.</summary>
    NoOpLinked = 2,

    /// <summary>Resolves nothing and no-ops.</summary>
    NoOp3 = 3,

    /// <summary>Plays, after an extra dispatcher step; resolves word[2] and word[3].</summary>
    LeafExtraStep = 4,

    /// <summary>Plays; resolves word[2] and word[6]. Absent from the corpus checked.</summary>
    Leaf5 = 5,

    /// <inheritdoc cref="Leaf5"/>
    Leaf6 = 6,

    /// <inheritdoc cref="Leaf5"/>
    Leaf7 = 7,

    /// <summary>Resolves nothing and no-ops.</summary>
    NoOp8 = 8,

    /// <inheritdoc cref="Leaf5"/>
    Leaf9 = 9,

    /// <summary>Resolves nothing and no-ops.</summary>
    NoOp10 = 10,

    /// <summary>A keyed table: `[6]` entries of three words at tail byte offset `[5]`, each
    /// `{id, resolvedPointer, key}`. The dispatcher walks every entry, recursing.</summary>
    Switch = 11,

    /// <summary>A plain list: `[3]` ids at tail byte offset `[2]`. The dispatcher walks every entry
    /// and recurses into each - it fires *all* of them, so this is a layered composite rather than a
    /// random-variation picker.</summary>
    List = 12,
}

/// <summary><see cref="SpkRecordType.SimpleFixed68"/>'s 68-byte sub-header - a *binary event object*
/// rather than a sound. <see cref="EventType"/> (word[1]) selects which of the remaining words mean
/// anything: the leaf kinds use word[2] as a link to the sound resource they play, while the two
/// composite kinds reinterpret it as a byte offset into a trailing child list. See
/// <see cref="SpkEventType"/> and <see cref="SpkPackage"/>'s remarks for the confidence behind each
/// field. Every other word was `0` in every real record checked.</summary>
public sealed class SimpleFixed68SubHeader
{
    public const int Size = 68;

    /// <summary>word[0] (+0x00) - echoes the record's own id.</summary>
    public required uint OwnId { get; init; }

    /// <summary>word[1] (+0x04) - which kind of event this record is; see <see cref="SpkEventType"/>.
    /// Named a "variant/voice count" in earlier revisions of this file on the strength of `1` being
    /// 91% of records - the decompiled fixup shows it is a type tag, and the two values that never fit
    /// that reading (`11`/`12`) are exactly the two that carry a child list.</summary>
    public required uint EventType { get; init; }

    /// <summary>Whether this event dispatches to a list of other events rather than playing a sound
    /// itself - true for <see cref="SpkEventType.List"/> and <see cref="SpkEventType.Switch"/>, the
    /// only two kinds with a <see cref="ChildIds"/> list and the only two where
    /// <see cref="LinkedId"/> is meaningless.</summary>
    public bool IsComposite => KnownEventType is SpkEventType.List or SpkEventType.Switch;

    /// <summary><see cref="EventType"/> as a named value, or null if it is not one the engine
    /// handles (which the engine itself would reject at load).</summary>
    public SpkEventType? KnownEventType =>
        Enum.IsDefined(typeof(SpkEventType), EventType) ? (SpkEventType)EventType : null;

    /// <summary>word[2] (+0x08) - the sound resource this event plays, for the leaf event kinds; the
    /// engine's fixup rewrites it into a live pointer. **Null for a composite event**, where the same
    /// word is a byte offset into the child list instead of an id - reading it as a link there is what
    /// makes a list event look like a record pointing at `0x00000000`.</summary>
    public uint? LinkedId => IsComposite ? null : RawWord2;

    /// <summary>word[2] (+0x08) verbatim, whatever it means for this <see cref="EventType"/> - a link
    /// for a leaf, a byte offset into the tail for a composite (`0` in every real record).</summary>
    public required uint RawWord2 { get; init; }

    /// <summary>The ids this event dispatches to, read from the bytes after the 68-byte sub-header -
    /// `[3]` of them for a <see cref="SpkEventType.List"/>, `[6]` for a
    /// <see cref="SpkEventType.Switch"/> (whose entries are three words wide, of which this is the
    /// first). Empty for every leaf event. These are ids of *other banks*, which have to be resident
    /// already - the fixup only looks them up in the atomic-object registry, so a bank not pulled in by
    /// `depload` resolves to null. See the `.spk` docs page.</summary>
    public required IReadOnlyList<uint> ChildIds { get; init; }

    /// <summary>The third column of a <see cref="SpkEventType.Switch"/> table, positionally paired with
    /// <see cref="ChildIds"/> - a switch value selecting between them, not traced to the code that
    /// reads it. Empty for every other event kind.</summary>
    public required IReadOnlyList<uint> SwitchKeys { get; init; }

    /// <summary>word[4] (+0x10) - constant `0x00010000` = `1.0` in `Q16.16` fixed point; plausibly an
    /// identity gain/scale default.</summary>
    public required uint IdentityGainQ16_16 { get; init; }

    /// <summary>word[7] (+0x1C) - an id-reference-shaped field; a `0xFFFFFFFF` sentinel only 14% of
    /// the time, otherwise overwhelmingly one of a small handful of ids reused thousands of times each
    /// (one single id alone accounts for 29% of all real records) - reads more like a shared
    /// category/template reference than a per-record link.</summary>
    public required uint CategoryId { get; init; }

    /// <summary>word[9] (+0x24) - `0` in 90% of real records; when nonzero, always exactly `+100` or
    /// `-100` - a clean discrete signed flag, not a continuous parameter.</summary>
    public required int SignedHundredFlag { get; init; }

    /// <summary>word[16] (+0x40) - boolean; `0` in 84% of real records, `1` in 16%.</summary>
    public required uint BoolFlag { get; init; }
}

/// <summary><see cref="SpkRecordType.TransformedFixed128"/>'s 128-byte sub-header, word-indexed fields
/// named where their meaning is known or strongly inferred - see <see cref="SpkPackage"/>'s remarks
/// for the confidence behind each one.</summary>
public sealed class TransformedFixed128SubHeader
{
    public const int Size = 128;

    /// <summary>word[0] (+0x00) - echoes the record's own id.</summary>
    public required uint OwnId { get; init; }

    /// <summary>word[2] (+0x08) - the sibling <see cref="SpkRecordType.FlatCopy"/> record's audio byte
    /// length (its payload size minus the 40-byte core). Exact in all 3,211 records that pair with a
    /// sibling across the corpus checked, for both codecs. Nothing in JackAll rewrites it when audio is
    /// replaced, so a record whose audio has been swapped will disagree with its sibling - see
    /// <see cref="SpkPackage.DeclaredAudioLengthMatches"/>.</summary>
    public required uint AudioByteLength { get; init; }

    /// <summary>word[22] (+0x58) - the same value as <see cref="AudioByteLength"/>, or `0`. Never a
    /// third value: of 3,211 paired records, 2,971 mirror it exactly and the remaining 240 are `0`.</summary>
    public required uint AudioByteLengthMirror { get; init; }

    /// <summary>word[5] (+0x14) - a negative `Q16.16` fixed-point value when nonzero (e.g. `-12.0`,
    /// `-8.0`); plausibly a gain/dB adjustment applied by this type's own post-load transform.</summary>
    public required int GainQ16_16 { get; init; }

    /// <summary>word[7] (+0x1C) - an id-reference: matches the positionally-preceding record 59% of
    /// the time, some id in the same bank 72% of the time - usually the paired `FlatCopy` record.</summary>
    public required uint FlatCopySiblingId { get; init; }

    /// <summary>word[17] (+0x44) - `1` (94%) or `2` (rare, ~2%); correlates with the sibling
    /// `FlatCopy` payload's size (an ~11x larger average when `2`), consistent with a channel-count
    /// field. Confirmed against two real payloads: `1` for a mono `FlatCopy` sibling, `2` for a
    /// stereo one.</summary>
    public required uint ChannelCountGuess { get; init; }

    /// <summary>word[19] (+0x4C) - the sibling `FlatCopy` audio's sample rate. Always a standard
    /// real-world rate across a real install (32000/22050/48000/44100/24000/16000/12000/8000/6000).</summary>
    public required uint SampleRate { get; init; }

    /// <summary>word[20] (+0x50) - irregular values in the low thousands, not a rate (equals
    /// <see cref="SampleRate"/> only 0.1% of the time); reads more like a decoded sample/frame count
    /// or output buffer size than a second rate field.</summary>
    public required uint Word20 { get; init; }

    /// <summary>word[25] (+0x64) - `4` (81%) or `3` (19%) in a real install; otherwise unidentified.</summary>
    public required uint Word25 { get; init; }

    /// <summary>word[28] (+0x70) - `7` in 99.8% of real records; otherwise unidentified.</summary>
    public required uint Word28 { get; init; }

    /// <summary>word[31] (+0x7C) - `0xFFFFFFFF` in 99.9% of real records; otherwise unidentified.</summary>
    public required uint Word31 { get; init; }
}

/// <summary>One sub-object packed inside an .spk - a hash-identified, variable-length record. Every
/// record's payload begins with a common 40-byte core (<see cref="Core"/>); the two most common
/// non-`FlatCopy` types additionally have a further fixed-size sub-header, parsed into
/// <see cref="SimpleFixed68"/>/<see cref="TransformedFixed128"/> when the type and payload length
/// match. <see cref="FlatCopy"/> records have no sub-header at all - their remainder
/// (<see cref="FlatCopyAudioStream"/>) holds either of two codecs, split ~74%/26% across a real
/// install: a complete Ogg Vorbis bitstream (detected by <see cref="SbaoAudio.TryReadVorbisId"/>
/// parsing a valid Vorbis identification header directly at the start), or a raw `TImaAdpcm` stream,
/// decodable with <see cref="ImaAdpcm"/>.</summary>
public sealed class SpkRecord
{
    public required uint Id { get; init; }
    public required IReadOnlyList<uint> PreambleWords { get; init; }
    public required byte[] Payload { get; init; }
    public required SpkRecordCore? Core { get; init; }
    public SimpleFixed68SubHeader? SimpleFixed68 { get; init; }
    public TransformedFixed128SubHeader? TransformedFixed128 { get; init; }

    /// <summary>For a <see cref="SpkRecordType.FlatCopy"/> record, the raw bytes after the 40-byte
    /// core - either a complete Ogg Vorbis bitstream (check with
    /// <see cref="SbaoAudio.TryReadVorbisId"/> first) or, if not, a `TImaAdpcm` stream ready for
    /// <see cref="ImaAdpcm.Decode"/>. Null for any other record type.</summary>
    public byte[]? FlatCopyAudioStream =>
        Core?.Type == SpkRecordType.FlatCopy ? Payload[SpkRecordCore.Size..] : null;
}

/// <summary>
/// Reads the container structure of a Far Cry 2 .spk sound-bank file - the header, the record count,
/// the id table, and each record's own preamble/size/payload framing, plus every field of the 40-byte
/// record core and the two most common sub-headers that's been identified so far.
///
/// Traced live via GhidraMCP against Dunia.dll (client). Container layout, confirmed against every
/// real `.spk` file in a Steam v1.03 install (8,282 files, 42,215 records, zero parse failures):
/// <code>
/// u32 magic = 0x53504B01   ("KPS" + a version byte, reversed-FourCC like .xbg/.xbm)
/// u32 count
/// u32[count] ids           // one hash-style id per record, same table order as the records below
/// count x {
///     u32 preambleWordCount (N)
///     u32[N] preambleWords  // copied into an id-keyed cache; see remarks below
///     u32 size
///     u8[size] payload      // 4-byte aligned before the next record
/// }
/// </code>
///
/// A record's payload always starts with a 40-byte core (magic `02 1F 00 10`, a declared-size field
/// that's always exactly 40, four still-unidentified fields, two always-zero fields, a type tag, and
/// a field that's always `2`) - see <see cref="SpkRecordCore"/>. The type tag selects one of seven
/// handlers (see <see cref="SpkRecordType"/>); <see cref="SpkRecordType.FlatCopy"/> is the one with no
/// further sub-header - its remainder is the actual compressed audio, split ~74%/26% across a real
/// install between a complete Ogg Vorbis bitstream and a raw `TImaAdpcm` (IMA-ADPCM) stream decodable
/// with <see cref="ImaAdpcm"/> (see <see cref="SpkRecord.FlatCopyAudioStream"/>'s remarks). The other
/// two common types
/// (<see cref="SpkRecordType.SimpleFixed68"/>/<see cref="SpkRecordType.TransformedFixed128"/>) have
/// their own fixed-size sub-headers, parsed into <see cref="SimpleFixed68SubHeader"/>/
/// <see cref="TransformedFixed128SubHeader"/>.
///
/// Preamble words are copied verbatim into a small cache keyed by the record's own id; the cached
/// copy's pointer becomes the record's `extra` field at load time, later read back by the engine and
/// threaded into generic runtime playback dispatch. Their content is the bank's own id plus every
/// parent that pulls it in - the same parent/child edge `depload` records from the other side, verified
/// against a world's `depload` on a full chain. Self is not at a fixed position; treat it as a set.
///
/// `SpkRecordType.Streamed` is never actually stored inside a `.spk` bank (rejected outright by the
/// engine) - streamed sounds exist exclusively as standalone `<id>.sbao`/`<id>.bao` files instead. Real
/// `.spk` record ids and real `.sbao` file ids overlap at only ~0.01% (noise level) across a whole
/// install - the two are mutually exclusive storage paths for the same id-space.
/// </summary>
public sealed class SpkPackage
{
    public const uint Magic = 0x53504B01;

    public required IReadOnlyList<SpkRecord> Records { get; init; }

    public static SpkPackage Parse(byte[] data)
    {
        if (data.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0)) != Magic)
        {
            throw new InvalidDataException(
                "Not a Far Cry 2 .spk (no 0x53504B01 header) - this viewer doesn't support this file's format.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        if (count > 1_000_000)
        {
            throw new InvalidDataException($"Implausible record count {count}.");
        }

        int idTableStart = 8;
        int recordsStart = idTableStart + checked((int)count * 4);
        if (recordsStart > data.Length)
        {
            throw new InvalidDataException("Truncated id table.");
        }

        var ids = new uint[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(idTableStart + i * 4));
        }

        var records = new List<SpkRecord>((int)count);
        var cursor = new ByteCursor(data);
        cursor.Position = recordsStart;
        for (int i = 0; i < count; i++)
        {
            uint preambleWordCount = cursor.ReadU32();
            if (preambleWordCount > 4096)
            {
                throw new InvalidDataException($"Implausible preamble word count {preambleWordCount} in record {i}.");
            }

            var preamble = new uint[preambleWordCount];
            for (int w = 0; w < preambleWordCount; w++)
            {
                preamble[w] = cursor.ReadU32();
            }

            uint size = cursor.ReadU32();
            if (size > int.MaxValue || cursor.Position + (long)size > data.Length)
            {
                throw new InvalidDataException(
                    $"Truncated payload in record {i} (wanted {size} bytes at 0x{cursor.Position:X}).");
            }

            byte[] payload = cursor.ReadBytes((int)size);
            // next record is 4-byte aligned
            cursor.Position += PadLength(cursor.Position);

            SpkRecordCore? core = ParseCore(payload);
            records.Add(new SpkRecord
            {
                Id = ids[i],
                PreambleWords = preamble,
                Payload = payload,
                Core = core,
                SimpleFixed68 = core?.Type == SpkRecordType.SimpleFixed68 ? ParseSimpleFixed68(payload) : null,
                TransformedFixed128 = core?.Type == SpkRecordType.TransformedFixed128
                    ? ParseTransformedFixed128(payload)
                    : null,
            });
        }

        return new SpkPackage { Records = records };
    }

    /// <summary>The sample rate for a <see cref="SpkRecordType.FlatCopy"/> record's audio, read from
    /// the sibling <see cref="TransformedFixed128SubHeader"/> record whose
    /// <see cref="TransformedFixed128SubHeader.FlatCopySiblingId"/> points back at it - or null if no
    /// such sibling exists in this bank.</summary>
    public int? TryGetFlatCopySampleRate(SpkRecord flatCopyRecord) =>
        TryGetAudioDescriptor(flatCopyRecord) is { } t128 ? (int)t128.SampleRate : null;

    /// <summary>The <see cref="TransformedFixed128SubHeader"/> describing a given
    /// <see cref="SpkRecordType.FlatCopy"/> record - the sibling whose
    /// <see cref="TransformedFixed128SubHeader.FlatCopySiblingId"/> points back at it, or null if this
    /// bank holds no such record.</summary>
    public TransformedFixed128SubHeader? TryGetAudioDescriptor(SpkRecord flatCopyRecord)
    {
        foreach (SpkRecord r in Records)
        {
            if (r.TransformedFixed128?.FlatCopySiblingId == flatCopyRecord.Id)
            {
                return r.TransformedFixed128;
            }
        }

        return null;
    }

    /// <summary>Whether a <see cref="SpkRecordType.FlatCopy"/> record's actual audio length agrees with
    /// the length its descriptor sibling declares in
    /// <see cref="TransformedFixed128SubHeader.AudioByteLength"/>. True in every shipped record; false
    /// means the audio has been replaced by a tool that didn't rewrite the descriptor (JackAll's own
    /// importers among them). Null when there is no descriptor sibling to compare against.
    ///
    /// Whether the engine actually reads this as a playback-length gate is untested - it is the best
    /// remaining candidate for the trailing-noise symptom documented on the `.spk` docs page, but the
    /// field that was tried and ruled out was word[20], not this one. Surfaced so a mismatch is visible
    /// rather than silently shipped.</summary>
    public bool? DeclaredAudioLengthMatches(SpkRecord flatCopyRecord) =>
        flatCopyRecord.FlatCopyAudioStream is { } audio && TryGetAudioDescriptor(flatCopyRecord) is { } t128
            ? t128.AudioByteLength == (uint)audio.Length
            : null;

    /// <summary>
    /// Rebuilds this .spk file's raw bytes with one record's payload replaced by
    /// <paramref name="newPayload"/> - everything else (ids, preamble words, every other record, and
    /// this record's own placement relative to them) is copied byte-for-byte from
    /// <paramref name="originalFile"/> unchanged; only the target record's `size` field, payload
    /// bytes, and trailing 4-byte alignment padding are rewritten.
    ///
    /// Deliberately narrow rather than a full "build a package from a list of records" API: plenty of
    /// this format is still only partially understood (the four unidentified core fields, most of each
    /// sub-header, whether the preamble/id cross-references are load-bearing at runtime) - copying
    /// everything but the one thing being replaced forward untouched is the safest option, since it
    /// never requires reconstructing anything we can't already read byte-for-byte off a real file.
    /// </summary>
    public static byte[] ReplaceRecordPayload(byte[] originalFile, uint recordId, byte[] newPayload)
    {
        if (originalFile.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(originalFile.AsSpan(0)) != Magic)
        {
            throw new InvalidDataException("Not a Far Cry 2 .spk (no 0x53504B01 header).");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(originalFile.AsSpan(4));
        var cursor = new ByteCursor(originalFile);
        cursor.Position = 8 + checked((int)count * 4);

        for (int i = 0; i < count; i++)
        {
            uint id = ByteCursor.U32(originalFile, 8 + i * 4);

            uint preambleWordCount = cursor.ReadU32();
            cursor.Position += checked((int)preambleWordCount) * 4;

            int sizeFieldPos = cursor.Position;
            uint size = cursor.ReadU32();
            cursor.Position += (int)size;
            // next record (or end of file) is 4-byte aligned
            cursor.Position += PadLength(cursor.Position);

            if (id != recordId)
            {
                continue;
            }

            // the record's old size field + payload + padding, replaced as one chunk
            int oldChunkLength = cursor.Position - sizeFieldPos;
            int newChunkLength = 4 + newPayload.Length + PadLength(newPayload.Length);
            var result = new byte[originalFile.Length - oldChunkLength + newChunkLength];

            int w = 0;
            Array.Copy(originalFile, 0, result, w, sizeFieldPos);
            w += sizeFieldPos;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(w), (uint)newPayload.Length);
            w += 4;
            Array.Copy(newPayload, 0, result, w, newPayload.Length);
            // padding bytes stay zero (fresh array)
            w += newPayload.Length + PadLength(newPayload.Length);
            Array.Copy(originalFile, cursor.Position, result, w, originalFile.Length - cursor.Position);

            return result;
        }

        throw new InvalidDataException($"No record with id 0x{recordId:x8} in this .spk.");
    }

    private static int PadLength(int length) => (4 - length % 4) % 4;

    private static SpkRecordCore? ParseCore(byte[] payload)
    {
        if (payload.Length < SpkRecordCore.Size)
        {
            return null;
        }

        return new SpkRecordCore
        {
            DeclaredSize = ByteCursor.U32(payload, 0x04),
            Unknown08 = ByteCursor.U32(payload, 0x08),
            Unknown0C = ByteCursor.U32(payload, 0x0C),
            Unknown10 = ByteCursor.U32(payload, 0x10),
            Unknown14 = ByteCursor.U32(payload, 0x14),
            ReservedZero18 = ByteCursor.U32(payload, 0x18),
            ReservedZero1C = ByteCursor.U32(payload, 0x1C),
            RawType = ByteCursor.U32(payload, 0x20),
            ReservedTwo24 = ByteCursor.U32(payload, 0x24),
        };
    }

    private static SimpleFixed68SubHeader? ParseSimpleFixed68(byte[] payload)
    {
        var sub = payload.AsSpan(SpkRecordCore.Size);
        if (sub.Length < SimpleFixed68SubHeader.Size)
        {
            return null;
        }

        uint eventType = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x04..]);
        var tail = sub[SimpleFixed68SubHeader.Size..];

        // The two composite kinds store their child list in the tail, addressed by a byte offset and a
        // count that live in different words per kind (word[2]/word[3] for a list, word[5]/word[6] for
        // a switch, whose entries are three words wide). Both are bounds-checked against the tail we
        // actually have rather than trusted: a truncated or hand-edited record yields no children
        // instead of an overread.
        (uint offset, uint count, int stride) = eventType switch
        {
            (uint)SpkEventType.List => (BinaryPrimitives.ReadUInt32LittleEndian(sub[0x08..]),
                                        BinaryPrimitives.ReadUInt32LittleEndian(sub[0x0C..]), 1),
            (uint)SpkEventType.Switch => (BinaryPrimitives.ReadUInt32LittleEndian(sub[0x14..]),
                                          BinaryPrimitives.ReadUInt32LittleEndian(sub[0x18..]), 3),
            _ => (0u, 0u, 0),
        };

        uint[] childIds = [];
        uint[] switchKeys = [];
        if (stride != 0 && count <= int.MaxValue / (stride * 4) && offset <= (uint)tail.Length
            && (int)count * stride * 4 <= tail.Length - (int)offset)
        {
            var entries = tail[(int)offset..];
            childIds = new uint[count];
            switchKeys = stride == 3 ? new uint[count] : [];
            for (int i = 0; i < count; i++)
            {
                childIds[i] = BinaryPrimitives.ReadUInt32LittleEndian(entries[(i * stride * 4)..]);
                if (stride == 3)
                {
                    switchKeys[i] = BinaryPrimitives.ReadUInt32LittleEndian(entries[(i * stride * 4 + 8)..]);
                }
            }
        }

        return new SimpleFixed68SubHeader
        {
            OwnId = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x00..]),
            EventType = eventType,
            RawWord2 = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x08..]),
            ChildIds = childIds,
            SwitchKeys = switchKeys,
            IdentityGainQ16_16 = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x10..]),
            CategoryId = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x1C..]),
            SignedHundredFlag = BinaryPrimitives.ReadInt32LittleEndian(sub[0x24..]),
            BoolFlag = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x40..]),
        };
    }

    private static TransformedFixed128SubHeader? ParseTransformedFixed128(byte[] payload)
    {
        var sub = payload.AsSpan(SpkRecordCore.Size);
        if (sub.Length < TransformedFixed128SubHeader.Size)
        {
            return null;
        }

        return new TransformedFixed128SubHeader
        {
            OwnId = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x00..]),
            AudioByteLength = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x08..]),
            AudioByteLengthMirror = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x58..]),
            GainQ16_16 = BinaryPrimitives.ReadInt32LittleEndian(sub[0x14..]),
            FlatCopySiblingId = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x1C..]),
            ChannelCountGuess = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x44..]),
            SampleRate = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x4C..]),
            Word20 = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x50..]),
            Word25 = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x64..]),
            Word28 = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x70..]),
            Word31 = BinaryPrimitives.ReadUInt32LittleEndian(sub[0x7C..]),
        };
    }

}
