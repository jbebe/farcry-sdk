---
sidebar_position: 4
---

# `.spk` — Sound Bank Format

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against **`Dunia.dll`** (the actual Windows client engine — see the
correction below), following up on the community's one-line note in [Getting
Started](../modding/getting-started.md) that `.spk` filenames are themselves hashes and that
community `.spk` editing exists ("enough to mod them, but not everything" — Gabor). Goal: recover
the on-disk container format well enough to write a real read-only parser/preview
(`tools/JackAll/src/JackAll.Core/Format/SpkPackage.cs`).
:::

## Status: outer container fully confirmed; inner per-record payload still opaque

:::warning["Spk"/"SPK" is an overloaded abbreviation in `Dunia.dll` — three unrelated things]
A string search for `.spk` in `Dunia.dll` turns up hits that have nothing to do with this file
format, traced and ruled out so they don't need re-discovering:

- **`"%s%s\%d.spk"`** — builds paths under `scripts\game\BarkData\` (decimal-numbered, not
  hash-named; there's a sibling `SPBarkData.banklist` string too) — the AI dialogue/**bark** script
  system, an entirely separate subsystem that happens to also use a `.spk` extension.
- **`"SPK%03d\n"`** — a text-line parser (`sscanf("SPK%03d\n", ...)`) for lines like `SPK001` in what
  reads as that same bark-script text format. "SPK" here is short for **"Speak,"** not "sound
  package."
- **`fNearLimitSpkDist` / `fFarLimitSpkDist`** — property-table entries for an in-editor
  **`"SpeakerSet"`** object (alongside `fAudibleDistance`, `fSpeakerDistanceMin/Max`, `fFadeTime`) — a
  placeable sound-emitter entity's distance-falloff tuning. "Spk" here is short for **"Speaker."**

None of these three are the DARE sound-bank container documented on this page.
:::

## A false start: the wrong binary

The first pass of this investigation ran against a binary in the same Ghidra project that turned out
to be **`FarCry2_server`** (the Linux dedicated-server ELF — see [the engine overview](../engine-internals/overview.md)'s
"a second binary" section), not `Dunia.dll`. That binary has much richer surviving symbols (real
demangled `CSoundResource::` C++ names), which made the initial trace easy — but its
`CSoundResource::ClientProcessRawData` (the method that's supposed to interpret the raw file bytes)
is a one-line stub that just `return 1`, doing nothing with the buffer. That's real and correctly
traced, it's just the *server*'s implementation — a headless dedicated server has no reason to
actually decode sound data, only enough of the resource-loading machinery to stay code-path-compatible
with the client. Redirecting the same trace at `Dunia.dll` (the real client engine) found the
non-stub implementation described below.

## Confirmed call chain (`Dunia.dll`, client)

Found by searching for the `"%s%08x.spk"` / `"%s%s\%08x.spk"` format strings (confirms hash-named
files) and walking their xrefs:

1. **`Spk_GetFileNameFromSoundId`** (`0x10624230`) — builds the actual filename from a sound id, either
   `"<bank_dir><id:08x>.spk"` or, when bit `0x40000000` of the id is set, a localized variant
   `"<bank_dir><lang>\<id:08x>.spk"`. Same shape/logic as the server binary's
   `CSoundResource::GetFileNameFromSoundId`.
2. **`Spk_BuildSoundFileNameString`** (`0x106242f0`) — thin wrapper, packages the filename into a
   `CryString`-like object.
3. **`Spk_GetSoundResourceFromId`** (`0x10624b80` — *wait, see correction below; this is
   `0x1062c180`*) — the real resource-fetch entry point (`__thiscall`, `this` = a `CSoundResource`-like
   object): calls `Spk_SoundResourceCtor`, builds the filename, opens it via **`VFS_ResolvePath`**
   (the same hooked resolver already documented in [the archives page](./archives-fat-dat.md) — `.spk`
   goes through the normal VFS path, not the `LevelAsset_OpenStream` bypass), reads the whole file into
   a buffer, then makes a **virtual call** through the resource object's own vtable at offset `+0x54`
   with `(buffer, size)`.
4. **`Spk_SoundResourceCtor`** (`0x106243d0`) — sets the object's vtable pointer to
   `PTR_FUN_10e82e10` right before step 3's virtual call, so that vtable is exactly what resolves the
   `+0x54` slot. Found by using `get_xrefs_from` on `0x10e82e10 + 0x54` (`0x10e82e64`) directly — there
   is no dedicated "read raw memory" tool in this GhidraMCP setup, but `get_xrefs_from` on a data
   address that holds a single pointer reliably reports what it points to, which is exactly what a
   vtable slot is.
5. **`Spk_ParseContainer`** (`0x10624b80`) — the vtable's `+0x54` slot, and the real, non-stub content
   parser. This is the function documented below.

(Correction to numbering above: step 3's address is `0x1062c180`; `0x10624b80` is step 5,
`Spk_ParseContainer`. Kept both addresses here since they were the two load-bearing finds.)

## Confirmed byte layout (`Spk_ParseContainer`, `0x10624b80`)

All fields little-endian. Verified against **every real `.spk` file in a Steam v1.03 install**
(8,282 files, 42,215 records total, zero parse failures) via
`tools/JackAll/src/JackAll.Core/Format/SpkPackage.cs`, and by hand against the smallest real samples
before writing that parser.

```
Header:
  u32   magic  = 0x53504B01     ("KPS" + a version byte, reversed-FourCC — same convention as
                                  .xbg/.xbm's "HSEM"/"MESH", see the XBM/XBG format page)
  u32   count
  u32[count] ids                // one id/hash per record, same order as the records below

Then `count` variable-length records, back-to-back, 4-byte aligned:
  u32   preambleWordCount (N)
  u32[N] preambleWords          // meaning NOT established — see "Not yet traced" below
  u32   size
  u8[size] payload              // registered opaquely, see next section
```

`Spk_ParseContainer`'s own validation (mirrored defensively rather than byte-for-byte in the C# parser,
which does its own bounds checks throughout instead): rejects if `size < 0x10`, if the magic doesn't
match exactly, if `count == 0`, or if the buffer is too small to hold the id table
(`size <= count*4 + 0xC`). The per-record loop reads each record's own `size` field and advances,
re-validating bounds every iteration, so a truncated/corrupt trailing record is caught rather than
walked off the end of the buffer.

## The payload is registered, not decoded, at load time

For each record, after reading `(id, preamble, size, payload)`, the parser calls
**`Spk_CreateSoundObjectFromRecord`** (`0x10a425b0`) with `(id, payloadPointer, size, extra)`. That
function is generic resource-manager machinery (same shape as the server binary's
`CResourceManager::CreateResource`/`GetFromSoundId` pattern) that ultimately calls
**`Spk_InitRecordDescriptor`** (`0x10a3f490`) — a **trivial 4-field setter**:

```c
void Spk_InitRecordDescriptor(void* obj, id, dataPtr, size, extra) {
    obj->id     = id;
    obj->dataPtr = dataPtr;   // still points into the just-loaded file buffer
    obj->size    = size;
    obj->extra   = extra;
}
```

So the per-record payload's own internal structure (observed by hand: a small format code like
`02 1f 00 10`, a length-looking field like `28 00 00 00` = 40, and a 16-byte high-entropy block —
consistent across every sample examined) is **not interpreted here at all**. The engine just keeps a
`{id, pointer, size}` triple and defers actual interpretation to wherever the sound is later triggered
for playback — a different, not-yet-traced part of the call graph. None of the sampled payloads
contain a RIFF/Ogg/Vorbis signature anywhere, so this is very likely playback *parameters*
(volume/pitch/3D falloff, maybe a cross-reference into `sound.dat`/`sound_english.dat`'s own hash
space) rather than embedded audio samples — but that's inference from the byte shape and the absence
of a codec signature, not a traced confirmation.

## Update: the per-record payload's core layout, and why `.spk` never references `.sbao`

Traced the consumer side (the piece explicitly left open above) by following the `"%08x.sbao"` /
`"%08x.bao"` format strings, which led to **`Spk_GetOrLoadSoundObject`** (`0x10a3fb30`) — a function
operating on an object with the exact same field layout `Spk_InitRecordDescriptor` writes
(`+4`=id, `+8`=dataPtr, `+0xc`=size, `+0x10`=extra). It checks the stored `dataPtr`: if non-null, the
descriptor already has inline data and goes straight to `Spk_ResolveSoundObjectData` →
`Spk_ValidateAndDispatchSoundObject`; if null, it falls back to **`Spk_LoadStandaloneSoundFile`**,
which calls `Spk_BuildSbaoOrBaoFileName` (`0x10a3f4b0` — literally `sprintf("%08x.sbao", id)` or
`"%08x.bao"`) and reads that file fresh from disk (see [the `.sbao` format page](./sbao.md)).

**`Spk_ValidateAndDispatchSoundObject`** (`0x10a3f960`) rejects anything under `0x28` (40) bytes
("*Invalid object size: you have probably loaded an old version of the data*") and `memcpy`s exactly
the first 40 bytes into a local struct — **this confirms the `28 00 00 00` field observed at payload
offset +4 in every real sample is a self-declared struct size, not incidental**. That struct is then
passed to **`Spk_DispatchSoundObjectByType`** (`0x10a3f820`), which switches on a `u32` at **struct
offset `+0x20`** — verified byte-for-byte against every record in a real install (all 42,215 records
hit an exact match to one of the 7 known type constants at exactly this offset, and no other offset in
the 40-byte descriptor comes close). The decompile's own field-index arithmetic (`param_1[6]`) pointed
at `+0x18` instead — that number went into the first cut of `SpkPackage.cs` and the app's own preview
showed "unrecognized type" for every real record until this offset was checked against real data and
corrected. The six `u32` fields between `DeclaredSize` (`+0x04`) and the type tag (`+0x20`), and the
one field after it (`+0x24`), aren't individually identified yet:

```
0x50000000  -> rejected outright: "Can't load atomic object id (0x%X) because it's a streamed sound
               data.\n" (FCE_Document_Export error, no handler call)
```

**All six non-streamed handlers were decompiled, and they are not interchangeable "atomic" variants —
each does genuinely different things with the data** (this surfaced when the app's own preview lumped
all six under one generic "atomic" label and that turned out to be uninformative, not just imprecise):

| Type | Handler | Confirmed behavior |
|---|---|---|
| `0x10000000` | `Spk_LoadSimpleFixed68Object` | Fixed 68-byte sub-header, plain copy of the remainder. |
| `0x20000000` | `Spk_LoadTransformedFixed128Object` | Fixed 128-byte sub-header, then a dedicated post-load transform (`Spk_TransformFixed128Payload`) — the only fixed-size type that does more than copy. |
| `0x30000000` | `Spk_LoadFlatCopyObject` | No sub-header at all — the whole remainder is copied verbatim. Simplest of the seven. |
| `0x40000000` | `Spk_LoadLargeFixed256Object` | Fixed 256-byte sub-header, plain copy — the largest fixed-size type. |
| `0x60000000` | `Spk_LoadCountPrefixedListObject` | Tiny (12-byte) allocation, then `Spk_ProcessCountPrefixedList` reads a leading count from the raw data and consumes `count*4 + 4` bytes before the remainder — a count-prefixed list of references, not a single sound. Best guess: a randomized-variation group (single caller, sound-specific, not shared generic code). |
| `0x70000000` | `Spk_LoadSelfReferentialObject` | Plain copy, but the first two fields of the copied data are then read as `{offset, flag}`: if `flag != 0`, `offset` is rewritten in place to an absolute pointer into the copy — an internal self-reference/fixup implying a nested sub-structure. |

This is exactly why real record payload sizes vary so much (40 bytes plain, 108 = 40+68, several KB
for larger/list types) without needing a single universal length-prefix scheme for the part after the
40-byte core — each type's handler knows its own shape.

**This settles whether `.spk` records reference specific `.sbao` files: they don't.** Type
`0x50000000` ("streamed") is the *only* type that ever needs external file data, and
`Spk_ValidateAndDispatchSoundObject`'s own error message says atomic (inline) loading explicitly
cannot handle it — meaning streamed sounds are never packed into an `.spk` bank's records at all. They
exist exclusively as standalone `<id>.sbao`/`<id>.bao` files, loaded by `Spk_LoadStandaloneSoundFile`
using the id directly (`sprintf("%08x.sbao", id)`) — not via any reference stored in a `.spk` file.
`.spk` banks only ever contain the non-streamed (atomic) types. This also explains an earlier
empirical check (`tools/JackAll` scratch tooling, not checked in): real `.spk` record ids and real
`.sbao` file ids overlap at only ~0.01% (noise-level) across the whole install — because they're
mutually exclusive storage paths for the same id-space, not a referencing relationship.

## Update: the preamble words and the `extra` argument, resolved together

Traced `Spk_ParseContainer`'s internal bookkeeping (previously left as unidentified
`piStack_1c`/`FUN_10624ac0` calls) all the way through:

- **`FUN_10624180`** is a tiny refcounted wrapper constructor: `{vtable, refcount=0, dataPtr}`, where
  `dataPtr` is a fresh heap buffer holding a verbatim `memcpy` of the record's own preamble block —
  `(N+1)*4` bytes, i.e. `preambleWordCount` plus all `N` words together.
- **`FUN_10624ac0`** is a find-or-create lookup into an **id-keyed cache** owned by the
  `CSoundResource`-like object — **not** a per-slot language/locale table as previously speculated
  (that guess is superseded by this finding). Each record's own id is looked up in this cache; on a
  miss, the wrapper from the previous bullet is inserted; the slot's refcount is bumped either way.
- The `extra` argument that ultimately reaches `Spk_CreateSoundObjectFromRecord` /
  `Spk_InitRecordDescriptor` is `*(wrapperObj + 8)` — the wrapper's `dataPtr` field itself. So
  **`extra` is a pointer straight to that record's own raw preamble bytes** (count + words),
  reached via this per-id cache rather than passed directly.
- A downstream consumer exists: **`Spk_GetOrLoadSoundObject`** reads the descriptor's `extra` field,
  and if it's non-null *and* the leading count word is nonzero, computes a pointer to the *first
  preamble word* (skipping the count) and returns it as a second out-parameter, alongside the
  resolved sound-object pointer. Both call sites (`FUN_10a3e510`, `FUN_10a3e5a0`) thread this pair
  into a generic variant/tuple builder (`FUN_10a3e420`) feeding the runtime playback/event-dispatch
  system. Tracing stopped there — that machinery is generic engine event dispatch, not anything
  `.spk`-container-specific.

**Net effect**: preamble words are per-record auxiliary data, cached per-id and passed through to
whatever triggers playback as an opaque extra pointer — never decoded or interpreted anywhere in the
container/file-format layer itself. This is consistent with the earlier observation that they "often
echo the record's own id and a second, related id" (e.g. a linked/fallback/variant sound id), but that
specific gameplay meaning is still inference, not confirmed — the actual consumer is generic runtime
dispatch outside the container format's scope.

## Update: standalone `.sbao`/`.bao` share the same VFS resolver as `.spk`

`Spk_LoadStandaloneSoundFile` opens its file through `FUN_10d16690`, which decompiles to a one-line
wrapper: `return VFS_ResolvePath(path, 1, 0) ? handle : -1`. That's the exact same resolver call
`Spk_GetSoundResourceFromId` uses for `.spk` bank loading, and the one already documented as the
hooked loose-file mod-loader resolver on [the archives page](./archives-fat-dat.md). **Confirmed:**
the loose-file override applies identically to `.spk` bank files and to standalone `.sbao`/`.bao`
files — there's no separate, unhooked file path for the standalone case.

## Update: the codec is standard IMA-ADPCM — found by byte search, not string search

The long-standing mystery of what codec the actual audio bytes (`FlatCopy`'s payload, the part with
no `RIFF`/`OggS` signature — see the earlier Ubitunedec side-investigation above) use is resolved.
Not found via GhidraMCP string/name search — those only turn up what's already been named or is
referenced by a nearby string, and this code has neither. Found by pulling the actual `Dunia.dll` off
disk and byte-searching it directly (a small Python script, not Ghidra) for the **canonical
IMA-ADPCM tables** — the standard 16-entry step-index adjustment table
(`-1,-1,-1,-1,2,4,6,8,-1,-1,-1,-1,2,4,6,8`) and the standard 89-entry step-size table
(`7,8,9,...,32767`) that every textbook/reference IMA-ADPCM implementation ships with verbatim.

**Both tables exist byte-for-byte, back to back, in `Dunia.dll`'s `.rdata` section** — stored as
`int32` arrays (not the more common `int16`, which is why an initial 16-bit search came up empty):
index table at `0x10ee3928`, step table immediately after at `0x10ee3968`. (Getting from a raw file
byte-offset to the right Ghidra address needed the actual PE section table — `.rdata`'s
`VirtualAddress`/`PointerToRawData` differ by a constant `0x1400` for this build — rather than
guessing; cross-checked against a known string's own file offset vs. its Ghidra-reported address to
confirm the math before trusting it.)

`get_xrefs_to` on the index table's address turned up three real callers:

- **`FUN_10a85150`** (`0x10a85150`) — a **mono IMA-ADPCM block decoder**. Textbook, byte-for-byte:
  unpack a nibble, `diff = (step * (2*(nibble&7)+1)) >> 3`, negate if bit 3 of the nibble is set, add
  to the predictor and clamp to `[-32768, 32767]`, adjust the step-index via the index table and
  clamp to `[0, 88]` (exactly the valid range for an 89-entry step table), look up the new step, next
  sample.
- **`FUN_10a85240`** (`0x10a85240`) — the same algorithm, but tracking **two independent
  predictor/step-index states** and alternating between them per output nibble — a 2-channel
  "nibbles-separated-by-channel" variant, matching the already-known string `"Only adpcm 4 Bits
  Separate work with sounds having more than 2 channels"`.
**Correction:** an earlier pass through this investigation attributed a third function,
`FUN_10a84980`, to this same decode chain because `get_xrefs_to` on the index table's address also
listed it as a caller. That's wrong — `FUN_10a84980` reads the 4 bytes right *before* the index table
(`0x10ee3920`, which turns out to spell `"OggS\0\0\0\0"`) and is actually a completely unrelated
**Ogg-page-header walker** (its header-region logic maps exactly onto the real Ogg page format —
`page_segments` count at byte 26, segment-length table starting at byte 27, matching the Ogg spec
field-for-field) — presumably serving the already-documented Ogg-Vorbis "long audio" `.sbao` sub-type.
Its apparent read of the index table's address is a coincidental shared `0xFFFFFFFF` constant the
compiler deduplicated, not a real connection. Corrected by finding the *actual* callers of
`FUN_10a85150`/`FUN_10a85240` instead — both are called from a single dispatcher, `FUN_10a853b0`,
which picks stereo vs. mono by a channel flag; and *that* function's only caller,
**`FUN_10a7f9e0`**, is the real `TImaAdpcm` decode/fill method and gives up the entire per-stream
framing at once.

## Update: the per-stream header — found, and empirically verified against real files

`FUN_10a7f9e0` (the `TImaAdpcm` instance method — `param_1` is `this`, matching the constructor found
earlier at `0x10a7f900`) reads a **28-byte (`0x1c`) header once**, at the very start of a stream,
before switching into steady-state decode:

```
offset  size  meaning
0x00    1     version — must be exactly 5 ("TImaAdpcm: IMA-ADPCM version seems to be too old" if not)
0x01    11    unidentified
0x0c    1     channel-mode flag (compared against the object's own channel setting — mismatch throws
              "TImaAdpcm: Incoherency in IMA-ADPCM resource header")
0x0d    3     unidentified
0x10    2     initial predictor, channel A  (u16 LE)
0x12    1     initial step-index, channel A (u8)
0x13    1     unidentified/padding
0x14    2     initial predictor, channel B  (u16 LE) — only meaningful when stereo
0x16    1     initial step-index, channel B (u8)
0x17    5     unidentified/padding (header total is 0x1c = 28 bytes)
```

After the header, the rest of the stream is packed IMA-ADPCM nibbles: **mono** reads one byte at a
time, high nibble then low nibble, each nibble one output sample (`FUN_10a85150`); **stereo** reads
one byte at a time too, but its high nibble is channel A's next sample and its low nibble is channel
B's — i.e. every byte yields one interleaved `(L, R)` frame directly (`FUN_10a85240`), which is
exactly the `"4 Bits Separate"` scheme the earlier-found string names.

**Verified against real data, not just the decompile.** Checked the header layout against both
multi-record sample files already used earlier on this page:

| | `004e1ccc.spk`'s `FlatCopy` (id `0x4e1cba`) | `004e1c52.spk`'s `FlatCopy` (id `0x4e1c50`) |
|---|---|---|
| version byte | `5` ✓ | `5` ✓ |
| channel flag (`+0xc`) | `0` (mono) | `1` (stereo) |
| initial predictor/step, ch. A | `2` / `0` | `61217` / `55` |
| initial predictor/step, ch. B | — | `60065` / `62` |

The stereo/mono split lines up exactly with the earlier statistical finding that `TransformedFixed128`
word `[17]` (guessed as a channel-count field from its correlation with sibling payload size) was `1`
for the small sibling and `2` for the huge one — same two files, same conclusion, reached
independently twice.

Then actually **implemented the decoder** (a plain Python reimplementation of `FUN_10a85150` /
`FUN_10a85240`, not Ghidra) and ran it against both real `FlatCopy` payloads end to end:

- Both decoded with **100% of the post-header bytes consumed, no errors, no bounds issues**.
- Output statistics look like real audio, not noise or a decode gone wrong: values spread across a
  wide, roughly zero-centered dynamic range rather than clipping flat or diverging — mono sample
  (~4,983 encoded bytes → 9,966 samples) ranged ±~18,000; the large stereo sample used the full
  16-bit range.
- Wrote both out as real, playable `.wav` files (sample rate taken from each `FlatCopy`'s sibling
  `TransformedFixed128` record's own `word[19]`, both `44100` Hz here) — sitting in `tmp/` as
  `004e1cba.wav` (mono, ~0.23s) and `004e1c50.wav` (stereo, ~60.6s).

This closes the loop opened all the way back at "when will we be able to play `.spk` files as audio":
**the codec, the per-stream header, and a working reference decoder are all now confirmed against
real retail data.** Remaining gaps are narrow and don't block basic playback: the unidentified header
bytes (`0x01`-`0x0b`, `0x0d`-`0x0f`, `0x17`-`0x1b` — possibly a total-sample-count field among them,
not yet needed since decoding just runs until the input bytes are exhausted), and whether the
channel-mode byte at `+0x0c` is strictly boolean or a general channel count (the
`"Adpcm allows only sound files with 1, 2, 4 and 6 channels"` string implies DARE supports more than
2 channels somewhere, presumably via multiple mono/stereo sub-streams rather than a single stream with
a channel count above 2 — not verified against a real >2-channel sample, none seen in the corpus scan
above).

## Update: three hand-picked real samples crack most of the 40-byte core and both fixed sub-headers

Byte-level analysis (not GhidraMCP — a small standalone Python parser written against the confirmed
layout above) of three real `.spk` files of very different sizes (132 B, 5.4 KB, 2.67 MB — the last
one dominated by a single multi-megabyte record), picked specifically to get more than one data point
per type. Two of the three are multi-record files, and both turned out to contain the exact same
pattern: a `0x30000000` (FlatCopy) record holding the actual bulk payload, immediately followed by a
`0x20000000` (TransformedFixed128) record, immediately followed by a `0x10000000` (SimpleFixed68)
record — i.e. **one "sound event" is commonly stored as a raw/full representation plus two smaller
fixed-size derived representations, grouped into one bank**. The third file is a single standalone
`SimpleFixed68` record. Only two data points per type is thin, so treat anything below marked
"consistent with" as a lead, not a closed case — but the cross-reference chain and the sample-rate
field are exact numeric matches against real ids/values, not pattern-matching guesses.

**The 40-byte core, all 7 records checked:** the two fields at `+0x18`/`+0x1C` were `0` in *every*
record (previously lumped into the "six unidentified fields" as unknown); the field at `+0x24`
(after the type tag) was `0x2` in *every* record. Four fields remain genuinely unidentified
(`+0x08`/`+0x0C`/`+0x10`/`+0x14` — high-entropy, differ per record, plausibly a checksum/hash pair).

**`SimpleFixed68`'s 68-byte sub-header, word-indexed (`u32[17]`, offsets relative to sub-header
start):**

| Word | Offset | Standalone sample | Sibling sample #1 | Sibling sample #2 |
|---|---|---|---|---|
| `[0]` | `+0x00` | own id (`0x45533a`) | own id (`0x4e1ccc`) | own id (`0x4e1c52`) |
| `[1]` | `+0x04` | `1` | `1` | `1` |
| `[2]` | `+0x08` | `0x448dde` | **sibling `TransformedFixed128` record's own id** (`0x4e1cc3`) | **sibling `TransformedFixed128` record's own id** (`0x4e1c51`) |
| `[4]` | `+0x10` | `0x00010000` | `0x00010000` | `0x00010000` |
| `[7]` | `+0x1c` | `0x456a81` | `0xffffffff` (sentinel "none") | `0xffffffff` |
| `[9]` | `+0x24` | `100` | `-100` (`0xffffff9c`) | `-100` |
| `[16]` | `+0x40` | `1` | `1` | `1` |

(all other words were `0` in all three samples). **Confirmed, not inferred:** word `[2]` is a direct,
byte-exact cross-reference to the sibling `TransformedFixed128` record's id when one exists in the
same bank — this is a real id equality check against the actual sibling record, not a guess. Word
`[4]` being a constant `0x00010000` in all three looks like a `Q16.16` fixed-point `1.0` (an identity
gain/scale default). Word `[9]`'s magnitude-100-but-opposite-sign pattern (one file `+100`, two files
`-100`) is suggestive of a signed parameter (attenuation/priority/distance in some unit of 100), but
that's a guess from three points, not a confirmed field. Word `[7]`'s `0xffffffff` in the two
sibling-having samples vs. a real id-shaped value in the standalone sample suggests it's a second,
independent link slot that defaults to "none" — plausibly filled in when a record has no
`TransformedFixed128` sibling to point `[2]` at instead.

**`TransformedFixed128`'s 128-byte sub-header, word-indexed (`u32[32]`), the two sibling samples:**

| Word | Offset | Sample #1 (small, ~5 KB sibling) | Sample #2 (huge, 2.67 MB sibling) |
|---|---|---|---|
| `[0]` | `+0x00` | own id (`0x4e1cc3`) | own id (`0x4e1c51`) |
| `[1]` | `+0x04` | `1` | `1` |
| `[5]` | `+0x14` | `0xfff40000` = **`-12.0`** in `Q16.16` | `0xfff80000` = **`-8.0`** in `Q16.16` |
| `[7]` | `+0x1c` | **sibling `FlatCopy` record's own id** (`0x4e1cba`) | **sibling `FlatCopy` record's own id** (`0x4e1c50`) |
| `[17]` | `+0x44` | `1` | `2` |
| `[19]` | `+0x4c` | `44100` | `44100` |
| `[20]` | `+0x50` | `22240` | `44100` (equals `[19]`) |
| `[25]` | `+0x64` | `3` | `3` |
| `[28]` | `+0x70` | `7` | `7` |
| `[31]` | `+0x7c` | `0xffffffff` | `0xffffffff` |

**Confirmed:** word `[7]` is a direct, byte-exact cross-reference to the sibling `FlatCopy` record's
id — so the three-record group is explicitly linked in both directions
(`Simple68[2] → Transformed128`'s own id, `Transformed128[7] → FlatCopy`'s own id), not just
implied by file ordering. Word `[19]` = `44100` in both samples is essentially certainly a **sample
rate** field. **Plausible, not confirmed:** word `[20]` differing from `[19]` only in sample #1
(`22240` vs. `44100`) while equaling it in sample #2 reads like a resample target rate — consistent
with the type's own name ("Transformed") and the earlier finding that this is the one fixed-size type
with a real post-load transform function (`Spk_TransformFixed128Payload`); word `[5]`'s negative
`Q16.16` value in both samples (matching the sign/scale of `SimpleFixed68`'s word `[4]`) is
consistent with a dB-like gain adjustment applied during that transform; word `[17]` (`1` vs. `2`)
lines up with the size of the paired `FlatCopy` payload (~5 KB vs. 2.67 MB) closely enough to guess
channel count, and would explain why sample #1 and #2's matching-value fields land two words apart
(`[22]`/`[24]`) — a mono/stereo-dependent header shift — but this is inference from two samples, not
verified against decompiled code.

**Preamble words, revisited with real data:** in the single-record file, the one preamble word is the
record's own id. In *both* multi-record files, every record in the file carries the *identical*
preamble list, and its last word is always this bank's own trailing/self id (i.e. the id the filename
is hashed from) — consistent with the earlier "echoes the record's own id" observation, now seen to
hold across every record in a bank, not just some. The one file with a 2-word preamble has a second
word (`0x4e1c53`) that is **not** any id present in that file — numerically the very next id after the
bank's own last id (`0x4e1c52` → `0x4e1c53`), suggesting the extra preamble word(s) may reference an
adjacent/related bank rather than anything internal. Consistent with, not proof of, the standing guess
that preamble words are linked/fallback/variant sound ids.

## Update: full-corpus statistical validation (all 8,282 real `.spk` files / 42,215 records)

The three-sample pass above was always going to be small-n; re-ran the same style of analysis (a
standalone Python parser, not GhidraMCP) against every real `.spk` file in the install — 8,282
files, 42,215 records, zero parse failures, matching the outer-container validation figure already
cited earlier in this page. This **confirms some of the small-sample leads outright, and corrects
others** — noted explicitly below so the record stays honest about which is which.

**Now fully confirmed (100% across all 42,215 records, no exceptions):**
- `DeclaredSize` (core `+0x04`) is `40` in literally every record — it's a hardcoded companion
  constant, not a field that ever varies with anything.
- Core `+0x18` and `+0x1C` are `0` in every record.
- Core `+0x24` is `0x2` in every record.

**Ruled out:** the four still-unidentified core fields (`+0x08`/`+0x0C`/`+0x10`/`+0x14`) are not a
CRC32 or Adler32 checksum of the record's own payload bytes — zero matches across all 42,215 records
against either algorithm. Doesn't say what they are, but it's one less hypothesis.

**Type distribution:** `TransformedFixed128` 39% (16,519), `SimpleFixed68` 34% (14,230), `FlatCopy`
27% (11,370), `SelfReferential` 0.2% (96). `CountPrefixedList`, `LargeFixed256`, and `Streamed` never
occur in a single real `.spk` record anywhere in this install.

**Corrected — the "triplet" pattern:** strict "last 3 records are `FlatCopy`, `TransformedFixed128`,
`SimpleFixed68` in that exact order" only holds for 41% of files with ≥3 records (784/1,902). But
among the 1,424 files whose record count is itself a multiple of 3, 80% (1,146) contain an *exactly
equal* count of all three types. The real pattern is "banks tend to hold matched sets of the three
representations" — not "always three consecutive records in a fixed order." (Also notable in the
raw per-file record-count histogram: 3, 6, 9, 12, 18, 24, and 39 are all disproportionately common
counts, reinforcing that multiples of 3 are structurally meaningful.)

**Corrected — `SimpleFixed68` sub-header:**
- Word `[9]`: `0` in 90% of records; when nonzero, it is *always* exactly `+100` or `-100`
  (`773`×`-100`, `611`×`+100`, nothing else) — a clean discrete/binary-signed flag, not a continuous
  parameter as the three-sample pass's "attenuation-ish" framing implied.
- Word `[16]`: genuinely boolean — `0` in 84%, `1` in 16% (the three-sample pass happened to hit `1`
  every time, which was misleading).
- Word `[1]`: `1` in 98%, but also `2`/`4`/`8` (clean powers of two) in the remainder — a possible
  variant/voice-count field, not previously flagged.
- Word `[2]` ("sibling `TransformedFixed128` id" in the three-sample pass): resolves to *some* real id
  in the corpus 99.9% of the time, and to a record in the *same file* 79% of the time — but matches
  the immediately-preceding record specifically only 9.2% of the time. So it's a same-bank id
  reference far more often than not, but "always the paired `TransformedFixed128` sibling" doesn't
  generalize — it isn't tied to file position or type the way the two samples suggested.
- Word `[7]` ("sentinel-when-no-sibling" in the three-sample pass): the `0xffffffff` sentinel appears
  in only 14% of records — a minority, not the fallback case. The non-sentinel values overwhelmingly
  cluster on a small handful of ids reused thousands of times each — one single id (`0x004ee7f0`)
  accounts for 4,081 of 14,160 records (29%) on its own; the next few most common account for another
  large chunk. That reads much more like a shared category/template/default-profile reference than a
  per-record link, and reframes the earlier "second independent link slot" guess.

**Corrected — `TransformedFixed128` sub-header:**
- Word `[7]` ("sibling `FlatCopy` id" in the three-sample pass): holds up *better* than `SimpleFixed68`'s
  word `[2]` did, but still isn't universal — 59% match the positionally-preceding record, 72% match
  some id in the same file, meaning ~28% reference something outside the same bank entirely (or
  nothing recognizable).
- Word `[9]` (not previously tabulated): genuinely boolean, `1` in 97%, `0` in 3%.
- Word `[19]` (sample rate): confirmed at scale — every value observed is a standard real-world audio
  rate. Distribution is dominated by `32000` (44%) and `22050` (42%), then `48000` (10%); `44100` — the
  value both three-sample-pass records happened to show — is actually the least common of the major
  rates at just 3%. Small-n luck, not representative.
- Word `[20]` ("resample target rate" guess in the three-sample pass): **corrected, not just
  refined** — at scale its values are irregular (`24132`, `24085`, `16009`, `12009`, `9889`, ...),
  nothing like a standard sample rate, and it equals word `[19]` in only 0.1% of records (19/15,719),
  not the "matches when mono" pattern the second sample suggested. Reads much more like a decoded
  sample/frame count or output buffer size than a second rate field.
- Word `[17]` (channel-count guess): correlates with the sibling `FlatCopy`'s size as predicted —
  records with `word[17]=2` (307 of 15,719, i.e. rare) have an ~11× larger average sibling payload
  (277 KB vs. 24.5 KB) than `word[17]=1`. Consistent with a channel-count field (stereo data runs
  roughly 2×+ larger for the same duration), though this is still correlation, not a decompiled
  confirmation.

**Corrected — preamble's extra word:** across every record with `N≥2` preamble words (37,130 of
them), the word immediately before the trailing self-id resolves to *some* real id in the corpus
98.3% of the time — strong confirmation these are genuine cross-references to other sound resources,
not noise. But only 30% of the time does it match another bank's own trailing id, and only 8.8% of
the time is it a simple `±1` numeric neighbor of this bank's own id — so the single 2-word sample's
"points at the very next sequential bank" reading was a coincidence, not the general rule. It
typically points at some other, generally unrelated-by-numbering id elsewhere in the corpus.

## Not yet traced / open questions

- **The game-design meaning of the six non-streamed types** — e.g. which is "simple one-shot" vs
  "looping" vs "3D-positioned", etc. All six handlers are now decompiled and structurally
  distinguished (see the table above), and the corpus-wide pass above gives several `SimpleFixed68`
  and `TransformedFixed128` fields solid statistical characterization, but the remaining four core
  fields (`+0x08`/`+0x0C`/`+0x10`/`+0x14`), most of `TransformedFixed128`'s untabulated words, and the
  concrete gameplay meaning behind the correlations found (e.g. what word `[7]`'s handful of shared
  `SimpleFixed68` "category" ids actually represent) are still unidentified.
- **`FlatCopy`'s payload contents** — confirmed to hold the actual bulk audio-like data (up to 2.67 MB
  observed), still with no recognizable codec signature; not re-examined at corpus scale in this pass.

## A side investigation: does Ubitunedec know more than we do?

`tools/third-party/Ubitunedec` (`ldeon/Ubitunedec`, aka `DecUbiSnd`) is a third-party decoder for an
**older generation** of Ubisoft's audio middleware ("Ubi Sound Tools" — the ADPCM/interleaved/6-or-4-bit
chunk formats used in titles like *XIII*, *Splinter Cell* (PC), and *Rainbow Six*), identified by a
one-byte version tag (`2`/`3`/`5`/`6`/`7`/`8`/`9`) at the very start of a stream, or a plain `OggS`
signature. Worth checking whether its codec knowledge extends any further into `.spk`'s still-opaque
per-record payload (the fields listed above as not yet identified) or its "not yet traced" sub-header
contents.

**It doesn't — verified by actually running the tool, not just reading its source:**

- `UbitunedecCMD.exe -S` (`--scan`, which walks an entire buffer looking for a recognized chunk
  starting anywhere, not just at offset 0) finds **zero** matches in any of the six real `.spk`
  fixtures in `Fixtures/Spk`, and zero in the one real short-SFX `.sbao` sample
  (`tools/misc/format-samples/004ae237.sbao`, see [the `.sbao` page](./sbao.md) for why that sample is
  relevant here).
- Forcing each structural decoder directly (`--input-type ubi_v3`/`v5`/`v6`/`iv2`/`6or4`) against that
  same `.sbao` sample, every one **rejects it** on its own signature check (e.g. "File does not have
  the correct signature (should be 03, 05, or 06)").
- Sanity check that the tool and invocation are actually working: the same `-S` scan correctly finds
  the real `OggS` chunk at byte offset 40 in a known Ogg-backed `.sbao` fixture — exactly matching this
  page's and [the `.sbao` page](./sbao.md)'s independently-confirmed 40-byte header size.

This makes sense in hindsight: `.spk`'s 40-byte record core (`02 1F 00 10` magic, declared-size field,
type tag, the same shape `.sbao` shares) is a **DARE** structure (Far Cry 2's actual audio middleware,
per `Data_Win32/SoundBinary/DARE.INI`) — a different, later engine generation than the one Ubitunedec
targets, not a variant of it. Ubitunedec's only genuine overlap with anything in this codebase is
decoding the Ogg Vorbis bitstream inside music/dialogue `.sbao` files — and that case is already fully
handled by `SbaoAudio`/`SbaoFileHandler` (split/combine/Vorbis-ID/playback/export/import), independently
of Ubitunedec. No change was made to `SpkPackage.cs`/`SpkFileHandler` as a result of this investigation.
