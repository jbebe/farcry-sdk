---
sidebar_position: 1
---

# `.fat`/`.dat` — Archives and the Asset Resolver

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against the Steam v1.03 build of `Dunia.dll`. For the practical, tool-level
side of unpacking/repacking these archives, see [Getting Started](../modding/getting-started.md); this
page covers the engine's own internals — how a relative asset path becomes bytes, and where a
loose-file mod loader can hook in.
:::

Far Cry 2 ships its assets packed into `.dat`/`.fat` pairs (`common.dat`, `patch.dat`, `worlds.dat`,
`sound.dat`, ...). Every asset request — a config, a script, a `.fcb`, a sound bank — funnels through
one generic resolver that decides whether to serve it from a packed archive or straight off disk.

## Three lazily-constructed singletons

Built together, on first use, by `FUN_10cf3e40` (each behind its own `if (DAT_xxx == 0)` guard) and
torn down together by `FUN_10cf3de0`:

| Global | Built by | Role |
|---|---|---|
| `DAT_116793b0` | `FUN_10ceffa0` | **Archive list** — fixed-size table of every top-level `.dat` archive the engine knows about (below). |
| `DAT_10ff0e94` | `FUN_10229700` | **Path manager** — resolves the install directory, builds `<install>\Data_Win32\`/`<install>\data\` root strings. Also reachable as `GetExePath` (`0x10002220`) / `FCE_Engine_GetPersonalPath` (`0x10893d10`) — consistent with the [save-data path](../engine-internals/save-data-path.md) notes. |
| `DAT_10ff0ef8` | `FUN_10235d90` | **VFS resolver** — turns a relative path into an open file handle. See "The generic resolver" below. Confirmed via a call site: `MOV ECX, dword ptr [0x10ff0ef8]` immediately precedes `CALL 0x102358a0`. |

An alternate, apparently editor-side bootstrap path (`FUN_104cfb60`) constructs an equivalent
archive-list object directly, with a different vtable (`PTR_FUN_10e69dac` vs. the base
`PTR_FUN_10f485d4`) — two call sites building what's semantically the same singleton, one via a
derived class; not load-bearing for the resolver below.

## The archive list

A fixed table of 42 slots, 132 bytes each: `char name[0x78]`, the mounted archive at `+0x78`, a flags
word at `+0x7C` and a priority at `+0x80`. The constructor (`0x10cb7180` in the GOG `Dunia.dll`) fills
the first ten **(RE-verified)**:

| Slot | Archive | Flags | Priority |
|---|---|---|---|
| 0 | `patch.dat` | `0x2B` | 1 |
| 1 | `common.dat` | `0x3F` | `0x7FFFFFFF` |
| 2 | — | 0 | 0 |
| 3 | `sound.dat` | `0x2E` | `0x7FFFFFFF` |
| 4 | `sound_%lang%.dat` | `0x2E` | `0x7FFFFFFF` |
| 5 | `soundcache.dat` | `0x28` | `0x7FFFFFFF` |
| 6, 7 | named later | `0x2D` | `0x7FFFFFFF` |
| 8 | named later | `0x24` | `0x7FFFFFFF` |
| 9 | `shadersobj.dat` | `0x0E` | `0x7FFFFFFF` |
| 10–41 | — | 0 | `0x7FFFFFFF` |

The flags are not ids: `0x24` is slot 8, and `shadersobj.dat` carries `0x0E`. A separate dynamic vector
is then populated with six more names: `worlds/tmpla/tmpla.dat`, `worlds/world1/world1.dat`,
`worlds/world2/world2.dat`, `worlds/multicommon/multicommon.dat`, and the `_%lang%` variant of each.
These are pre-merge components of what ships as the single `worlds.dat`/`worlds.fat` pair.

**Search order is priority order.** Registering an archive (`0x10233420`, GOG) inserts it after every
archive whose priority is at or below its own, so a lower number is searched first and equal priorities
keep mount order **(RE-verified)**. `patch.dat` at 1 is searched before everything; the stock archives
share `0x7FFFFFFF`.

**DLC archives** come from each DLC folder's `toc.rml`. Each game mode lists `BigFile` names there, and
the engine mounts `<name>.dat` — plus `<name>_<language>.dat` when that exists — into a free slot from 12
upward with flags `0x29` and priority 3, or `0x69` and priority 2 when the entry asks for it (`0x102a5200`
and `0x10cb6e00`, GOG, **RE-verified**). Either way a mounted DLC archive is searched after `patch.dat`
and before every stock archive. The game mode decides what mounts: the campaign (`FCXSingle`) mounts
only `dlc1`'s `entitylibrary` and `dominos`; `dlc1.dat`, `dlc_jungle.dat` and both `menus` archives are
listed only for multiplayer modes and the main menu **(seen in data)**. On a DLC1 install those two
campaign archives shadow 2 `common.dat` entries (the DLC weapon graph scripts) and 11 `worlds.dat`
entries.

**Per-archive open** (`FUN_102a6c40`, indexed by slot, stride `0x84`): opens the underlying `.dat`
through `FUN_102358a0` with mode `8` — `GENERIC_READ` with `FILE_FLAG_NO_BUFFERING`, so archive reads
bypass the Windows file cache (`0x1022f1c0`, GOG, **RE-verified**) — and, if buffering is enabled for that
slot, wraps it in an async double-buffered reader (`FUN_1022a220`, 64KB buffer) backed by a
`CreateFileA`-descended I/O ring (`FUN_1023c5b0`).

A `.fat` opens with the magic `FAT2`, version `5`, a flags word and the entry count, then 16-byte
entries sorted by hash, then a localisation count. The flags word is `0x0301` — platform 1 (PC) in
byte 0, compression version 3 in byte 1 — on every compressed archive, and `0` on `sound.fat`,
`sound_english.fat` and `shadersobj.fat` **(seen in data)**.

### How a path becomes a key

`CPathID::SetContent` (`0x09c8fe50` in the Linux server, **RE-verified**) turns a path into the key:

1. `CFileManager::FormatPath` lowercases it, turns every run of `/` and `\` into one `\`, and drops a
   single leading `\` — but keeps a leading `\\`, and keeps a leading `/` as `\`.
2. If the extension — everything from the **first** dot of the last component — is a source extension
   in `config/resourceconfig.xml`, it is replaced by the cooked one: `.dds`/`.png` → `.xbt`, `.glm` →
   `.xbg`, `.mlm` → `.xbm`, `.mac` → `.mab`, `.skel.xml` → `.skeleton`, `.fxa`/`.fxe` → `.lfa`/`.lfe`,
   `.hkr` → `.hkx`, `.rta` → `.rtx`, `.frank` → `.apm`, `.gsdat` → `.sdat`, `.ai.xml` → `.ai.rml`.
3. The result is CRC32-hashed; an empty path gives `0xFFFFFFFF`.

So a reference to `ui/textures/common/hardcore.dds` finds the entry `ui\textures\common\hardcore.xbt`
(`0xD580E9A0`), and `foo.v2.dds` is not swapped at all. A second constructor form skips step 2.

There is no `.fat` string literal anywhere in the binary — archive entries are indexed by CRC32 hash,
not filename. The engine only opens the literal `X.dat` file by name; the paired `X.fat` index loads as
a private in-memory hash table, an implicit sibling-file convention rather than a runtime string
operation.

## The generic resolver — `FUN_102358a0`

**Every asset load funnels through this function**, both the per-archive open above and high-level
format loaders (confirmed caller: `FUN_102340f0`, the FCB-loader trampoline, mode `0x21`).

```
FUN_102358a0(DAT_10ff0ef8 /*this*/, char *relativePath, uint modeFlags, char forceFlag)
```

Control flow:

1. **`modeFlags & 0x20`** — recursive re-entry with adjusted flags, gated behind a cache-generation
   check (`DAT_10ff0f14`) — not asset-relevant.
2. **`modeFlags & 0x40`** — opens via mode `2`, then wraps the result in a 4MB buffered reader
   (`FUN_1024b0a0`) — a "load whole file into memory" fast path.
3. **Main path** (mode `8`/`0x21`, the ones real asset loaders use):
   - Virtual provider-gate call `(**(code**)**(param_1+4))(path, mode)` — if false, resolution fails.
     A pluggable veto hook already exists here; no non-trivial implementation found.
   - `FUN_10231510(path)` checks whether `path` is already absolute (`:` or leading `\\`) — the crux
     of the hook design below.
   - If relative and the resolver has a non-empty search-path list (`*(param_1+0xc) != 0`): hashes
     `path` and walks the search-path list in priority order, first match wins (`FUN_10249070` per
     entry). Later entries and the raw-disk fallback are never reached once something matches.
   - If nothing matched (or the path was relative with an empty search-path list): prefixes it with
     the path manager's root into a 260-byte (`MAX_PATH`) stack buffer.
   - If the path was absolute (or after the fallback prefix): copies it into the same stack buffer and
     calls `FUN_10231ae0(path, modeFlags)` — maps `modeFlags` onto Win32 `CreateFileW` flags and opens
     it directly. **This is the raw filesystem escape hatch, already reachable for any absolute path
     with zero engine modification.**

**Per-search-path-entry lookup** (`FUN_10249070`): binary-searches (`FUN_10248870`) a sorted array of
16-byte `{hash, offset, size, ...}` records for the path's hash; on a hit, opens a sub-stream at the
recorded offset within that entry's already-open `.dat` handle (`FUN_102487d0`). This is the `.fat`
index's real in-memory shape: a sorted, CRC32-keyed offset/size table, one per mounted archive, each
wrapped as one "search-path entry" — i.e. **each mounted `.fat`/`.dat` pair is structurally one link in
an ordered override chain**, the same shape as a Bethesda BSA load order or a STALKER `gamedata` search
path. Retail just never populates that list with anything but packed archives.

This confirms the archive search order behind the
[`gamemodesconfig.xml`-in-two-archives gotcha](../modding/gotchas.md): `patch.dat` > mounted DLC
archives > `common.dat` > `sound*.dat`/`soundcache.dat`/`shadersobj.dat` > `worlds/*.dat`, first match
wins — `common.dat` is checked before any `worlds/*.dat`, so its copy of a colliding hash wins over
`World.dat`'s.

## Existing override precedent in the engine

`FUN_1065b130` (world entity-library load) always loads the base `entitylibrary.fcb` (or
`entitylibrary_full.fcb`, chosen by a flag) via the resolver above, and unconditionally also attempts
to load `generated\EntityLibraryPatchOverride.fcb` through the same resolver. If that second load
succeeds, the result is merged in via `FUN_10549560`. This is a hardcoded single-file special case, but
it proves "load base, conditionally load+merge an override, don't error if absent" is native to the
engine's own design.

A second confirmation, traced in the more heavily-symbolized `FarCry2_server` binary:
`CXGame::LoadArchetypes` (`0x08888750`) builds `<world's generated dir>\entitylibrary.fcb` and loads it
via `CEntityLibraryManager::ReadFromXML`, then calls `CDlcService::GetEntityLibraries()` and merges each
installed DLC's own entity library on top via `CEntityLibraryManager::Override(...)`. This confirms the
DLC weapon-override behavior noted in [Gotchas](../modding/gotchas.md): DLC entity data loads after the
main patch and wins via a real, named `Override` call, not just an inferred load-order effect.

This binary's strings contain no reference anywhere to `entitylibrary_full.fcb` — every path it builds
is the plain name — suggesting the `_full` split (seen only in the Windows-side loader) is client-only
(presentation/render-layer entity data a dedicated server has no use for). Not confirmed by an actual
content diff of matched pairs for the same world.

## Entry decompression: schemes 0/1/2

`ArchiveEntry_OpenAtOffset` (`0x102487d0`) calls `ArchiveEntry_Decompress` (`0x102486d0`) for any entry
whose uncompressed-size bits are non-zero — a 3-way dispatch on a 2-bit scheme value:

| Scheme | Handler | Status |
|---|---|---|
| 0 | `0x10258c50` | LZMA: a 7-bit varint, five LZMA property bytes, then the range-coded stream (`0x10256380` → `LZMA_DecompressInPlace` `0x1025a850` in the GOG build, **RE-verified**). Unreachable in practice — real `Compression=None` entries always carry `UncompressedSize=0`, so they never reach the dispatcher. |
| 1 | `ArchiveEntry_DecompressLzo1x` (`0x10258d60`) → `Lzo1x_Decompress` (`0x1025a620`) | Confirmed LZO1X — matches JackAll's `Lzo1x.cs` state machine constant-for-constant. |
| 2 | `ArchiveEntry_DecompressZlib` (`0x10258d00`) → `Zlib_DecompressChunked` (`0x1025d1c0`) → `Zlib_InflateRawBlock` (`0x1025d110`) | Confirmed real zlib (raw DEFLATE, `windowBits=-15`), unrelated to the separate Quazal-networking zlib instance elsewhere in the binary. |

### Cross-reference: the same dispatch, named, in `FarCry2_server`

`FarCry2_server` (see [Overview](../engine-internals/overview.md) for why it's the better-symbolized
binary of the two) names the archive class: real strings confirm the container class is
**`CCryArchive`**, with a `CFatItem` (`FatLessThanCrc`/`FatLessThanSeekPos`-sorted, matching the
CRC32-keyed `.fat` layout above) and a `CNfoItem` (the plaintext `.nfo` sidecar manifest — see the
community-reported note in [Getting Started](../modding/getting-started.md)), managed by
`CCryArchiveManager`.

`CCryArchive::Decompress(IFile*, uint size, uint scheme)` (`0x09c70c00`) is the same 3-way scheme
dispatch, and it names real codecs for all three branches: **`scheme == 0` → `LZMA_DecompressInPlace`**,
`scheme == 1` → `LZO_DecompressInPlace` (matches scheme 1 above), `scheme == 2` →
`EdgeZlib_DecompressInPlace` (Sony's SPU-accelerated, bitstream-compatible reimplementation of zlib —
the same DEFLATE bitstream as scheme 2 above, a different concrete library backing it, plausible given
this source tree is shared across platforms). `Dunia.dll` agrees: its scheme-0 branch is LZMA too (see
the table above), and the actual "uncompressed" case is signaled by `UncompressedSize=0` and
short-circuits before this dispatch ever runs.

Every shipped FC2 archive (~215k entries scanned via JackAll) uses only schemes 0 and 1 — scheme 2
never appears in real data, so it is known from disassembly alone.

**Scheme 2 is not a plain raw-deflate stream over the whole entry.** `Zlib_DecompressChunked` wraps it
in a bespoke container: a header gives a block count and fixed block size (rounded to a multiple of 16,
capped at `0x10000`), and each block carries its own 16-bit length prefix (`0` = stored, verbatim;
otherwise raw-DEFLATE), with the cursor padded to stay 16-byte aligned between blocks.
`System.IO.Compression.DeflateStream`/`ZLibStream` cannot decode this directly — `ZLibStream`
additionally expects a zlib header and Adler32 trailer that don't exist here. JackAll's
`DuniaArchive.cs` calls `ZLibStream` and is wrong on both counts, which is harmless only because no
shipped data exercises this path. A conforming encoder for scheme 2 means
reproducing this exact chunk container, not calling into `System.IO.Compression`.

## Recommended hook point for a loose-file mod loader

The lowest-risk, highest-leverage hook is a trampoline detour on `FUN_102358a0` itself
(`0x102358a0`, `__thiscall`, `this = DAT_10ff0ef8`):

1. On entry, for a plain read-style open (`8`/`0x21` — not the `0x20`/`0x40` recursive-wrapper calls),
   normalize `relativePath` and check `GetFileAttributesW` against a candidate loose path, e.g.
   `<install>\Data_Win32\LooseMods\<relativePath>` (or a priority-ordered list of mod folders,
   mirroring the archive override-chain idea at the hook layer).
2. If a loose file exists, rewrite the path argument to that absolute candidate path before calling
   through. `FUN_10231510`'s absolute-path check already routes any absolute path straight to
   `FUN_10231ae0` → `CreateFileW`, completely bypassing the archive search-path/hash-lookup loop — no
   need to fabricate a fake search-path entry or understand the FAT hash-table format at all.
3. If no loose file exists, call through unmodified.

This mirrors what the engine already does for a plain absolute path, so it needs no understanding of
the binary-search/hash internals and no risk of corrupting the in-memory FAT tables.

**Constraint**: the absolute-path branch copies into a fixed 260-byte (`MAX_PATH`) stack buffer
(`acStack_104`) with no bounds check beyond that size. A loose-mod root nested deep inside a long Steam
library path plus a long relative asset path could realistically overflow it — keep the loose-mod root
short, or verify the concatenated length before rewriting.

### Implementation

This hook design is implemented in `tools/modpatcher/` (ships as a `dinput8.dll` proxy) and verified
on a real launch: the `VFS_ResolvePath` hook installs cleanly and logs every boot-time asset request
(configs, scripts, sound, archive containers, `entitylibrary.fcb`,
`EntityLibraryPatchOverride.fcb`) passing through it with zero crashes across a full boot-through-
world-load sequence. The override works end-to-end: an **empty** file placed at
`Data_Win32\Loose\worlds\worlds.dat` crashes the game exactly where a corrupt `worlds.dat` would —
direct proof the rewritten-path mechanism substitutes the loose file rather than merely logging past
it. `Dunia.dll`'s static imports include `DINPUT8.dll`, which is what makes the proxy-DLL
hook-installation approach work.

### Coverage gap: a second, lower-level path bypasses the hook

Not everything goes through `VFS_ResolvePath`. `ArchiveEntry_FindAndOpen` has 5 callers, not 1: besides
`VFS_ResolvePath`, there's `ArchiveChain_FindByHash`, a small per-object helper (`FUN_10cf0df0`), and —
significantly — **`LevelAsset_OpenStream`** (`0x107e06b0`, 9 callers of its own, clustered with the
world/level path-builder `FUN_107e28b0`).

`LevelAsset_OpenStream` reimplements `VFS_ResolvePath`'s core logic one level lower, entirely outside
the hook: it either calls `VFS_OpenFileRaw` directly (raw `CreateFileW`, no archive search) or hashes a
path and calls `ArchiveEntry_FindAndOpen` directly. Two sampled callers show terrain/heightmap read
patterns (fixed `width×height×4`-byte reads, repeating 8×8 tile grids) — consistent with the
per-world-sector [`.sdat` terrain files](./sdat.md).

**Practical implication**: the loose-file loader cannot override anything requested through this path
(confirmed: world-sector terrain data; suspected but unconfirmed: `.xbg` meshes and `.xbm` materials,
which live in the same hash-indexed archive storage and plausibly stream the same way). Extending
coverage means hooking `ArchiveEntry_FindAndOpen` itself — the true shared choke point — but it
receives an already-computed hash, not a string, so the path-rewrite trick doesn't directly apply; it
would need a precomputed hash→loose-file lookup table built at startup instead. That extension is not
implemented.

## Unknowns

- The virtual "provider gate" call at `*(param_1+4)` — always passes in practice, no non-trivial
  implementation found. Could be a second, engine-native hook point.
- Whether `DAT_10ff0ef8`'s search-path list (`+8`/`+0xc`) is ever populated with more than archives —
  i.e. whether a dormant/dev-only code path already adds loose directories to this same list, which
  would be a cleaner hook than the detour above if it exists. Writers to `DAT_10ff0ef8+8`/`+0xc` are
  not traced.
- `FUN_102487d0`'s exact record layout beyond hash/offset — not needed for the detour design, but would
  matter for building a real in-process virtual archive instead of a path-rewrite detour.
