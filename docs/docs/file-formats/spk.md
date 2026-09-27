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

:::note[Two things named "spk" that are not hash-named banks]
The decimal-numbered `scripts\game\BarkData\loc\<N>.spk` files are this same container, holding NPC
voice lines; see [bark banks](./bark-banks.md). `fNearLimitSpkDist`/`fFarLimitSpkDist` are unrelated:
tuning properties on an in-editor "SpeakerSet" sound-emitter entity, "Spk" short for "Speaker".
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
| `0x40000000` | `LargeFixed256` | Fixed 256-byte sub-header, plain copy. | never in a `.spk`; the one retail object is `common/soundbinary/7fffffff.bao`, the DARE project descriptor (`ATOMIC` load mode), which holds the reverb presets |
| `0x50000000` | `Streamed` | Rejected outright when loading bank data ("*Can't load atomic object id (0x%X) because it's a streamed sound data*"). Streamed sounds exist only as standalone `<id>.sbao`/`<id>.bao` files. | 0% (by definition) |
| `0x60000000` | `CountPrefixedList` | Reads a leading count, consumes `count*4 + 4` bytes — a count-prefixed reference list, likely a randomized-variation group. | never seen in a real install |
| `0x70000000` | `SelfReferential` | Plain copy, but the first two fields of the copy are then read as `{offset, flag}`: if `flag != 0`, `offset` is rewritten to an absolute pointer into the copy — an internal fixup. Every retail record of this type is a [rolloff curve](#rolloff-curves). | 0.2% |

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
rest of the sub-header is a union keyed by it. Four functions read the type and together define what
each one means:

- **`FUN_10a3ebd0`** — the post-load fixup. Rewrites each type's id-shaped fields into live pointers
  via `FUN_10a419f0` → `FUN_10a40aa0(id, 1)`, a registry lookup that also takes a reference (the
  counters behind `Atomic Object 0x%x should have its internal counters to zero (RefCount = %d,
  LoadCount = %d)`).
- **`FUN_10a3c9c0`** — the play dispatcher.
- **`FUN_10a38d20`** — the resource enumerator: lists every resource an event can reach, which is why it
  walks every entry of a type `11` as well as a type `12`. It is not what plays them.
- **`FUN_10a391e0`** — duration; logs `Invalid sound event type.` for anything it does not handle.

| Type | Fixup resolves | On play (`FUN_10a3c9c0`) | Duration | Records |
|---|---|---|---|---|
| `1` | `[2]`, `[7]` | starts a voice on `[2]`, attenuated by the rolloff curve in `[7]` | real | 4,149 |
| `2` | `[2]` | acts on the event in `[2]` with the Q16.16 value in `[3]` (retail: 1.0, 0.3, 0.5, 3.0) — reads as stop-with-fade **(inferred)** | `-1` | 241 |
| `3` | nothing | acts on the event in `[2]` **(inferred: stop family)** | `-1` | — |
| `4` | `[2]`, `[3]` | **StopNGo** (the engine's own name, from `Cannot launch StopNGo event correctly`; `FUN_10a3acf0`): stops the event in `[2]` on the same object, then plays the event in `[3]`. With `[4]` nonzero and `[6]` zero, the play waits until the stopped instance has ended. Every retail third-person auto-fire stop is one, with `[2]` its own start loop, `[4]` `0.05` (Q16.16) and `[6]` `1`; see [automatic fire](../engine-internals/audio-runtime.md#automatic-fire) | `-1` | 60 |
| `5`, `6`, `7`, `9` | `[2]`, `[6]` | starts a voice | real | — |
| `8` | nothing | **sets the listener reverb** to the effect in `[2]`, a DARE project resource rather than a bank record; audible only with EAX (see [audio runtime](../engine-internals/audio-runtime.md#reverb)) | `-1` | 18 |
| `10` | nothing | applies a Q16.16 dB value over a duration to a target — a volume fade **(inferred)** | `-1` | — |
| `11` | `[4]`; then a table at byte offset `[5]`, `[6]` entries of 3 words | plays the **one** entry whose key matches the object's current value for switch `[3]`, else the default event `[4]` | `-1` | 8 |
| `12` | an array at byte offset `[2]`, `[3]` entries of 1 word | starts **every** entry at the same moment, grouped under one instance | `-1` | 65 |

Counts are over 4,895 extracted `.spk` files, which is not a full install — they are proportions, not
totals. Types `3`/`5`/`6`/`7`/`9`/`10` appear in none of them.

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

A type-`12` event plays through `FUN_10a3b090` (its log calls it a "MultiEvent"), which dispatches every
entry in turn — no random selection, no break on first success — and groups the voices under one
instance, so it fires **all** of its children at the same moment. There is no per-child offset: a layer
cannot be made to start later than its siblings. It is a layered composite, and that is what the data
shows: 43 of the type-`12` events have exactly two children, and one child is shared across many
weapons (`0x004565A6` appears in 8 of them, `0x004B291E` in 9) — a common layer mixed under a per-weapon
one. Nesting is supported by the recursion but never used.

A type-`11` event is a **switch**. `FUN_10a3ae70` reads the playing object's current value for the
switch group in word `[3]` and `FUN_10a37200` plays the one entry whose third word equals it; if none
does, the default event in word `[4]` plays. In retail the groups are the surface material
(`0x00440260`, whose values `0x00440261`… are the `sndswvlSoundSwitchValue`s in
`databases/materials/logicmaterials.xml`) and water depth (`0x00455A7F`). Most switching happens one
level down, in switch resources (see [`TransformedFixed128`](#transformedfixed128-sub-header-128-bytes-u3232)).

:::warning[Tools that read word[2] as a link will show a dead end]
A type-`12` event's word[2] is a byte offset, so anything that prints it as a "linked id" reports `0`
and never reaches the tail. A one-entry list event — such as
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

:::danger[`depload` is needed for a child bank, not for the bank an `.fcb` field names]
A sound id an `.fcb` field names is turned into `soundbinary\<id>.spk` by
`CSoundResource::GetFromSoundId` and loaded by that path (RE-verified on the Linux server). So a new
bank named after a new event loads with no `depload` entry. The G3KA4's per-round shots in
`mods/sound-overhaul` are such banks, `00fc0101.spk` and `00fc0107.spk`, which nothing lists
**(heard, 2026-09-26)**.

What the registry cannot load is an id reached only from inside a bank: a child in a tail, or a
resource another bank's event points at. That id must be in the same bank or in a bank `depload`
lists as a child, or it resolves to null and plays nothing. That half is inferred from the absence of
a load-on-miss path rather than tested with a deliberately unlisted child.
:::

The other way round is tested. Records appended to a bank that is already listed are registered when
it loads and play with no `depload` change: an echo event, sample and audio added to the Makarov's
`004569c9.spk` as `0x00FC0001`–`0x00FC0003` play in game **(heard, 2026-09-26)**. The new records
copied their siblings' preamble words and got fresh random core fields.

### Leaf fields (type `1`, 91% of records)

| Word | Offset | Meaning |
|---|---|---|
| `[0]` | `+0x00` | echoes the record's own id |
| `[1]` | `+0x04` | the event type above |
| `[2]` | `+0x08` | the sound resource this event plays — resolved to a live pointer by the fixup |
| `[4]` | `+0x10` | constant `0x00010000` = `1.0` in Q16.16 fixed point — plausibly an identity gain/scale default |
| `[7]` | `+0x1C` | the event's **rolloff curve**, resolved by the fixup and validated as a `"Rolloff"` object (`FUN_10a3fcd0`); see [rolloff curves](#rolloff-curves). `0xFFFFFFFF` means none: an unpositioned sound. In the retail banks 2,483 of 4,087 distinct type-`1` events point at one of the 96 curves, and the rest carry the sentinel. The heavy reuse is shared curves: one 80 m curve serves 949 events |
| `[9]` | `+0x24` | `0` in 90% of records; when nonzero, always exactly `+100` or `-100` — a discrete signed flag |
| `[16]` | `+0x40` | boolean — `0` in 84%, `1` in 16% |

(All other words are `0` in every sample checked.)

## Rolloff curves

:::info[Verified via reverse engineering and against the retail corpus]
The evaluator is `FUN_10a55440`, called from `FUN_10a66740` with the listener distance. All 96
`SelfReferential` records in the retail banks decode as curves, and every type-`1` word `[7]` that is not
the sentinel points at one of them.
:::

A rolloff curve is a `SelfReferential` record whose payload, after the 40-byte core, is:

```
u32   offset          // 0 in retail; fixed up to a pointer at load
u32   count
{ f32 distance_m; f32 gain_dB }[count]
```

All 96 live in one bank, `common/soundbinary/2fffffff.spk`: the rolloff pack, whose id
`CSoundSystem::GetRollOffPackId` returns.

The engine interpolates linearly between points and holds the last value past the last point. Every
retail curve ends with a point at −96 dB, so the last distance is the event's audible range. A curve can
start below 0 dB (`0 m: −3.8 dB`), and a flat run keeps a sound at full level out to some distance
(`0 m: 0 dB, 150 m: 0 dB, …`).

Ranges in retail run from 4 m to 1,000 m. The most used curve, `0x00442C37`, serves 949 events: −3.8 dB
at 0 m, −9.2 dB at 20 m, −19 dB at 56 m, −34.9 dB at 74 m, cut at 80 m. The longest, `0x004E1D0F`,
reaches −51.8 dB at 914 m and cuts at 1,000 m. Because curves are shared, editing one retunes every event
that points at it; giving one event its own falloff means a new curve record and a new word `[7]`.

## `TransformedFixed128` sub-header (128 bytes, `u32[32]`)

| Word | Offset | Meaning |
|---|---|---|
| `[0]` | `+0x00` | echoes the record's own id |
| `[1]` | `+0x04` | resource kind: `1` for a sample, which the rest of this table describes; see [resource containers](#resource-containers) for the others |
| `[2]` | `+0x08` | **the sibling `FlatCopy`'s audio byte length** — its payload size minus the 40-byte core. Exact in all 3,211 records that pair with a sibling, both codecs. The game plays the audio to this length; see [playback length](#playback-length-comes-from-the-descriptor) |
| `[5]` | `+0x14` | **gain** in dB, Q16.16: added to the voice's volume when the resource plays (RE-verified, `FUN_10a52370`); `-12.0` and `-8.0` are common |
| `[7]` | `+0x1C` | an id-reference: matches the positionally-preceding record 59% of the time, some id in the same file 72% of the time |
| `[9]` | — | boolean, `1` in 97% |
| `[17]` | `+0x44` | `1` (94%) or `2` (~2%) — correlates with the sibling `FlatCopy` payload's size (~11× larger average when `2`), consistent with a **channel-count field** |
| `[19]` | `+0x4C` | **sample rate** — always a standard real-world rate: `32000` (44%), `22050` (42%), `48000` (10%), `44100` (3%), rarer `24000`/`16000`/`12000`/`8000`/`6000` |
| `[13]` | `+0x34` | **loop flag**: `1` on the 219 samples that must loop, such as every automatic weapon's fire loop, `0` on the other 5,170. It decides which of the two length pairs below is filled **(seen in data; the reading code is not traced)** |
| `[20]` | `+0x50` | the average **byte rate**: `floor(bytes × rate / frames)` in 91% of samples, off by 1–2 in the rest. The game does not read it (see [playback length](#playback-length-comes-from-the-descriptor)) |
| `[21]`, `[22]` | `+0x54`, `+0x58` | one-shot length: `[21]` the frame count, `[22]` the byte length, equal to `[2]`. Both `0` when `[13]` is `1`. For Ogg Vorbis the frame count is the last page's granule position exactly; for IMA-ADPCM it is the stream's frames less 29 or 30 |
| `[23]`, `[24]` | `+0x5C`, `+0x60` | loop length, the same pair: set only when `[13]` is `1`, and `0` otherwise. Across all 5,389 retail samples the split has no exception |
| `[25]` | `+0x64` | **codec**: `3` for IMA-ADPCM, `4` for Ogg Vorbis, without exception |
| `[28]` | `+0x70` | `7` (99.8%) |
| `[31]` | `+0x7C` | `0xFFFFFFFF` (99.9%) |

### Resource containers

:::info[Verified against the retail corpus]
Kinds and layouts below are read from the data across every retail bank. The code that reads them is
not traced.
:::

Word `[1]` also marks resources that hold other resources rather than audio:

| Kind | Records | What it is |
| --- | --- | --- |
| `1` | 13,322 | a sample (the table above) |
| `3` | 107 | a **switch**: word `[8]` is the switch group, `[9]` the default child, `[7]` the entry count, then `{child, value}` pairs |
| `4` | 502 | a **random container**: `[7]` the entry count, `[8]` a remaining weight, then `{child, weight, flag, 0}` entries |
| `6` | 6 | multitrack ambience channels |
| `7` | 143 | a **multilayer**: each layer plays a resource under a volume curve of (parameter value, dB) points and, optionally, a pitch curve of (parameter value, pitch ratio) points, each on its own game parameter; e.g. desert wind on parameter `0x0044025C` (0–250) runs from −96 dB at 0 to 0 dB at 250. A layer's resource can itself be a multilayer, which multiplies the two volume curves |
| `2`, `8` | 42, 18 | not identified |

### How a random container picks

:::info[Verified via reverse engineering]
The transform is `FUN_10a559f0` → `FUN_10a55800`, the picker `FUN_10a55f50`, reached from the
resource player `FUN_10a52370` through `FUN_10a56130`. Traced in the Steam `Dunia.dll`.
:::

A random container's weights are Q16.16 fractions, and in every retail container the entry weights
plus word `[8]` sum to 1.0. Equal weights are `floor(1.0 / n)`: three entries weigh `0x5555` each.
Word `[3]` is the byte offset of the entries within the tail, `0` in retail.

On each play the picker draws a number from 0 to 65535:
- **Below `[8]`, nothing plays.** `[8]` is the chance of silence. The bullet pass-by crack
  `0x00448BD1` has 12 entries and `[8]` all at 1/13 (`0x13B1`), so one crack in 13 is silent.
- **Silence is never picked twice in a row**, unless `[9]` is `1` (17 retail containers).
- **Otherwise the weights are walked in order** until the draw falls inside one.
- **The entry that played last is skipped**, unless its own third word is `1` (10 retail entries).
- **Word `[10]` = `1` makes the container a sequence** (274 retail containers). It keeps a position
  per playing object: the first play starts at a random entry, and each later play takes the next
  one, wrapping around. Weights and silence are ignored in this mode.

Every resource kind's word `[5]` is added to the voice's volume in dB, clamped at −96 dB.

A multilayer's tail holds three runs, and every offset counts from the start of the tail:
1. one 28-byte layer per `[7]`: `{child, curve count, curves offset, 0xFFFFFFFF, 0, 0, 0}`;
2. every curve in layer order, 24 bytes each: `{target, parameter, point count, points offset, 0, 0}`;
3. every point, as `{f32 x, f32 y}`.

A curve's target is `0` for volume in dB, or `1` for a pitch ratio (read from the values: `0.9` to
`1.1` across the parameter's range). Kinds `2` and `6` carry tails that are not decoded.

All 7,513 retail banks rebuild byte for byte from these layouts. `jackall-cli spk decode` and
`encode` edit banks through them; see [editing sound banks](../modding/editing-sound-banks.md).

Switch resources key on the same groups as switch events, plus weapon status (`0x004402A7`), vehicle
reliability, footstep speed, infamy, stamina and perspective. A bullet impact is one: `Weapon.Bullet`'s
event `0x004565A3` plays resource `0x004565A4`, a material switch with 36 entries, each a random
container of 2–7 clips.

A multilayer's parameter is answered by the playing object. The game parameters and their ranges are
declared in `7fffffff.bao`. One of them, `0x00440259` (0–250), is used by no entity, bank or XML in
retail.

## The project descriptor

:::info[Verified via reverse engineering and against the retail file]
`FUN_10a54b20` in `Dunia.dll` loads it. The values are read from retail
`common/soundbinary/7fffffff.bao`.
:::

`7fffffff.bao` is one `LargeFixed256` object: DARE's project-wide settings and tables. After the 40-byte
core comes the 256-byte sub-header; offsets below are from its start, file offset `0x28`. The loader
checks the load mode string at `+0x04` (`ATOMIC`; `PACKAGE` is the other mode) and copies these fields:

| Offset | Retail | Meaning |
| --- | --- | --- |
| `+0xAC` | `1` | obstruction drives the per-voice low-pass; `0` would turn it into a volume drop |
| `+0xB0` | `20.0` | low-pass cutoff at full obstruction, Hz |
| `+0xB4` | `3200.0` | low-pass cutoff as obstruction approaches zero, Hz |
| `+0xB8`, `+0xBC` | offset `0`, count `63` | reverb presets, `0x70` bytes each (`EAXREVERBPROPERTIES`) |
| `+0xC0`, `+0xC4` | count `0` | occlusion materials, `0x2C` bytes each **(inferred from the size the band-pass reads)** |
| `+0xC8`, `+0xCC` | count `12` | 4-byte ids, not identified |
| `+0xD0`, `+0xD4` | count `0` | 16-byte records, not identified |
| `+0xD8`, `+0xDC` | count `15` | game-parameter ranges, 12 bytes each |

The table offsets count from the end of the sub-header (file offset `0x128`), and the tables follow one
another to the end of the file. The low-pass curve these values feed is on
[audio runtime](../engine-internals/audio-runtime.md#the-software-filters). Other fields the loader
copies (`+0x84`, `+0x88`, `+0x8C`, `+0x90`, `+0x94`, `+0xA8` and two 16-byte blocks at `+0xE0` and
`+0xF0`) are not identified.

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

That matches the statistics: the word before a preamble's trailing entry resolves to a
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

Since it's a complete, independently-valid audio file, no proprietary decode work is needed beyond
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
| `0x01` | 11 | `0` in every retail stream |
| `0x0C` | 1 | channel-mode flag (`0` = mono, `1` = stereo) |
| `0x0D` | 1 | `0` in every retail stream |
| `0x0E` | 1 | `10` in every retail stream; the header parse never reads it |
| `0x0F` | 1 | `0` in every retail stream |
| `0x10` | 2 | initial predictor, channel A (u16 LE) |
| `0x12` | 1 | initial step-index, channel A (u8) |
| `0x13` | 1 | unidentified/padding |
| `0x14` | 2 | initial predictor, channel B (u16 LE) — meaningful only when stereo |
| `0x16` | 1 | initial step-index, channel B (u8) |
| `0x17` | 5 | unidentified/padding (header total `0x1C` = 28 bytes) |

After the header, the rest of the stream is packed IMA-ADPCM nibbles.

The header parse (at `0x10a7fbae`) reads only the version, the channel-mode flag, and each channel's
initial predictor and step index **(RE-verified)**. It checks the flag against the channel count and
fails with the "Incoherency" error on a mismatch. The constant bytes above are from all 2,885 retail
IMA-ADPCM streams **(seen in data)**.

Retail headers that start on a loud sample are primed: the predictor sits near the clip's first sample
with a large step index (the MAC-10's loop: `22836`/`80` against a first sample of `32767`), so decoding
does not slew up from zero **(seen in data)**. `spk import` primes a sample whose loop flag is set with
the state its own tail ends on, so the restart continues the wave. It starts a one-shot from `0`/`0`.

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

## Playback length comes from the descriptor

An IMA-ADPCM `FlatCopy` record plays for the length its descriptor declares in `TransformedFixed128`
word `[2]`, not for the length of its stream. Replace the audio with a shorter clip and leave `[2]`
alone, and the game plays the clip, then decodes a burst of noise for the rest of the original clip's
duration. Rewrite `[2]`, and `[22]` where it mirrors it, to the new stream's byte length and the clip
ends cleanly: a 0.24 s replacement of the Makarov's 1.32 s first-person shot plays with no crack and
no trailing noise **(heard in game, 2026-09-26)**. That clip was not also played without the rewrite;
the noise is the symptom reported before.

Word `[20]` is not it: patching it to the replacement's real sample count changes nothing. The counter
at `+0x30` in `TImaAdpcm_DecodeStream` (`0x10a7f9e0`) is not a remaining length either, but the
decoder's look-ahead buffer, refilled and drained every call.

`jackall-cli spk import` and `spk encode` derive every audio word of the samples that play the new
stream: `[2]`, the one-shot or loop pair, the rate, the channels and the codec. `spk list` flags any
record whose descriptor disagrees with its stream. Whether the engine reads the loop pair
`[23]`/`[24]` is untested. The App's importer does not rewrite the descriptor: it pads a shorter
IMA-ADPCM clip with silence up to the original's sample count, which keeps the old length true, but a
longer clip or any Ogg replacement ships a stale length. Whether Ogg Vorbis records are also cut at
`[2]` is untested.

## Unknowns

- The four unidentified 40-byte-core fields (`0x08`–`0x14`) — confirmed not a checksum, otherwise
  unidentified. Possibly a secondary id, spatial/priority data, or similar.
- The concrete game-design meaning of each record type (one-shot vs. looping vs. 3D-positioned sounds)
  and most of `TransformedFixed128`'s untabulated sub-header words.
- What separates the playable leaf event types (`1`, and the unseen `5`/`6`/`7`/`9`) from each other.
- Types `2`, `3`, `4` and `10` are only partly traced; see the [event type table](#binary-event-objects).
- Whether the channel-mode byte can represent channel counts above 2 directly, or whether >2-channel
  audio is always built from multiple sub-streams.
