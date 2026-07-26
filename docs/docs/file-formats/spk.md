---
sidebar_position: 4
---

# `.spk` — Sound Bank Format

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against **`Dunia.dll`** (the Windows client engine — the same file format's
code is present but stubbed out in the Linux dedicated-server binary, which never plays audio).
Confirmed against **every real `.spk` file in a Steam v1.03 install** (8,282 files, 42,215 records,
zero parse failures) via a standalone parser
(`tools/JackAll/src/JackAll.Core/Format/SpkPackage.cs`) and cross-checked against a hand-written
Python decoder run against real extracted payloads. Companion page: [`.sbao`](./sbao.md), the
standalone (non-bank) sibling format sharing the same DARE "atomic object" data.
:::

`.spk` files are hash-named sound banks (`soundbinary\<id:08x>.spk`) holding multiple small
DARE ("Ubisoft's proprietary audio middleware, config in `Data_Win32/SoundBinary/DARE.INI`) resource
records, each identified by its own id.

:::note["Spk"/"SPK" means three different things in `Dunia.dll`]
A string search for `spk`/`SPK` in the binary also turns up two unrelated subsystems that happen to
reuse the abbreviation — worth knowing so they don't get mistaken for this format:
- `scripts\game\BarkData\<N>.spk` (decimal-numbered) — the AI dialogue/bark script system.
- `fNearLimitSpkDist` / `fFarLimitSpkDist` — tuning properties on an in-editor `"SpeakerSet"` object
  (a placeable sound-emitter entity); "Spk" here is short for **"Speaker."**

Neither is the sound-bank container documented below.
:::

## Container format

All fields little-endian, 4-byte aligned throughout.

```
Header:
  u32   magic  = 0x53504B01     ("KPS" + a version byte, reversed-FourCC — same convention as
                                  .xbg/.xbm's "HSEM"/"MESH", see the XBM/XBG format page)
  u32   count
  u32[count] ids                // one id per record, same order as the records below

Then `count` variable-length records, back-to-back:
  u32   preambleWordCount (N)
  u32[N] preambleWords          // see "Preamble words" below
  u32   size
  u8[size] payload              // see "Record core" below
```

Validation performed by the engine's own parser: rejects if the buffer is under `0x10` bytes, if the
magic doesn't match exactly, if `count == 0`, or if the buffer is too small to hold the id table
(`size <= count*4 + 0xC`). The per-record loop reads each record's own `size` field and re-validates
bounds every iteration, so a truncated/corrupt trailing record is caught rather than walked off the
end of the buffer.

## Loading pipeline

1. A sound id is turned into a filename: `"<bank_dir><id:08x>.spk"`, or, when bit `0x40000000` of the
   id is set, a localized variant `"<bank_dir><lang>\<id:08x>.spk"`.
2. The file is opened through **`VFS_ResolvePath`** — the same hooked resolver documented on
   [the archives page](./archives-fat-dat.md), not a bypass path. This means the loose-file
   mod-loader override applies to `.spk` banks exactly the same way it does to `.fat`/`.dat` archive
   contents.
3. The whole file is read into a buffer and handed to the container parser via a virtual call.
4. Standalone `.sbao`/`.bao` files (see below) are opened through the identical `VFS_ResolvePath`
   call — there's no separate, unhooked file path for that case either.

## Record core (40 bytes)

Every record's payload begins with a common 40-byte core, present regardless of record type:

| Offset | Size | Field | Value |
|---|---|---|---|
| `0x00` | 4 | magic | `02 1F 00 10` (constant) |
| `0x04` | 4 | declared size | `40` (`0x28`) — **always exactly this**, in literally every one of 42,215 real records; a hardcoded companion constant, not a field that actually varies |
| `0x08` | 4 | unidentified | differs per record (high-entropy) |
| `0x0C` | 4 | unidentified | differs per record (high-entropy) |
| `0x10` | 4 | unidentified | differs per record (high-entropy) |
| `0x14` | 4 | unidentified | differs per record (high-entropy) |
| `0x18` | 4 | unidentified | `0` in every record checked |
| `0x1C` | 4 | unidentified | `0` in every record checked |
| `0x20` | 4 | type tag | one of the 7 constants below |
| `0x24` | 4 | unidentified | `0x2` in every record checked |

The four still-unidentified fields (`0x08`-`0x14`) are not a CRC32 or Adler32 checksum of the rest of
the payload (checked against all 42,215 records, zero matches against either algorithm).

Anything under 40 bytes is rejected outright ("*Invalid object size: you have probably loaded an old
version of the data*") — confirming the declared-size field really is a hardcoded structure-version
tag, not incidental.

## Record types

The type tag at core offset `0x20` selects one of seven handlers:

| Type | Name | Behavior | Share of real records |
|---|---|---|---|
| `0x10000000` | `SimpleFixed68` | Fixed 68-byte sub-header, remainder (if any) copied verbatim. | 34% |
| `0x20000000` | `TransformedFixed128` | Fixed 128-byte sub-header, then a dedicated post-load transform — the only fixed-size type that does more than copy. | 39% |
| `0x30000000` | `FlatCopy` | No sub-header — the entire remainder is copied verbatim. This is where the actual compressed audio bytes live, as either of two codecs (see "The audio codec(s)" below). | 27% |
| `0x40000000` | `LargeFixed256` | Fixed 256-byte sub-header, plain copy. | never seen in a real install |
| `0x50000000` | `Streamed` | Rejected outright when loading atomic/bank data ("*Can't load atomic object id (0x%X) because it's a streamed sound data*"). Streamed sounds are never packed into a `.spk` bank's records — they exist exclusively as standalone `<id>.sbao`/`<id>.bao` files (see below). | 0% (by definition) |
| `0x60000000` | `CountPrefixedList` | Reads a leading count from the raw data and consumes `count*4 + 4` bytes before the remainder — a count-prefixed list of references rather than a single sound. Likely a randomized-variation group. | never seen in a real install |
| `0x70000000` | `SelfReferential` | Plain copy, but the first two fields of the copy are then read as `{offset, flag}`: if `flag != 0`, `offset` is rewritten in place to an absolute pointer into the copy — an internal fixup implying a nested sub-structure. | 0.2% |

Real `.spk` banks only ever contain the three common non-streamed types
(`SimpleFixed68`/`TransformedFixed128`/`FlatCopy`) plus the rare `SelfReferential`; the other three
were never observed across all 42,215 real records. Banks tend to hold **matched sets** of all three
common types — 80% of files whose record count is a multiple of 3 contain exactly an equal count of
`FlatCopy`, `TransformedFixed128`, and `SimpleFixed68` — though they aren't always laid out as
consecutive triples in a fixed order.

## `SimpleFixed68` sub-header (68 bytes, `u32[17]`)

| Word | Offset | Meaning |
|---|---|---|
| `[0]` | `+0x00` | echoes the record's own id |
| `[1]` | `+0x04` | `1` in 98% of records; otherwise `2`/`4`/`8` — possibly a variant/voice count |
| `[2]` | `+0x08` | an id-reference: resolves to *some* real id in the corpus 99.9% of the time, to a record in the *same file* 79% of the time (not reliably the positionally-adjacent record) |
| `[4]` | `+0x10` | constant `0x00010000` = `1.0` in `Q16.16` fixed point — plausibly an identity gain/scale default |
| `[7]` | `+0x1C` | another id-reference-shaped field; a `0xFFFFFFFF` sentinel only 14% of the time — the rest overwhelmingly cluster on a small handful of ids reused thousands of times each (one single id alone accounts for 29% of all records), reading more like a shared category/template reference than a per-record link |
| `[9]` | `+0x24` | `0` in 90% of records; when nonzero, *always* exactly `+100` or `-100` — a clean discrete signed flag, not a continuous parameter |
| `[16]` | `+0x40` | boolean — `0` in 84%, `1` in 16% |

(All other words are `0` in every sample checked.)

## `TransformedFixed128` sub-header (128 bytes, `u32[32]`)

| Word | Offset | Meaning |
|---|---|---|
| `[0]` | `+0x00` | echoes the record's own id |
| `[1]` | `+0x04` | `1` |
| `[5]` | `+0x14` | a negative `Q16.16` fixed-point value when nonzero (e.g. `-12.0`, `-8.0`) — plausibly a gain/dB adjustment applied by this type's own post-load transform |
| `[7]` | `+0x1C` | an id-reference: matches the positionally-preceding record 59% of the time, some id in the same file 72% of the time |
| `[9]` | — | boolean, `1` in 97%, `0` in 3% |
| `[17]` | `+0x44` | `1` (94%) or `2` (rare, ~2%) — correlates with the sibling `FlatCopy` payload's size (an ~11× larger average when `2`), consistent with a **channel-count field** |
| `[19]` | `+0x4C` | **sample rate** — always a standard real-world audio rate: `32000` (44%), `22050` (42%), `48000` (10%), `44100` (3%), rarer `24000`/`16000`/`12000`/`8000`/`6000` |
| `[20]` | `+0x50` | irregular values in the low thousands, not a rate (equals `[19]` in only 0.1% of records) — reads more like a decoded sample/frame count or output buffer size |
| `[25]` | `+0x64` | `4` (81%) or `3` (19%) |
| `[28]` | `+0x70` | `7` (99.8%) |
| `[31]` | `+0x7C` | `0xFFFFFFFF` (99.9%) |

## Preamble words and the `extra` field

Each record's preamble (the count-prefixed word list before its payload) is copied verbatim into a
small heap-allocated wrapper object and cached in an **id-keyed map** owned by the sound resource
(keyed by that record's own id, not a locale/language table). The pointer to that copied preamble
data becomes the `extra` field on the record's in-memory descriptor (`{id, dataPtr, size, extra}`).

A downstream consumer reads it back: if `extra` is non-null and the leading preamble word-count is
nonzero, a pointer to the first actual preamble word (skipping the count) is threaded alongside the
resolved sound object into the runtime playback/event-dispatch system — generic engine machinery, not
specific to this container format, and not traced further.

Statistically, the word immediately before a preamble's trailing self-id (present whenever a record
has 2+ preamble words — the large majority of real records) resolves to *some* real id elsewhere in
the corpus 98.3% of the time, confirming these are genuine cross-references to other sound resources.
It's usually **not** this same bank's own id (only 30% of the time) and rarely a simple numeric
neighbor (`±1`, only 8.8%) — it typically points at some other, unrelated-by-numbering id.

## Relationship to `.sbao`

Type `0x50000000` (`Streamed`) is the only record type that ever needs external file data — and it's
never actually stored inside a `.spk` bank (rejected outright, see the type table above). Streamed
sounds instead exist exclusively as standalone `<id>.sbao`/`<id>.bao` files, loaded directly from the
id (`sprintf("%08x.sbao", id)`) when a resource's descriptor has no inline data. Real `.spk` record
ids and real `.sbao` file ids overlap at only ~0.01% (noise level) across a whole install — the two
are mutually exclusive storage paths for the same id-space, not a referencing relationship. See
[the `.sbao` page](./sbao.md) for that format's own layout.

## The audio codec(s): Ogg Vorbis and IMA-ADPCM

`FlatCopy`'s payload (no sub-header, the entire remainder copied verbatim) is where the actual
compressed audio bytes live — up to 2.67 MB observed in a single record. It's not one fixed codec:
real records split roughly **74%/26% Ogg Vorbis / IMA-ADPCM**, distinguished per record by whether the
payload's own bytes parse as a valid Ogg Vorbis identification header.

### Ogg Vorbis variant (~74%)

The majority case: the `FlatCopy` payload *is* a complete, standard Ogg container — starting directly
with an `OggS` page-magic first page, whose first packet is a standard Vorbis identification header
(`0x01` + `"vorbis"` + version/channel-count/sample-rate fields). Unlike the Ogg-backed variant of
[`.sbao`](./sbao.md) (see "Relationship to `.sbao`" above), there's no extra engine header wrapping it
here — the record's own 40-byte core is immediately followed by Ogg bytes, full stop. Sample rate and
channel count are read straight out
of the embedded Vorbis ID packet rather than needing the sibling `TransformedFixed128` record's own
(possibly redundant, unconfirmed-in-this-case) sample-rate field.

Since it's a complete, independently-valid audio file, no proprietary decode work was needed here
beyond detecting it — any standard Ogg Vorbis decoder handles it directly, and replacing one is just a
matter of dropping in a different valid Ogg Vorbis stream of the same sample rate/channel count (no
playback-length metadata to keep in sync — the container is self-describing; contrast with the
IMA-ADPCM variant below, which does require that).

### IMA-ADPCM variant (~26%)

The minority case, and the one with no `RIFF`/`OggS` signature anywhere in it — this is a proprietary
raw `TImaAdpcm` stream, requiring the full trace below to decode.

The codec is **standard IMA-ADPCM**. Found by pulling `Dunia.dll` off disk and byte-searching it
directly for the canonical reference tables every textbook IMA-ADPCM implementation ships with —
the 16-entry step-index adjustment table (`-1,-1,-1,-1,2,4,6,8,-1,-1,-1,-1,2,4,6,8`) and the 89-entry
step-size table (`7,8,9,...,32767`). Both exist byte-for-byte in `Dunia.dll`'s `.rdata` section,
stored as `int32` arrays, back to back (index table at `0x10ee3928`, step table at `0x10ee3968`).

Two real decoder functions consume these tables:
- **Mono** (`0x10a85150`) — textbook IMA-ADPCM: unpack a nibble, `diff = (step * (2*(nibble&7)+1)) >>
  3`, negate if bit 3 of the nibble is set, add to the predictor and clamp to `[-32768, 32767]`,
  adjust the step-index via the index table and clamp to `[0, 88]`, look up the new step, next
  sample.
- **Stereo** (`0x10a85240`) — the same algorithm with two independent predictor/step-index states,
  where each byte's high nibble is channel A's next sample and low nibble is channel B's — i.e. every
  byte yields one interleaved `(L, R)` frame directly ("4 bits separate" per-channel encoding).

Both are dispatched from a single function on a channel flag, whose caller is the real `TImaAdpcm`
decode method (confirmed via its own error strings, `"TImaAdpcm: Incoherency in IMA-ADPCM resource
header"` / `"TImaAdpcm: IMA-ADPCM version seems to be too old"`). That method reads a **28-byte
header** once, at the very start of a stream, before switching into steady-state decode:

| Offset | Size | Meaning |
|---|---|---|
| `0x00` | 1 | version — must be exactly `5` |
| `0x01` | 11 | unidentified |
| `0x0C` | 1 | channel-mode flag (`0` = mono, `1` = stereo) |
| `0x0D` | 3 | unidentified |
| `0x10` | 2 | initial predictor, channel A (`u16` LE) |
| `0x12` | 1 | initial step-index, channel A (`u8`) |
| `0x13` | 1 | unidentified/padding |
| `0x14` | 2 | initial predictor, channel B (`u16` LE) — only meaningful when stereo |
| `0x16` | 1 | initial step-index, channel B (`u8`) |
| `0x17` | 5 | unidentified/padding (header total is `0x1C` = 28 bytes) |

After the header, the rest of the stream is packed IMA-ADPCM nibbles as described above.

**Verified against real data:** checked this exact layout against two real IMA-ADPCM-variant
`FlatCopy` payloads (one mono, one stereo) — the version byte was `5` in both, and the channel-mode flag correctly predicted
mono vs. stereo (matching, independently, the `TransformedFixed128` word `[17]` channel-count
correlation found from statistics alone). Reimplementing the decode loop directly (a plain Python
port of the two decoder functions above) and running it against both payloads end to end consumed
**100% of the post-header bytes with no errors**, producing output statistics consistent with real
audio (a wide, roughly zero-centered dynamic range rather than clipped-flat or diverging garbage),
and was written out as playable `.wav` files using the sample rate read from each record's own
`TransformedFixed128` sibling.

This is a genuinely standard, publicly documented algorithm — not a customized dialect — so any
off-the-shelf IMA-ADPCM decoder applies once the 28-byte header is skipped and the channel mode is
read. It's also a different codec family entirely from Ubisoft's older in-house "Ubi Sound Tools"
ADPCM dialects (the ones the third-party tool `Ubitunedec` decodes, used in older titles like *XIII*
and *Splinter Cell*) — a coincidence of both being "an ADPCM," not the same codec.

DARE's own `"Adpcm allows only sound files with 1, 2, 4 and 6 channels"` string implies channel
counts above 2 are supported somewhere, presumably by combining multiple mono/stereo sub-streams
rather than a single stream with a channel-mode byte above 1 — not verified against a real sample,
since none appeared in the corpus scan.

## Playback length: shorter IMA-ADPCM replacements decode as trailing noise

Modding symptom: replace a `FlatCopy` record's IMA-ADPCM audio with a shorter clip (payload bytes +
container `size` field both correctly rewritten to the new, smaller length) and the game plays your
replacement correctly for its own duration, then keeps going — decoding a burst of noise for the
*remainder of the original clip's duration* instead of stopping cleanly or going silent. This means
whatever governs total playback length is not simply "walk the IMA-ADPCM stream until the reader
runs out of input bytes," despite that being all that's structurally required to decode a stream on
its own (see above).

**First hypothesis, tested and falsified:** `TImaAdpcm_DecodeStream` (`0x10a7f9e0`, the method behind
the `"TImaAdpcm: Incoherency in IMA-ADPCM resource header"` error strings) decodes through a
`TImaAdpcm` object that carries a counter at offset `+0x30`, decremented every decode call. On first
read this looked like a "total remaining samples in the clip" gate seeded once at load time from the
sibling `TransformedFixed128` record's word `[20]` (`+0x50`) — plausible, since that word was already
flagged above as "reads more like a decoded sample/frame count than a second rate field" from
statistics alone. A fix built on that theory (patching word `[20]` to the replacement's real sample
count after every swap) was shipped and then **empirically failed to change the symptom at all**.

Re-reading `TImaAdpcm_DecodeStream` more carefully after that failure showed why: `+0x30` is actually
an internal look-ahead **buffer** counter — how many already-decoded samples are sitting in a scratch
buffer waiting to be handed to the caller this call — refilled from the byte-stream reader (`+0x24`)
and drained every streaming call. It has nothing to do with total clip length; the "seeded from
word `[20]` at load" theory doesn't hold up. That patch was reverted.

The real mechanism traces further than this: `TImaAdpcm` construction goes through a codec-selector
dispatch (`FUN_10a7ae40`) reached from a generic multi-stage "voice" construction function
(`FUN_10a6ff20`) that wires up several DARE-pipeline sub-objects (decoder, and at least three more
unidentified stages) — shared machinery across all its codecs, not something specific to `.spk`. The
trace was not carried further into that pipeline to find where (or whether) an actual total-length
value gets set.

**Practical workaround, not a root-cause fix:** JackAll's `.spk` audio importer
(`SpkFileHandler.ImportAudio_Click`) now pads a shorter IMA-ADPCM replacement with trailing digital
silence (real zero-valued PCM samples run through the same encoder, so the predictor decays cleanly)
up to the original clip's own sample count before encoding, rather than trying to declare the new,
shorter length anywhere. This keeps the encoded byte length from ever shrinking below the original,
so whatever the real length-governing mechanism turns out to be, it can't run past a same-or-longer
buffer — trading "properly detecting and declaring the new duration" for "never giving the engine
less data than before." Ogg Vorbis–backed records need no such workaround — that container is
self-describing.

## Open questions

- The four unidentified 40-byte-core fields (`0x08`-`0x14`) — confirmed not a checksum of the payload,
  otherwise unidentified. Possibly a secondary id, spatial/priority data, or similar.
- The concrete game-design meaning behind each record type (which is used for one-shot vs. looping vs.
  3D-positioned sounds, etc.) and most of `TransformedFixed128`'s untabulated sub-header words.
- What `SimpleFixed68` word `[7]`'s small cluster of heavily-reused "category" ids actually represents.
- The unidentified bytes in the 28-byte ADPCM stream header (`0x01`-`0x0B`, `0x0D`-`0x0F`,
  `0x17`-`0x1B`).
- Whether the channel-mode byte can represent channel counts above 2 directly, or whether >2-channel
  audio is always built from multiple sub-streams instead.
- What actually governs an IMA-ADPCM `FlatCopy` record's total playback length (see "Playback length"
  above) — confirmed *not* to be `TransformedFixed128` word `[20]`, and traced as far as a generic
  multi-stage DARE "voice" construction pipeline (`FUN_10a6ff20` → codec-selector `FUN_10a7ae40`) with
  several still-unidentified sub-objects, but not resolved to a concrete field or instruction. What
  `TransformedFixed128` word `[20]` itself actually represents, if not this, is also still open.
