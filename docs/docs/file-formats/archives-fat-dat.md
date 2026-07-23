---
sidebar_position: 1
---

# `.fat`/`.dat` — Archive Loading & the Generic File Resolver

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against the Steam v1.03 build. For the practical, tool-level side of
unpacking/repacking these archives (Gibbed's tools, wobatt's decoder), see [Getting
Started](../modding/getting-started.md); this page covers the engine's own internals.
:::

Goal of this investigation: understand exactly how the engine turns a relative asset path (e.g.
`worlds/world1/generated/entitylibrary.fcb`) into bytes, well enough to identify a safe hook point
for a STALKER/Skyrim-style loose-file mod loader (load from a folder instead of/in addition to the
packed `.fat`/`.dat` archives).

## Status: implemented and dynamically verified (Project 1)

The hook design below was built (`tools/modpatcher/`, ships as a `dinput8.dll` proxy) and confirmed
working against a real launch, not just statically reasoned about:
- `VFS_ResolvePath` hook installs cleanly; log shows every boot-time asset request (configs,
  scripts, sound, archive containers, `entitylibrary.fcb`, `EntityLibraryPatchOverride.fcb`) passing
  through it correctly with zero crashes across a real boot-through-world-load sequence.
- The override itself was proven end-to-end: placing an **empty** file at
  `Data_Win32\Loose\worlds\worlds.dat` caused the game to crash exactly where it would if
  `worlds.dat` itself were corrupt — direct proof the rewritten-path/absolute-path mechanism is
  actually substituting the loose file, not just logging past it.
- `Dunia.dll`'s real static imports were confirmed to include `DINPUT8.dll` (cross-checked against
  `list_imports`), validating the proxy-DLL hook-installation approach documented in the plan.

## Known coverage gap: a second, lower-level path bypasses this hook entirely

Not everything goes through `VFS_ResolvePath`. `ArchiveEntry_FindAndOpen` (the shared choke point
both `VFS_ResolvePath` and this bypass path funnel through) has **5 callers**, not 1. Besides
`VFS_ResolvePath`, the other four are `ArchiveChain_FindByHash`, a small per-object helper
(`FUN_10cf0df0`), and — the significant one — **`LevelAsset_OpenStream`** (`0x107e06b0`), which has
**9 callers of its own**, clustered with the world/level path-builder (`FUN_107e28b0`) already
documented above.

`LevelAsset_OpenStream` reimplements `VFS_ResolvePath`'s core logic **one level lower**, entirely
outside our hook: it either calls `VFS_OpenFileRaw` directly (raw `CreateFileW`, no archive search
at all) or hashes a path and calls `ArchiveEntry_FindAndOpen` directly — skipping the hooked
function completely. Two sampled callers show clear terrain/heightmap read patterns (fixed
`width×height×4`-byte reads, repeating 8×8 tile grids with per-tile "loaded" flags) — consistent
with the per-world-sector [`.sdat` terrain files](./sdat.md).

**Practical implication**: the current loose-file loader does not and cannot override anything
requested through this path (confirmed: world-sector terrain data; suspected but not yet directly
confirmed: meshes `.xbg` and materials `.xbm`, which live in the same hash-indexed archive storage
and are very plausibly streamed the same way). Extending coverage would mean hooking
`ArchiveEntry_FindAndOpen` itself (the true universal choke point both paths share) — but that
function receives an already-computed **hash**, not a string, so the path-rewrite trick this
project relies on doesn't directly apply; it would need a precomputed hash→loose-file lookup table
built at startup instead. Scoped as a deliberate follow-up, not yet started.

## Confirmed behavior: three lazily-constructed singletons

All three are built together, on first use, by `FUN_10cf3e40` (double-checked lazy init, each guarded
by its own `if (DAT_xxx == 0)` — also independently torn down together by `FUN_10cf3de0`, each via a
virtual destructor call `(**(code**)*DAT_xxx)(1)`):

| Global | Built by | Role |
|---|---|---|
| `DAT_116793b0` | `FUN_10ceffa0` | **Archive list** — fixed-size table of every top-level `.dat` archive name the engine knows about, see below. |
| `DAT_10ff0e94` | `FUN_10229700` | **Path manager** — resolves the exe's install directory, then builds `<install>\Data_Win32\` and `<install>\data\` root strings (`"bin"` search-and-strip logic + `SHGetFolderPathW`-based save path, consistent with the already-documented [save-data path notes](../engine-internals/save-data-path.md)). Also named-callback accessible: `GetExePath` (`0x10002220`), `FCE_Engine_GetPersonalPath` (`0x10893d10`). |
| `DAT_10ff0ef8` | `FUN_10235d90` | **VFS resolver** — the object whose method actually turns a relative path into an open file handle. See "The generic resolver" below. Confirmed via disassembly of a call site: `MOV ECX, dword ptr [0x10ff0ef8]` immediately precedes `CALL 0x102358a0`. |

`FUN_104cfb60` (an alternate, apparently editor-side, bootstrap path) constructs an equivalent archive-list object directly rather than going through `FUN_10cf3e40`, but overwrites its vtable to `PTR_FUN_10e69dac` instead of the base `PTR_FUN_10f485d4` — two call sites building what's semantically the same singleton, one via a derived class. Not fully explained, not load-bearing for the resolver work below.

## The archive list (`DAT_116793b0`, built by `FUN_10ceffa0`)

A fixed-size table, one `0x84`-byte (132-byte) slot per known top-level archive, each slot holding an
inline name string plus two int fields (an id/index and what looks like a priority or "unlimited"
sentinel, `0x7fffffff`, on most slots):

```
patch.dat        (id 0x2b, second field = 1  -- the one non-sentinel value, consistent with "loads first")
common.dat       (id 0x3f)
sound.dat        (id 0x2e)
sound_%lang%.dat (id 0x2e)
soundcache.dat   (id 0x28)
[unused slot]
[unused slot]
shadersobj.dat   (id 0x24)
[~30 further unused slots, reserved capacity]
```

Then, separately, a **dynamic vector** (`param_1+1`/`param_1+2` in the constructor, distinct storage
from the fixed slots above) is populated by pushing six more archive names:
`worlds/tmpla/tmpla.dat`, `worlds/world1/world1.dat`, `worlds/world2/world2.dat`,
`worlds/multicommon/multicommon.dat`, and the `_%lang%` variant of each of the four `worlds/...` names.

This matches the file-manifest catalog exactly (`common`, `patch`, `shadersobj`, `sound`,
`sound_english`, `worlds/worlds`, `worlds/worlds_english` — the constructor's `worlds/world1.dat`
etc. are pre-merge components of what ships as the single `worlds.dat`/`worlds.fat` pair; DLC
archives are handled by a separate mechanism not traced here).

**Per-archive open** (`FUN_102a6c40`, indexed by slot number, stride `0x84` matching the table above):
looks up the slot's name string, opens the underlying `.dat` via a cached-open helper
(`FUN_102358a0`, see below, called here with mode `8`) and, if buffering is enabled for that slot,
wraps it in an async double-buffered reader (`FUN_1022a220`, 64KB buffer) backed by a real
`CreateFileA`-descended chain (`FUN_1023c5b0` sets up a pool of `param_4` buffers of `param_3` bytes
each — an I/O ring, not the raw handle itself).

## Why there is no `.fat` string literal anywhere in the binary

Archive entries are indexed by CRC32 hash, not stored filenames. Confirmed here: the engine only
ever opens the literal `X.dat` file by name (see table above) — the paired `X.fat` index is loaded
as a private in-memory hash table (see "per-entry lookup" below) and its filename is never
referenced as a string constant; it's an implicit sibling-file convention baked into the
archive-open code, not a runtime string operation.

## The generic resolver — `FUN_102358a0`, `this = DAT_10ff0ef8`

**This is the one function every asset load funnels through**, both the low-level per-archive open
above and the high-level format loaders (confirmed caller: `FUN_102340f0`, the FCB-loader trampoline
used e.g. by the world entity-library load path, mode `0x21`). Signature:

```
FUN_102358a0(DAT_10ff0ef8 /*this, via ECX*/, char *relativePath, uint modeFlags, char forceFlag)
```

Control flow (mode-flag branches first, main path last):

1. **`modeFlags & 0x20`** — recursive re-entry with adjusted flags, gated behind a generation/version
   check (`DAT_10ff0f14`) — a cache-validity wrapper, not asset-relevant.
2. **`modeFlags & 0x40`** — opens via mode `2` then wraps the result in a large (0x400000 = 4MB)
   buffered reader (`FUN_1024b0a0`) — a "load whole file into memory" fast path.
3. **Main path** (everything else, including the `8`/`0x21` modes seen from actual asset loaders):
   - Calls a virtual **provider gate**: `(**(code**)**(param_1+4))(path, mode)` — if this returns
     false, resolution fails immediately. A pluggable veto hook already exists here; not traced
     further (no confirmed non-trivial implementation found).
   - **`FUN_10231510(path)`** — checks whether `path` is already absolute: `true` iff the string
     contains `:` (drive letter) or starts with `\\` (UNC). This single check is the crux of the
     hook design below.
   - **If relative, and the resolver has a non-empty search-path list** (`*(param_1+0xc) != 0`,
     array at `*(param_1+8)`, 8-byte stride): computes a hash of `path` (`FUN_10233c20`) and walks
     the search-path list **in order, first match wins** (`FUN_10249070` per entry — see below). If
     any entry's hash table contains the path, that entry's data is returned immediately and the
     loop/function exits — **later entries and the raw-disk fallback are never reached.**
   - **If nothing matched** (empty search-path list, or no entry contained the hash) **and the path
     was relative**: builds `<PathManager root> + path` into a 260-byte (`MAX_PATH`) stack buffer via
     `FUN_10235860`.
   - **If the path was absolute** (or after the fallback prefixing above): copies it into the same
     260-byte stack buffer and calls **`FUN_10231ae0(path, modeFlags)`** — a thin wrapper that maps
     `modeFlags` onto Win32 `CreateFileW` access/creation-disposition flags and calls `CreateFileW`
     directly. **This is the raw filesystem escape hatch, already present and already reachable with
     zero engine modification for any absolute path.**

**Per-search-path-entry lookup** (`FUN_10249070`, called once per entry in priority order): binary
searches (`FUN_10248870`) a sorted array of 16-byte `{hash, offset_lo/size...}` records
(`entry+0x28`, count `entry+0x2c`) for the path's hash; on a hit, opens a sub-stream at the recorded
byte offset within that entry's own already-open `.dat` handle (`FUN_102487d0`). **This confirmed
the `.fat` index's actual in-memory shape**: a sorted, CRC32-hash-keyed offset/size table, one per
mounted archive, each wrapped as one "search-path entry" in the resolver's list — i.e. **each mounted
`.fat`/`.dat` pair is structurally just one link in an ordered override chain**, the same general
shape as a Bethesda BSA load order or a STALKER `gamedata` search path list. The retail game just
never populates that list with anything other than packed archives.

This confirms — from the disassembly, not just from a forum report — the archive search-path order
behind the [`gamemodesconfig.xml`-in-two-archives gotcha](../modding/gotchas.md): `patch.dat` >
`common.dat` > `sound*.dat`/`soundcache.dat`/`shadersobj.dat` > `worlds/*.dat`, first match wins —
`common.dat` is checked before any `worlds/*.dat`, so its copy of a colliding hash wins over
`World.dat`'s.

## Confirmed existing precedent: the patch-override merge

`FUN_1065b130` (world entity-library load) is a second, independent confirmation of the "try an
override, merge if present" pattern already documented at the data level in [Getting
Started](../modding/getting-started.md) (`entitylibrarypatchoverride.fcb`). It always loads the base
`entitylibrary.fcb` (or `entitylibrary_full.fcb`, chosen by a flag at `this+0xc4`) via the resolver
above, and **unconditionally also attempts** to load the fixed path
`generated\EntityLibraryPatchOverride.fcb` through the exact same resolver call. If that second load
succeeds (non-zero return), the result is merged into the base data via `FUN_10549560`. This is a
hardcoded single-override special case for one file — not a generalized mechanism — but it proves
the "load base, conditionally load+merge an override, don't error if the override is absent" idiom
is native to the engine's own design, not something a mod loader would be fighting against.

## Second confirmation: the DLC entity-library merge, and a lead on `entitylibrary_full.fcb`

Traced in a separate, more heavily-symbolized FC2 binary (identified as a server build, distinct
from the retail `Dunia.dll` this file otherwise documents — real mangled C++ symbols throughout,
e.g. `CEntityLibraryManager`, `CDlcService`, `CXGame`). `CXGame::LoadArchetypes` (`0x08888750`) is
the runtime (non-editor) entity-archetype loader:

1. Builds `<world's generated dir>\entitylibrary.fcb` — **only ever this exact name**, loads it via
   `CEntityLibraryManager::ReadFromXML`.
2. Calls `CDlcService::GetEntityLibraries()` to enumerate every installed DLC's own entity library,
   and for each one calls `CEntityLibraryManager::Override(...)` to merge it on top of the base.

This directly confirms the previously "unconfirmed but consistent" community theory (see the DLC
weapon gotcha in [Gotchas](../modding/gotchas.md)) that DLC entity data loads after the main patch
and wins — there's a real, named `Override` call fed by `GetEntityLibraries`, not just an inferred
load-order effect.

**Lead on `entitylibrary_full.fcb`**: this binary's strings contain zero references to
`entitylibrary_full.fcb` anywhere — every entity-library path it ever builds is the plain name. Since
the Windows-side `World_LoadEntityLibraryWithOverride` (above) *does* pick between the two via a flag,
the split looks likely to be client-only (e.g. `_full` carrying presentation/render-layer entity data
a dedicated server has no use for, `entitylibrary.fcb` being the shared gameplay-logic subset) —
inferred from absence-of-reference + naming, not yet confirmed by an actual content diff of matched
`entitylibrary.fcb`/`entitylibrary_full.fcb` pairs for the same world. Worth a follow-up once both
files for one world are available to diff directly.

## Recommended hook point for a loose-file/folder mod loader

Given the above, the lowest-risk, highest-leverage hook is a trampoline **detour on `FUN_102358a0`
itself (`0x102358a0` in the v1.03 build, `__thiscall`, `this = DAT_10ff0ef8`)**:

1. On entry, if `modeFlags` is a plain read-style open (matches the values already seen in practice:
   `8`, `0x21` — reject the `0x20`/`0x40` recursive-wrapper calls, which aren't real path requests),
   normalize `relativePath` and check `GetFileAttributesW` against a candidate loose path — e.g.
   `<install>\Data_Win32\LooseMods\<relativePath>` (or a priority-ordered list of several mod folders,
   replicating the override-chain idea directly at the hook layer rather than inside the engine).
2. If a loose file exists, **rewrite the path argument to that absolute candidate path** before
   calling through to the original function. `FUN_10231510`'s absolute-path check (`:` or `\\`)
   already routes any absolute path straight to `FUN_10231ae0` → `CreateFileW`, **completely bypassing
   the archive search-path/hash-lookup loop** — no need to fabricate a fake search-path entry or
   understand/replicate the FAT hash-table format at all.
3. If no loose file exists, call through unmodified — normal archive resolution proceeds exactly as
   today.

This mirrors exactly what the engine already does for a plain absolute path, so it requires no
understanding of `FUN_10248870`'s binary-search/hash internals and no risk of corrupting the
in-memory FAT tables. **Practical constraint to respect**: the absolute-path branch copies into a
fixed 260-byte (`MAX_PATH`) stack buffer (`acStack_104` in `FUN_102358a0`) with no bounds check beyond
that size — a loose-mod root nested deep inside a long Steam library path (common on Windows,
`C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\...`) plus a long relative asset path could
realistically overflow it. Keep the loose-mod root short (e.g. a drive-root-relative path, or verify
the concatenated length before rewriting) rather than assuming `MAX_PATH` headroom.

## Confirmed: entry decompression dispatch (compression scheme 0/1/2)

Traced forward from `ArchiveEntry_OpenAtOffset` (`0x102487d0`, per-entry sub-stream open above) to
answer a JackAll question: is `CompressionScheme.Zlib` (bit pattern `2` in the packed
scheme/uncompressed-size field JackAll's `FatArchive.cs` already decodes) real, or dead/unused? Every
shipped FC2 archive (`common`, `patch`, `shadersobj`, `sound`, `sound_english`, `worlds`,
`worlds_english`, both DLCs — ~215k entries scanned via JackAll) uses only scheme `0` (none) and `1`
(LZO1X); scheme `2` never appears in real data, so this had to be settled by disassembly, not by
sampling shipped files.

For any entry whose upper (uncompressed-size) bits are non-zero, `ArchiveEntry_OpenAtOffset` calls
**`ArchiveEntry_Decompress`** (`0x102486d0`), a 3-way dispatch keyed exactly on the 2-bit scheme
value:

| Scheme | Handler | Identity |
|---|---|---|
| 0 | `0x10258c50` | Unidentified — parses its own variable-length (1-5 byte, `0x80`-continuation) length prefix from the compressed bytes rather than trusting the fat index. Never reached in practice (real `Compression=None` entries always carry `UncompressedSize=0`, so they never reach the dispatcher at all — this branch exists in the binary but appears unreachable from any real asset). Not investigated further. |
| 1 | `ArchiveEntry_DecompressLzo1x` (`0x10258d60`) → `Lzo1x_Decompress` (`0x1025a620`) | **Confirmed LZO1X** — `Lzo1x_Decompress`'s disassembly matches JackAll's `Lzo1x.cs` state machine constant-for-constant (the `0x11`/`0x40`/`0x20`/`0x10` token thresholds, the `0x800`/`0x4000` back-reference offsets). This is the same algorithm already exhaustively round-trip tested against real data by `Lzo1xTests`. |
| 2 | `ArchiveEntry_DecompressZlib` (`0x10258d00`) → `Zlib_DecompressChunked` (`0x1025d1c0`) → `Zlib_InflateRawBlock` (`0x1025d110`) | **Confirmed real zlib**, not dead code and not the unrelated Quazal-networking zlib instance (that one lives entirely separately, around `0x10c9xxxx`/`0x10ca0xxx`, and is unreferenced from anywhere near the archive code). `Zlib_InflateRawBlock` calls `zlib_inflateInit2_(strm, -15, "1.2.3", 0x38)` then `zlib_inflate(strm, 4 /* Z_FINISH */)` then `zlib_inflateEnd` — `windowBits=-15` is zlib's documented raw-DEFLATE-no-header convention, `"1.2.3"`/`0x38` (`sizeof(z_stream)` on 32-bit) match the embedded `" inflate 1.2.3 Copyright 1995-2005 Mark Adler "` string exactly. |

**Scheme 2 is not a plain raw-deflate stream over the whole entry.** `Zlib_DecompressChunked` wraps
it in a bespoke container first: a header gives a block count and a fixed block size (rounded up to
a multiple of 16, capped at `0x10000`); each block carries its own 16-bit length prefix — `0` means
"stored," the block is `memmove`'d through verbatim, otherwise it's raw-DEFLATE and gets fed to
`Zlib_InflateRawBlock` — with the read cursor padded forward between blocks to keep it 16-byte
aligned. **`System.IO.Compression.DeflateStream`/`ZLibStream` cannot decode this directly** — neither
matches the framing (`ZLibStream` additionally expects a zlib header + Adler32 trailer neither of
which exist here). JackAll's `DuniaArchive.cs` `DecompressZlib` currently calls `ZLibStream` and is
consequently wrong on both counts — harmless today only because no shipped FC2 data exercises that
path, so it's dead/untested code, not a live bug.

**Practical implication for modding**: scheme 2 is genuinely usable — the engine will read it
correctly — but a conforming encoder/decoder means reproducing this exact chunk container, not just
calling into `System.IO.Compression`. Scoped as a deliberate follow-up if JackAll ever wants
compressed mod output; not started.

**Not yet traced / open questions**:
- The virtual "provider gate" call at `*(param_1+4)` — always seems to pass in practice, no
  non-trivial implementation examined yet. Could theoretically be a second, engine-native hook point.
- Whether `DAT_10ff0ef8`'s search-path list (`+8`/`+0xc`) is ever populated with more than the
  archives — i.e., whether there's a dormant/dev-only code path that already adds loose directories
  to this same list (would be an even cleaner hook than the detour above, if it exists and is just
  gated behind an unset flag/missing command-line arg). Worth a follow-up search for writers to
  `DAT_10ff0ef8+8`/`+0xc` specifically.
- `FUN_102487d0`'s exact record layout beyond the first two fields (hash, offset) — not needed for
  the detour design above, but would matter for anyone wanting to build a real in-process virtual
  archive instead of a path-rewrite detour.
