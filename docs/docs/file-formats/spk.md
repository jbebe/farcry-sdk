---
sidebar_position: 7
---

# `.spk` — Sound Bank Format

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against **`Dunia.dll`** (the same code exists but is stubbed out in the
Linux dedicated server, which never plays audio). Confirmed against every real `.spk` file in a Steam
v1.03 install (8,282 files, 42,215 records, zero parse failures) via a standalone parser
(`tools/JackAll/src/JackAll.Core/Format/SpkPackage.cs`) and cross-checked against a hand-written Python
decoder run against real extracted payloads. Companion page: [`.sbao`](./sbao.md), the standalone
(non-bank) sibling format sharing the same DARE data.
:::

`.spk` files are hash-named sound banks (`soundbinary\<id:08x>.spk`) holding multiple small DARE
("Ubisoft's proprietary audio middleware, config in `Data_Win32/SoundBinary/DARE.INI`") resource
records, each identified by its own id.

:::note["Spk"/"SPK" means three different things in `Dunia.dll`]
A string search for `spk`/`SPK` also turns up two unrelated subsystems: `scripts\game\BarkData\<N>.spk`
(decimal-numbered, the AI dialogue/bark script system) and `fNearLimitSpkDist`/`fFarLimitSpkDist`
(tuning properties on an in-editor "SpeakerSet" sound-emitter entity — "Spk" short for "Speaker").
Neither is the sound-bank container documented below.
:::

## Container format

All fields little-endian, 4-byte aligned.

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

The engine's own parser rejects a buffer under `0x10` bytes, a magic mismatch, `count == 0`, or a
buffer too small to hold the id table (`size <= count*4 + 0xC`). The per-record loop re-validates
bounds every iteration, so a truncated/corrupt trailing record is caught rather than walked off the
end of the buffer.

## Loading pipeline

1. A sound id becomes a filename: `"<bank_dir><id:08x>.spk"`, or, with bit `0x40000000` of the id set,
   a localized variant `"<bank_dir><lang>\<id:08x>.spk"`.
2. The file opens through **`VFS_ResolvePath`** — the same hooked resolver documented on the
   [archives page](./archives-fat-dat.md), not a bypass path. The loose-file mod-loader override
   applies to `.spk` banks exactly the same way it does to `.fat`/`.dat` archive contents.
3. The whole file is read into a buffer and handed to the container parser via a virtual call.
4. Standalone `.sbao`/`.bao` files (below) are opened through the identical `VFS_ResolvePath` call —
   no separate, unhooked path for that case either.

## Record core (40 bytes)

Every record's payload begins with a common 40-byte core:

| Offset | Size | Field | Value |
|---|---|---|---|
| `0x00` | 4 | magic | `02 1F 00 10` (constant) |
| `0x04` | 4 | declared size | `40` (`0x28`) — always exactly this, in all 42,215 real records; a hardcoded structure-version tag, not a field that varies |
| `0x08`–`0x14` | 4 each | unidentified | differs per record (high-entropy); confirmed not a CRC32 or Adler32 checksum of the rest of the payload |
| `0x18`, `0x1C` | 4 each | unidentified | `0` in every record checked |
| `0x20` | 4 | type tag | one of the 7 constants below |
| `0x24` | 4 | unidentified | `0x2` in every record checked |

Anything under 40 bytes is rejected outright ("*Invalid object size: you have probably loaded an old
version of the data*"), confirming the declared-size field really is a hardcoded version tag.

## Record types

| Type | Name | Behavior | Share of real records |
|---|---|---|---|
| `0x10000000` | `SimpleFixed68` | Fixed 68-byte sub-header, remainder copied verbatim. | 34% |
| `0x20000000` | `TransformedFixed128` | Fixed 128-byte sub-header, then a dedicated post-load transform — the only fixed-size type that does more than copy. | 39% |
| `0x30000000` | `FlatCopy` | No sub-header — entire remainder copied verbatim. Where the compressed audio bytes live. | 27% |
| `0x40000000` | `LargeFixed256` | Fixed 256-byte sub-header, plain copy. | never seen in a real install |
| `0x50000000` | `Streamed` | Rejected outright when loading bank data ("*Can't load atomic object id (0x%X) because it's a streamed sound data*"). Streamed sounds exist only as standalone `<id>.sbao`/`<id>.bao` files. | 0% (by definition) |
| `0x60000000` | `CountPrefixedList` | Reads a leading count, consumes `count*4 + 4` bytes — a count-prefixed reference list, likely a randomized-variation group. | never seen in a real install |
| `0x70000000` | `SelfReferential` | Plain copy, but the first two fields of the copy are then read as `{offset, flag}`: if `flag != 0`, `offset` is rewritten to an absolute pointer into the copy — an internal fixup. | 0.2% |

Real `.spk` banks only ever contain `SimpleFixed68`/`TransformedFixed128`/`FlatCopy` plus rare
`SelfReferential`. Banks tend to hold matched sets: 80% of files whose record count is a multiple of 3
contain exactly an equal count of all three common types, though not always laid out as consecutive
triples.

## `SimpleFixed68` sub-header (68 bytes, `u32[17]`)

| Word | Offset | Meaning |
|---|---|---|
| `[0]` | `+0x00` | echoes the record's own id |
| `[1]` | `+0x04` | `1` in 98% of records; otherwise `2`/`4`/`8` — possibly a variant/voice count |
| `[2]` | `+0x08` | an id-reference: resolves to some real id in the corpus 99.9% of the time, to a record in the same file 79% of the time |
| `[4]` | `+0x10` | constant `0x00010000` = `1.0` in Q16.16 fixed point — plausibly an identity gain/scale default |
| `[7]` | `+0x1C` | an id-reference-shaped field; `0xFFFFFFFF` sentinel only 14% of the time — the rest cluster heavily on a handful of ids (one accounts for 29% of all records), reading more like a shared category/template reference |
| `[9]` | `+0x24` | `0` in 90% of records; when nonzero, always exactly `+100` or `-100` — a discrete signed flag |
| `[16]` | `+0x40` | boolean — `0` in 84%, `1` in 16% |

(All other words are `0` in every sample checked.)

## `TransformedFixed128` sub-header (128 bytes, `u32[32]`)

| Word | Offset | Meaning |
|---|---|---|
| `[0]` | `+0x00` | echoes the record's own id |
| `[1]` | `+0x04` | `1` |
| `[5]` | `+0x14` | negative Q16.16 fixed-point value when nonzero (e.g. `-12.0`, `-8.0`) — plausibly a gain/dB adjustment applied by this type's post-load transform |
| `[7]` | `+0x1C` | an id-reference: matches the positionally-preceding record 59% of the time, some id in the same file 72% of the time |
| `[9]` | — | boolean, `1` in 97% |
| `[17]` | `+0x44` | `1` (94%) or `2` (~2%) — correlates with the sibling `FlatCopy` payload's size (~11× larger average when `2`), consistent with a **channel-count field** |
| `[19]` | `+0x4C` | **sample rate** — always a standard real-world rate: `32000` (44%), `22050` (42%), `48000` (10%), `44100` (3%), rarer `24000`/`16000`/`12000`/`8000`/`6000` |
| `[20]` | `+0x50` | irregular values in the low thousands, not a rate (equals `[19]` in only 0.1%) — reads like a decoded sample/frame count or output buffer size |
| `[25]` | `+0x64` | `4` (81%) or `3` (19%) |
| `[28]` | `+0x70` | `7` (99.8%) |
| `[31]` | `+0x7C` | `0xFFFFFFFF` (99.9%) |

## Preamble words and the `extra` field

Each record's preamble (the count-prefixed word list before its payload) is copied into a small
heap-allocated wrapper and cached in an id-keyed map owned by the sound resource. The pointer becomes
the `extra` field on the record's in-memory descriptor (`{id, dataPtr, size, extra}`), threaded
alongside the resolved sound object into the runtime playback/event-dispatch system — generic engine
machinery, not specific to this container.

Statistically, the word immediately before a preamble's trailing self-id (present when a record has 2+
preamble words, the large majority) resolves to some real id elsewhere in the corpus 98.3% of the time
— genuine cross-references to other sound resources. It's usually not this same bank's own id (30% of
the time) and rarely a simple numeric neighbor (`±1`, 8.8%).

## Relationship to `.sbao`

`Streamed` (`0x50000000`) is the only record type that needs external file data, and it's never stored
inside a `.spk` bank — rejected outright. Streamed sounds exist exclusively as standalone
`<id>.sbao`/`<id>.bao` files, loaded directly from the id (`sprintf("%08x.sbao", id)`) when a
resource's descriptor has no inline data. Real `.spk` record ids and real `.sbao` file ids overlap at
only ~0.01% (noise level) across a whole install — mutually exclusive storage paths for the same
id-space, not a referencing relationship. See [`.sbao`](./sbao.md) for that format's own layout.

## The audio codecs: Ogg Vorbis and IMA-ADPCM

`FlatCopy`'s payload (no sub-header, entire remainder verbatim) is where the compressed audio bytes
live — up to 2.67 MB observed in a single record. Real records split roughly **74%/26% Ogg Vorbis /
IMA-ADPCM**, distinguished per record by whether the payload parses as a valid Ogg Vorbis
identification header.

### Ogg Vorbis (~74%)

The `FlatCopy` payload is a complete, standard Ogg container — starting directly with an `OggS`
page-magic first page, whose first packet is a standard Vorbis identification header. Unlike the
Ogg-backed variant of [`.sbao`](./sbao.md), there's no extra engine header wrapping it here — the
record's own 40-byte core is immediately followed by Ogg bytes. Sample rate and channel count are read
straight out of the embedded Vorbis ID packet rather than needing the sibling `TransformedFixed128`
record's own sample-rate field.

Since it's a complete, independently-valid audio file, no proprietary decode work was needed beyond
detection — any standard Ogg Vorbis decoder handles it, and replacing one is just dropping in a
different valid Ogg Vorbis stream of the same sample rate/channel count. No playback-length metadata
to keep in sync — the container is self-describing.

### IMA-ADPCM (~26%)

No `RIFF`/`OggS` signature — a proprietary raw `TImaAdpcm` stream. The codec itself is **standard
IMA-ADPCM**, found by byte-searching `Dunia.dll` directly for the canonical reference tables every
textbook implementation ships with: the 16-entry step-index adjustment table
(`-1,-1,-1,-1,2,4,6,8,-1,-1,-1,-1,2,4,6,8`) and the 89-entry step-size table (`7,8,9,...,32767`). Both
exist byte-for-byte in `Dunia.dll`'s `.rdata` section as `int32` arrays, back to back (index table at
`0x10ee3928`, step table at `0x10ee3968`).

Two decoder functions consume these tables:

- **Mono** (`0x10a85150`) — textbook IMA-ADPCM: unpack a nibble, `diff = (step * (2*(nibble&7)+1)) >>
  3`, negate if bit 3 is set, add to the predictor and clamp to `[-32768, 32767]`, adjust the
  step-index via the index table and clamp to `[0, 88]`, look up the new step, next sample.
- **Stereo** (`0x10a85240`) — the same algorithm with two independent predictor/step-index states,
  where each byte's high nibble is channel A's next sample and low nibble is channel B's.

Both are dispatched from a single function on a channel flag, whose caller is the real `TImaAdpcm`
decode method (confirmed via its own error strings, `"TImaAdpcm: Incoherency in IMA-ADPCM resource
header"` / `"...version seems to be too old"`). That method reads a **28-byte header** once, before
switching into steady-state decode:

| Offset | Size | Meaning |
|---|---|---|
| `0x00` | 1 | version — must be exactly `5` |
| `0x01` | 11 | unidentified |
| `0x0C` | 1 | channel-mode flag (`0` = mono, `1` = stereo) |
| `0x0D` | 3 | unidentified |
| `0x10` | 2 | initial predictor, channel A (u16 LE) |
| `0x12` | 1 | initial step-index, channel A (u8) |
| `0x13` | 1 | unidentified/padding |
| `0x14` | 2 | initial predictor, channel B (u16 LE) — meaningful only when stereo |
| `0x16` | 1 | initial step-index, channel B (u8) |
| `0x17` | 5 | unidentified/padding (header total `0x1C` = 28 bytes) |

After the header, the rest of the stream is packed IMA-ADPCM nibbles.

**Verified against real data**: checked against two real IMA-ADPCM `FlatCopy` payloads (one mono, one
stereo) — version byte `5` in both, channel-mode flag correctly predicted mono/stereo (matching the
`TransformedFixed128` word `[17]` channel-count correlation found independently from statistics). A
plain Python port of the decode loop, run against both payloads end to end, consumed 100% of the
post-header bytes with no errors, produced output statistics consistent with real audio, and was
written out as playable `.wav` files using the sample rate from each record's `TransformedFixed128`
sibling.

This is a standard, publicly documented algorithm — any off-the-shelf IMA-ADPCM decoder applies once
the 28-byte header is skipped. It's a different codec family from Ubisoft's older in-house "Ubi Sound
Tools" ADPCM dialects (decodable by the third-party tool `Ubitunedec`, used in older titles like *XIII*
and *Splinter Cell*) — a coincidence of both being "an ADPCM," not the same codec.

DARE's own string `"Adpcm allows only sound files with 1, 2, 4 and 6 channels"` implies channel counts
above 2 are supported somewhere, presumably by combining multiple mono/stereo sub-streams rather than a
single stream with a channel-mode byte above 1 — not verified against a real sample.

## Playback length: shorter IMA-ADPCM replacements decode as trailing noise

Modding symptom: replace a `FlatCopy` record's IMA-ADPCM audio with a shorter clip (payload bytes and
the container `size` field both correctly rewritten) and the game plays the replacement correctly for
its own duration, then decodes a burst of noise for the *remainder of the original clip's duration*
instead of stopping cleanly. Whatever governs total playback length is not simply "decode until the
reader runs out of input bytes."

`TImaAdpcm_DecodeStream` (`0x10a7f9e0`) carries a counter at offset `+0x30`, decremented every decode
call — **not** a "total remaining samples" gate seeded from `TransformedFixed128` word `[20]` as first
suspected (a fix patching word `[20]` to the replacement's real sample count was tried and had no
effect on the symptom). It's actually an internal look-ahead **buffer** counter — decoded samples
sitting in a scratch buffer waiting to be handed to the caller, refilled from the byte-stream reader and
drained every call, unrelated to total clip length.

`TImaAdpcm` construction goes through a codec-selector dispatch (`FUN_10a7ae40`) reached from a generic
multi-stage "voice" construction function (`FUN_10a6ff20`) wiring up several DARE-pipeline sub-objects
(decoder plus at least three more unidentified stages) — shared machinery across all DARE codecs. Where
(or whether) an actual total-length value gets set within that pipeline is unresolved.

**Practical workaround**: JackAll's `.spk` audio importer (`SpkFileHandler.ImportAudio_Click`) pads a
shorter IMA-ADPCM replacement with trailing digital silence up to the original clip's own sample count
before encoding, rather than declaring a shorter length. This keeps the encoded byte length
same-or-longer than the original, so whatever the real length-governing mechanism is, it can't run past
the buffer. Ogg Vorbis records need no such workaround — the container is self-describing.

## Unknowns

- The four unidentified 40-byte-core fields (`0x08`–`0x14`) — confirmed not a checksum, otherwise
  unidentified. Possibly a secondary id, spatial/priority data, or similar.
- The concrete game-design meaning of each record type (one-shot vs. looping vs. 3D-positioned sounds)
  and most of `TransformedFixed128`'s untabulated sub-header words.
- What `SimpleFixed68` word `[7]`'s small cluster of heavily-reused "category" ids represents.
- The unidentified bytes in the 28-byte ADPCM stream header.
- Whether the channel-mode byte can represent channel counts above 2 directly, or whether >2-channel
  audio is always built from multiple sub-streams.
- What actually governs an IMA-ADPCM `FlatCopy` record's total playback length — confirmed not
  `TransformedFixed128` word `[20]`, traced as far as the DARE "voice" construction pipeline with
  several unidentified sub-objects, not resolved to a concrete field or instruction.
