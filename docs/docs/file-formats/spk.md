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

## Not yet traced / open questions

- **The game-design meaning of the six non-streamed types** — e.g. which is "simple one-shot" vs
  "looping" vs "3D-positioned", etc. All six handlers are now decompiled and structurally
  distinguished (see the table above), but *what the sub-header fields inside each mean* (beyond
  `0x60000000`'s leading count and `0x70000000`'s offset/flag pair) isn't traced.
- **The six `u32` fields between `DeclaredSize` and the type tag** (struct offsets `+8`..`+0x1F`) —
  previously mis-described as a "16-byte high-entropy block" (that guess only looked at the first 16
  of these bytes); now known to span 24 bytes / six real fields, none yet individually identified
  (could include a checksum, a secondary id, spatial/priority data, etc.). Also one more field after
  the type tag, at `+0x24`.
- **The preamble words' exact meaning** — `N` varies per record (seen 1, 2, and 3 in real files); the
  words themselves were observed to often echo the record's own id and a second, related id, but this
  wasn't traced to a consumer.
- **`Spk_GetSoundResourceFromId`'s `extra` argument** (the 4th field in `Spk_InitRecordDescriptor`,
  sourced from `*(undefined4*)(piVar8[1] + 8)` in the decompile) — looked like it might be a per-slot
  language/locale pointer from a hashmap-like structure (`Spk_ParseContainer`'s internal
  `piStack_1c`/`FUN_10624ac0` bookkeeping), not confirmed.
- Whether `VFS_ResolvePath` (step 3, main call chain above) means the loose-file mod-loader hook
  already documented in [the archives page](./archives-fat-dat.md) can already override `.spk` (and,
  per this update, standalone `.sbao`/`.bao`) files today — plausible given it's the same resolver, not
  independently verified for these specific file types.
