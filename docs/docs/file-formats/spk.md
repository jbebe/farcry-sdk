---
sidebar_position: 7
---

# `.spk` — Sound Bank Format

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against **`Dunia.dll`** (the same code exists but is stubbed out in the
Linux dedicated server, which never plays audio). Confirmed against every real `.spk` file in a Steam
v1.03 install (8,282 files, 42,215 records, zero parse failures) via a standalone parser
(`tools/JackAll/src/JackAll.Tools/Spk/SpkPackage.cs`) and cross-checked against a hand-written Python
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

## Binary event objects

A `SimpleFixed68` record is not a sound. It is a **binary event object** — the engine's own term, from
the failure path of its post-load fixup (`FUN_10a3ebd0`, `Dunia.dll`):

```
ERROR: Cannot init binary event, unknown event type.
```

That fixup switches on sub-header **word[1]**, which is the **event type**, not a variant count. The
rest of the sub-header is a union keyed by it. Three functions read the type and together define what
each one means:

- **`FUN_10a3ebd0`** — the post-load fixup. Rewrites each type's id-shaped fields into live pointers
  via `FUN_10a419f0` → `FUN_10a40aa0(id, 1)`, a registry lookup that also takes a reference (the
  counters behind `Atomic Object 0x%x should have its internal counters to zero (RefCount = %d,
  LoadCount = %d)`).
- **`FUN_10a38d20`** — the play dispatcher.
- **`FUN_10a391e0`** — duration; logs `Invalid sound event type.` for anything it does not handle.

| Type | Fixup resolves | On play | Duration | Records |
|---|---|---|---|---|
| `1` | `[2]`, `[7]` | starts a voice | real | 4,149 |
| `2` | `[2]` | no-op | `-1` | 241 |
| `3`, `8`, `10` | nothing | no-op | `-1` | 18 (`8` only) |
| `4` | `[2]`, `[3]` | starts a voice, after an extra step | `-1` | 60 |
| `5`, `6`, `7`, `9` | `[2]`, `[6]` | starts a voice | real | — |
| `11` | `[4]`; then a table at byte offset `[5]`, `[6]` entries of 3 words | iterates **every** entry, recursing | `-1` | 8 |
| `12` | an array at byte offset `[2]`, `[3]` entries of 1 word | iterates **every** entry, recursing | `-1` | 65 |

Counts are over the 4,895 `.spk` files extracted into this repo's `tmp/`, which is not a full install
— treat them as proportions, not totals. Types `5`/`6`/`7`/`9` appear in none of them.

### Types `11` and `12` carry a tail

Only these two put anything after the 68-byte sub-header, and they are the only records in the corpus
whose payload exceeds `0x28 + 68`. The tail is a list the fixup walks in place, replacing each id with
the resolved object:

```
type 12:  u32[ [3] ]              // child event/resource ids, at tail byte offset [2]
type 11:  { u32 id, u32 resolved, u32 key }[ [6] ]   // at tail byte offset [5]
```

`[2]` and `[5]` are byte offsets into the tail, **not** id-references — they are `0` in every real
record. The arithmetic closes exactly: `[2] + [3]*4` equals the tail size in all 65 type-`12` records,
and `[5] + [6]*12` in all 8 type-`11` records. Every type-`12` tail id is a real bank id, and none of
the 70 records carrying a tail holds any audio of its own.

The play dispatcher iterates the whole list and calls itself on each entry — no random selection, no
break on first success — so a type-`12` event fires **all** of its children. It is a layered
composite, and that is what the data shows: 43 of the type-`12` events have exactly two children, and
one child is shared across many weapons (`0x004565A6` appears in 8 of them, `0x004B291E` in 9) — a
common layer mixed under a per-weapon one. Nesting is supported by the recursion but never used.

:::warning[Tools that read word[2] as a link will show a dead end]
A type-`12` event's word[2] is a byte offset, so anything that prints it as a "linked id" reports `0`
and never reaches the tail. `jackall-cli spk list` does exactly this. A one-entry list event — such as
the Dart Rifle's first-person shot, `0x004BF5EA` → `0x004BF5E9` — therefore looks like a bank
containing nothing but a parameter record pointing nowhere.
:::

### What loads the child bank: `depload`

A child id in a tail is only ever a **registry lookup**. `FUN_10a419f0` → `FUN_10a40aa0(id, 1)` finds an
already-registered atomic object and takes a reference; there is **no load-on-miss path**. An id that
was never loaded resolves to `0`, and the play dispatcher bails with `Atomic object 0x%X has not been
loaded!`. Parsing a bank registers only the records inside *that file* — it never opens another.

So something else has to have loaded the child bank first, and that something is
[`depload`](./depload.md), which lists sound banks by path as `CSoundResource` entries — 5,480 of them
in one world — with the parent/child relation spelled out:

```xml
<CSoundResource ID="soundbinary\004bf5ea.spk" nbChildren="3">
    <CSoundResource ID="soundbinary\804e1b35.spk" />
    <CSoundResource ID="soundbinary\00449311.spk" />
    <CSoundResource ID="soundbinary\004bf5e9.spk" />
</CSoundResource>
```

The child list is **wider than the tail**: three banks resident against one dispatched to. The tail is
the immediate play list; `depload` is the transitive set the event can reach (`00449311`'s own word[2]
points at `004e1b35`, and `804e1b35` is that id's localized variant — the high-bit form from the
[loading pipeline](#loading-pipeline)). It matches the corpus from the other side too: the three banks
listed here are exactly the three whose record preambles carry `0x004BF5EA` — see
[preamble words](#preamble-words-and-the-extra-field).

:::danger[For sound, `depload` is a requirement, not a prefetch hint]
The [`depload` page](./depload.md) notes that a missing entry costs a texture only streaming warmth,
while an animation genuinely fails to load. **Sound behaves like animation, and for a sharper reason**:
a texture is asked for by path, so the resource system can still find it, but a sound is only ever
asked for by id against a registry that cannot load. Replacing audio inside an existing chain is safe —
the entries already exist. Pointing a weapon at a *new* bank chain needs the `depload` entry added, or
the event resolves to null and the weapon is silent.

Inferred from the absence of a load-on-miss path rather than tested with a deliberately unlisted bank.
:::

### Leaf fields (type `1`, 91% of records)

| Word | Offset | Meaning |
|---|---|---|
| `[0]` | `+0x00` | echoes the record's own id |
| `[1]` | `+0x04` | the event type above |
| `[2]` | `+0x08` | the sound resource this event plays — resolved to a live pointer by the fixup |
| `[4]` | `+0x10` | constant `0x00010000` = `1.0` in Q16.16 fixed point — plausibly an identity gain/scale default |
| `[7]` | `+0x1C` | a second object reference, also resolved by the fixup; `0xFFFFFFFF` sentinel only 14% of the time, the rest clustering on a handful of ids (one accounts for 29% of all records) — reads like a shared category/template |
| `[9]` | `+0x24` | `0` in 90% of records; when nonzero, always exactly `+100` or `-100` — a discrete signed flag |
| `[16]` | `+0x40` | boolean — `0` in 84%, `1` in 16% |

(All other words are `0` in every sample checked.)

## `TransformedFixed128` sub-header (128 bytes, `u32[32]`)

| Word | Offset | Meaning |
|---|---|---|
| `[0]` | `+0x00` | echoes the record's own id |
| `[1]` | `+0x04` | `1` |
| `[2]` | `+0x08` | **the sibling `FlatCopy`'s audio byte length** — its payload size minus the 40-byte core. Exact in all 3,211 records that pair with a sibling, both codecs |
| `[5]` | `+0x14` | negative Q16.16 fixed-point value when nonzero (e.g. `-12.0`, `-8.0`) — plausibly a gain/dB adjustment applied by this type's post-load transform |
| `[7]` | `+0x1C` | an id-reference: matches the positionally-preceding record 59% of the time, some id in the same file 72% of the time |
| `[9]` | — | boolean, `1` in 97% |
| `[17]` | `+0x44` | `1` (94%) or `2` (~2%) — correlates with the sibling `FlatCopy` payload's size (~11× larger average when `2`), consistent with a **channel-count field** |
| `[19]` | `+0x4C` | **sample rate** — always a standard real-world rate: `32000` (44%), `22050` (42%), `48000` (10%), `44100` (3%), rarer `24000`/`16000`/`12000`/`8000`/`6000` |
| `[20]` | `+0x50` | irregular values in the low thousands, not a rate (equals `[19]` in only 0.1%) — reads like a decoded sample/frame count or output buffer size |
| `[22]` | `+0x58` | the same audio byte length as `[2]`, or `0`. Never a third value: of 3,211 paired records, 2,971 match `[2]` exactly and the remaining 240 are `0` |
| `[25]` | `+0x64` | `4` (81%) or `3` (19%) |
| `[28]` | `+0x70` | `7` (99.8%) |
| `[31]` | `+0x7C` | `0xFFFFFFFF` (99.9%) |

## Preamble words and the `extra` field

Each record's preamble (the count-prefixed word list before its payload) is copied into a small
heap-allocated wrapper and cached in an id-keyed map owned by the sound resource. The pointer becomes
the `extra` field on the record's in-memory descriptor (`{id, dataPtr, size, extra}`), threaded
alongside the resolved sound object into the runtime playback/event-dispatch system — generic engine
machinery, not specific to this container.

The list is **the bank's own id plus every parent that pulls it in** — the `depload` parent/child edge
recorded from the child's side. Cross-checked both ways on the Dart Rifle's first-person chain: the
three banks whose preambles carry `0x004BF5EA` (`004bf5e9`, `00449311`, `804e1b35`) are exactly the
three `depload` lists as that bank's children, and `004bf5eb` — a leaf nothing wraps — carries only
its own id. Self is not at a fixed position in the list; treat it as a set, not a sequence.

That also explains the earlier statistic: the word before a preamble's trailing entry resolves to a
real id elsewhere in the corpus 98.3% of the time, is usually not the bank's own id (30%), and is
rarely a numeric neighbour (`±1`, 8.8%) — the behaviour of a parent reference, not of a sibling or a
sequence number.

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

**Untested candidate**: `TransformedFixed128` word `[2]` is the sibling's audio byte length, exactly,
in all 3,211 paired records — a far better-behaved field than word `[20]`, which was the one actually
tried and ruled out. Neither JackAll's importer nor `jackall-cli spk import` updates it, so every
replacement made so far has shipped a descriptor still declaring the *original* clip's length. That is
exactly the shape of a read-length gate, and it would explain the symptom directly. It has **not** been
tested in game. Rewriting `[2]` (and `[22]`, which mirrors it) to the replacement's real stream length
is the obvious experiment.

## Unknowns

- The four unidentified 40-byte-core fields (`0x08`–`0x14`) — confirmed not a checksum, otherwise
  unidentified. Possibly a secondary id, spatial/priority data, or similar.
- The concrete game-design meaning of each record type (one-shot vs. looping vs. 3D-positioned sounds)
  and most of `TransformedFixed128`'s untabulated sub-header words.
- What `SimpleFixed68` word `[7]`'s small cluster of heavily-reused "category" ids represents.
- What separates the playable leaf event types (`1`, and the unseen `5`/`6`/`7`/`9`) from each other,
  and what the no-op types (`2`, `3`, `8`, `10`) are for — they resolve references and then decline to
  play.
- What a type-`11` event's third column keys on. The keys are a shared, dense id range (`0x00440261`–
  `0x0044027D` across six of the eight records) and word `[3]` holds the id immediately below that
  range — consistent with a switch group and its values, matching the archetype fields
  `sndswtpCloseFarSoundSwitchType` / `sndswvlCloseSoundSwitchValue`, but not traced to the code that
  reads them.
- The unidentified bytes in the 28-byte ADPCM stream header.
- Whether the channel-mode byte can represent channel counts above 2 directly, or whether >2-channel
  audio is always built from multiple sub-streams.
- What actually governs an IMA-ADPCM `FlatCopy` record's total playback length — confirmed not
  `TransformedFixed128` word `[20]`, traced as far as the DARE "voice" construction pipeline with
  several unidentified sub-objects, not resolved to a concrete field or instruction. Word `[2]` is an
  untested candidate; see [above](#playback-length-shorter-ima-adpcm-replacements-decode-as-trailing-noise).
