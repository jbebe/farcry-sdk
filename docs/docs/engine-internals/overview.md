---
sidebar_position: 1
---

# Dunia.dll — Overview & Symbol Table

Part of the Dunia.dll note set, companion to [the launcher exe notes](./launcher-exe.md), kept
separate because this is a fundamentally different scale of target — `FarCry2.exe` is a ~20KB thin
launcher stub; `Dunia.dll` is the actual engine core. Update this file as functions/classes get
identified in the Ghidra project (`reverse/fc2.rep/`), same convention as the exe file. See the
sidebar for the full engine-internals note set.

## What this binary is

- **Build identified**: `C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin\Dunia.dll`,
  20,183,176 bytes — matches **Far Cry 2 Steam v1.03** exactly, per
  `research/reference-files/text-snippets/Far_Cry_2_1.03_filediffs.txt`. Worth re-checking this
  hash/size if the Ghidra project is ever pointed at a different copy — DVD/GOG/Uplay/1.00–1.02
  builds are all differently sized and *not* guaranteed to share offsets.
- **Ubisoft's "Dunia Engine"** — built for Far Cry 2, publicly documented as derived from CryEngine
  (heavily modified). Also powers *Avatar: The Game* (2009) — confirmed at the data/asset level in
  the [modding getting-started notes](../modding/getting-started.md) (shared skeleton/rig tooling
  works across both titles), not yet confirmed at the binary level from this side.
- **Same toolchain as the exe**: MSVC 2008-era (the exe links `MSVCR80.dll`; expect this DLL to
  match). This is a real C++ engine core — expect RTTI, vtables, class hierarchies throughout,
  unlike the exe's flat C-style code. Keep Ghidra's **RTTI Analyzer** and **Demangler** on; they'll
  do a lot of the "what is this" work for free at this scale.
- **Confirmed exports** (from `FarCry2.exe`'s import table, already recovered in
  [the launcher exe notes](./launcher-exe.md)): `RunGame(HINSTANCE*, const char*)`, `RegisterGameFunctionProvider(void*)`,
  `AddFunctionCB(void* fn, const char* name)`. These are our known-good entry points into this DLL —
  start navigation from them rather than from `DllMain`.
- **Embeds a real Lua interpreter — not just native C++.** Confirmed via strings, statically
  compiled directly into this DLL (no `lua51.dll`/`lua5.1.dll` import):
  ```
  "Lua 4.1 (alpha)"        <- literal interpreter version string
  "CLuaResource" / "LuaGlobals" / "LuaState"   <- native C++ binding/wrapper classes
  "StopSoundMixingFromLua" / "StartSoundMixingFromLua" / "PlayMusicFromLua"  <- native fns exposed to script
  "SCRIPTS\MissionTools.lua"                    <- an actual shipped script file
  "value for `lua_getinfo' is not a function"   <- Lua interpreter's own error strings
  ```
  `"Lua 4.1 (alpha)"` is a rare, semi-official PUC-Rio branch that briefly existed between the
  officially-released Lua 4.0 and 5.0 — this exact lineage is a known fingerprint of CryEngine's
  historical bundled Lua fork. This is the first **binary-level** confirmation of the
  CryEngine-derivation claim above; previously that was only confirmed at the data/asset-tooling
  level in the modding notes. See [the Lua API surface notes](./lua-api-surface.md) for the full
  exposed API map.
- **Also links licensed Havok middleware for physics/animation**, confirmed via string:
  `"Havok Physics evaluation key has expired or is invalid...Please contact Havok.com..."` (and an
  equivalent Havok Animation string). Not a from-scratch physics/animation system.
  :::note[Community-reported]
  A specific version number surfaced independently (Discord, `🔩-tools-talking`, 2022-12-10, from
  "Lasercar": *"well, far cry 2 uses Havok 5.5.0 r1"*) — **Havok 5.5.0 r1**. Not yet cross-checked
  against this DLL by disassembly, but consistent with the evaluation-key string above and useful as
  a starting point for any future `.hkx`/physics RE.
  :::
- **Net architecture picture**: native C++ core for performance-critical systems (weapons, AI,
  entities) + licensed Havok for physics/animation + a genuinely embedded Lua layer scoped to a
  narrower band of designer-tunable behavior (mission sequencing, reinforcement/respawn timers —
  confirmed reliable per the [known gotchas](../modding/gotchas.md) — some sound/music triggers) +
  external FCB/XML data files for stat tuning. Not "everything hardcoded," but nowhere near a
  Papyrus-style "everything is a scripted quest" model either — Lua is scoped to specific systems
  here, not a general authoring layer. **The `AddFunctionCB`/`FunctionRegistry_Invoke` mechanism
  documented in [the function-registry notes](./function-registry.md) is entirely separate from
  this Lua layer** — pure native C++, CRC32-hash-keyed, no Lua involvement in anything traced so
  far. Two independent extension mechanisms coexisting in the same binary.

## A second binary lives in the same Ghidra project: `FarCry2_server`

Discovered while researching the savegame format (see [the savegame format
notes](../file-formats/savegame.md)) — `reverse/fc2.gpr` already contains a third program besides
`FarCry2.exe` and `Dunia.dll`, named `FarCry2_server` in its project metadata
(`reverse/fc2.rep/idata/00/00000002.prp`). It's the **Linux dedicated-server build**: an ELF binary
(`.dynamic`/`.got.plt`, load base `~0x08048000`), POSIX/glibc imports (`pthread_create`, `mkdir`,
`gethostbyname`, ...), GCC/Itanium-mangled C++ symbols (`_ZN14CPersistenceDB...`), and — unlike this
PC `Dunia.dll` — **largely unstripped**, with a real `.symtab`/`.strtab` giving genuine class/method
names for shared engine code (persistence, save/load, screenshot/thumbnail, game-file-list systems
all confirmed present and linked in, even though a headless server never itself writes a player
`.sav`).

**Any address in this Ghidra project starting `0x08`/`0x09`/`0x0a` belongs to `FarCry2_server`, not
this DLL** — everything else in this note set uses `Dunia.dll`'s `0x10xxxxxx` PC load addresses.
Worth deliberately cross-referencing this binary against `Dunia.dll` going forward: its better symbol
coverage can name a PC-side function whose Windows binary only has a bare `FUN_`/`DAT_` address.

:::note[Community-reported]
The community independently flagged why this binary is so symbol-rich: the **Linux dedicated server
was accidentally shipped as a debug build** (Discord, `🔩-tools-talking`, 2022-07-13, from
"Steve64b": *"apparently the linux build was accidentally built in debug mode"*,
`http://static3.cdn.ubi.com/far_cry_2/FarCry2_Dedicated_Server_Linux.tar.gz`) — consistent with the
unstripped `.symtab`/`.strtab` confirmed above. Separately, a community member ("bajuh") reported
independently reverse-engineering `Dunia.dll` with Ghidra, cross-referencing this same Linux server
binary, specifically to build an FCB-editing tool (Discord, `🔨-fc2-modding`, 2026-07-17) — someone
outside this project doing essentially the same cross-referencing exercise as this note set, a
possible collaboration/comparison lead if that tool or writeup ever surfaces publicly.
:::

## Named symbols (address table)

Recorded here in case the Ghidra project (`reverse/fc2.rep/`) is ever lost — names alone are
useless without the addresses to re-apply them. All addresses are load-time VAs from the Steam
v1.03 build identified above.

| Address | Name | Role | Origin |
|---|---|---|---|
| `0x10006510` | `RunGame` | Entry point called from the exe's `WinMain`; command-line dispatch + main loop | Pre-existing (demangled export) |
| `0x10001cc0` | `RegisterGameFunctionProvider` | Stashes the exe's callback pointer into `g_pGameFunctionProvider` | Pre-existing (export) |
| `0x10001cd0` | `AddFunctionCB` | Export wrapper; real logic is `FunctionRegistry_Insert` | Pre-existing (export) |
| `0x10004900` | `InitDuniaEngine` | Main engine init, called from `RunGame`; likely where `g_pFunctionRegistry` gets constructed (unconfirmed) | Pre-existing (export) |
| `0x10fd42c8` | `g_pGameFunctionProvider` | Global: holds the exe's `RegisterDebugCommands` pointer between `RegisterGameFunctionProvider` and its invocation in `RunGame` | **Renamed** (was `DAT_10fd42c8`) |
| `0x10fd4280` | `g_hGameWindow` | Global: main window handle, passed to `DestroyWindow` each `RunGame` loop iteration | **Renamed** (was `DAT_10fd4280`) |
| `0x1160629c` | `g_pFunctionRegistry` | Global: pointer to the one engine-wide named-function registry singleton. Confirmed as the `this` for both `FunctionRegistry_Insert` and `FunctionRegistry_Invoke` via direct disassembly (`MOV ECX, dword ptr [0x1160629c]` at both call sites) | **Renamed** (was `DAT_1160629c`) |
| `0x10299430` | `FunctionRegistry_Insert` | `__thiscall`, single caller (`AddFunctionCB` only). Find-or-insert `(name, fn)` into `g_pFunctionRegistry`'s map | **Renamed** (was `FUN_10299430`) |
| `0x102993b0` | `FunctionRegistry_Invoke` | `__thiscall`, ~17 callers engine-wide. Finds a name in `g_pFunctionRegistry` and calls the stored fn ptr with 2 args, silent no-op if not found | **Renamed** (was `FUN_102993b0`) |
| `0x10e8e358` | *(string data)* `"AddDiamond"` | Literal name string, lives in this DLL's own data (separate copy from the exe's). Only referenced from `0x1066b660` | Not renamed — already a plain string |
| `0x10229400` | `CRC32_Hash` | Textbook CRC-32: reflected algorithm, `0xffffffff` seed, 256-entry lookup table (`DAT_10f95388`), final bitwise complement. Generic across the whole engine (90+ callers), not registry-specific — but "CRC-32 hash" is accurate for every one of them, so the name doesn't overclaim | **Renamed** (was `FUN_10229400`) |
| `0x10228380` | `GetNameHash` | Wrapper around `CRC32_Hash`: writes `CRC32(name)` into the output slot, `0xffffffff` sentinel for a null/empty name, alternate argument-less path via `FUN_10229440` (cached/current-context variant, not yet examined) for some callers. Same "generic but accurate" reasoning as above | **Renamed** (was `FUN_10228380`) |
| `0x102487d0` | `ArchiveEntry_OpenAtOffset` | `__thiscall`, single caller `ArchiveEntry_FindAndOpen`. Opens a sub-stream at an entry's recorded offset and, if compressed, hands off to `ArchiveEntry_Decompress` | Pre-existing (named in an earlier session, not yet written up before this table entry) |
| `0x102486d0` | `ArchiveEntry_Decompress` | 3-way dispatch on the entry's 2-bit compression-scheme field. See [the archives notes](../file-formats/archives-fat-dat.md) "Confirmed: entry decompression dispatch" for the full writeup | **Renamed** (was `FUN_102486d0`) |
| `0x10258d60` | `ArchiveEntry_DecompressLzo1x` | Scheme-1 handler; thin wrapper around `Lzo1x_Decompress` | **Renamed** (was `FUN_10258d60`) |
| `0x1025a620` | `Lzo1x_Decompress` | The actual LZO1X token decoder — confirmed byte-for-byte structurally identical to JackAll's `Lzo1x.cs` (`tools/JackAll/src/JackAll.Core/Format/Lzo1x.cs`) | **Renamed** (was `FUN_1025a620`) |
| `0x10258d00` | `ArchiveEntry_DecompressZlib` | Scheme-2 handler; thin wrapper around `Zlib_DecompressChunked` | **Renamed** (was `FUN_10258d00`) |
| `0x1025d1c0` | `Zlib_DecompressChunked` | Custom blocked container wrapping raw-DEFLATE per block — **not** a plain deflate/zlib stream, see [the archives notes](../file-formats/archives-fat-dat.md) for the exact framing | **Renamed** (was `FUN_1025d1c0`) |
| `0x1025d110` | `Zlib_InflateRawBlock` | Inflates one block via `zlib_inflateInit2_`(windowBits=-15)/`zlib_inflate`(Z_FINISH)/`zlib_inflateEnd` — confirmed genuine zlib 1.2.3, raw-DEFLATE mode | **Renamed** (was `FUN_1025d110`) |
| `0x10258e30` | `zlib_inflateInit2_` | Confirmed via its own arguments: `windowBits=-15` (raw deflate), version string `"1.2.3"`, `stream_size=0x38` (`sizeof(z_stream)` on 32-bit) | **Renamed** (was `FUN_10258e30`) |
| `0x10259030` | `zlib_inflate` | Called with flush=`4` (`Z_FINISH`); return `1` checked as `Z_STREAM_END` | **Renamed** (was `FUN_10259030`) |
| `0x10d75340` | `zlib_inflateEnd` | Paired with `zlib_inflateInit2_`/`zlib_inflate` above | **Renamed** (was `FUN_10d75340`) |
| `0x10235080` | `Fcb_ReadHeader` | Validates an `.fcb` buffer's magic/version/flags, then calls `Fcb_AllocateTree`. See [the `.fcb` notes](../file-formats/fcb.md) for the full writeup | **Renamed** (was `FUN_10235080`) |
| `0x10234fc0` | `Fcb_AllocateTree` | Allocates the output object-tree pool (`(totalObjectCount*6 + totalValueCount) * 4` bytes) and kicks off `Fcb_ParseObject` | **Renamed** (was `FUN_10234fc0`) |
| `0x10234d60` | `Fcb_ParseObject` | The recursive `.fcb` object-tree parser — child/value counts, object-level backreferences, value-level shared-bytes offsets, all confirmed in [the `.fcb` notes](../file-formats/fcb.md) | **Renamed** (was `FUN_10234d60`) |
| `0x10234260` | `Fcb_ReadTypeHash` | Reads an object's TypeHash — a plain u32, or (flags bit 0 set) a length-prefixed string hashed via `GetNameHash` | **Renamed** (was `FUN_10234260`) |
| `0x10246200` | `Fcb_MagicConstant` | Trivial: returns the `.fcb` magic constant `0x4643626e` ("FCbn") | **Renamed** (was `FUN_10246200`) |
| `0x10246210` | `Fcb_SupportedVersionConstant` | Trivial: returns `2`, the only `.fcb` version this build accepts | **Renamed** (was `FUN_10246210`) |
| `0x10624230` | `Spk_GetFileNameFromSoundId` | Builds a `.spk` filename from a sound id (hash-named, optional locale subfolder). See [the `.spk` notes](../file-formats/spk.md) | **Renamed** (was `FUN_10624230`) |
| `0x106242f0` | `Spk_BuildSoundFileNameString` | Wraps the above filename into a `CryString`-like object | **Renamed** (was `FUN_106242f0`) |
| `0x1062c180` | `Spk_GetSoundResourceFromId` | Resolves a sound id to a resource: builds the filename, opens via `VFS_ResolvePath`, reads the file, virtual-dispatches to `Spk_ParseContainer` | **Renamed** (was `FUN_1062c180`) |
| `0x106243d0` | `Spk_SoundResourceCtor` | Sets the sound-resource object's vtable pointer (`PTR_FUN_10e82e10`) just before the virtual dispatch above | **Renamed** (was `FUN_106243d0`) |
| `0x10624b80` | `Spk_ParseContainer` | The real (non-stub) `.spk` container parser — magic/count/id-table/variable-record walk. Full byte layout in [the `.spk` notes](../file-formats/spk.md) | **Renamed** (was `FUN_10624b80`) |
| `0x10a425b0` | `Spk_CreateSoundObjectFromRecord` | Generic resource-manager wrapper invoked per `.spk` record | **Renamed** (was `FUN_10a425b0`) |
| `0x10a3f490` | `Spk_InitRecordDescriptor` | Trivial 4-field setter: stores `{id, dataPtr, size, extra}` — the payload is registered opaquely, not decoded, at load time | **Renamed** (was `FUN_10a3f490`) |
| `0x10a3fb30` | `Spk_GetOrLoadSoundObject` | The consumer of the `{id, dataPtr, size, extra}` descriptor: uses inline data if present, else falls back to loading a standalone file. See [the `.spk` notes](../file-formats/spk.md) | **Renamed** (was `FUN_10a3fb30`) |
| `0x10a3fb00` | `Spk_ResolveSoundObjectData` | Dispatcher: inline data vs standalone-file-load, based on the above | **Renamed** (was `FUN_10a3fb00`) |
| `0x10a3f9f0` | `Spk_LoadStandaloneSoundFile` | Loads a sound object from its own standalone `.sbao`/`.bao` file by id (the "streamed" path) | **Renamed** (was `FUN_10a3f9f0`) |
| `0x10a3f4b0` | `Spk_BuildSbaoOrBaoFileName` | `sprintf("%08x.sbao", id)` or `"%08x.bao"` — confirms the shared id-namespace between `.spk` records and standalone sound files | **Renamed** (was `FUN_10a3f4b0`) |
| `0x10a3f960` | `Spk_ValidateAndDispatchSoundObject` | Validates the 40-byte sound-object descriptor's minimum size, copies it, dispatches by type | **Renamed** (was `FUN_10a3f960`) |
| `0x10a3f820` | `Spk_DispatchSoundObjectByType` | Switches on the descriptor's type tag (offset `+0x20` — verified against all 42,215 real records; the decompile's own field-index arithmetic pointed at `+0x18`, which was wrong); `0x50000000` ("streamed") is rejected outright for inline loading — see [the `.spk` notes](../file-formats/spk.md) | **Renamed** (was `FUN_10a3f820`) |
| `0x10a3f280` | `Spk_LoadSimpleFixed68Object` | Type `0x10000000` handler: fixed 68-byte sub-header, plain copy | **Renamed** (was `FUN_10a3f280`) |
| `0x10a3f310` | `Spk_LoadTransformedFixed128Object` | Type `0x20000000` handler: fixed 128-byte sub-header, then `Spk_TransformFixed128Payload` | **Renamed** (was `FUN_10a3f310`) |
| `0x10a3f3c0` | `Spk_LoadFlatCopyObject` | Type `0x30000000` handler: no sub-header, whole remainder copied verbatim | **Renamed** (was `FUN_10a3f3c0`) |
| `0x10a3f690` | `Spk_LoadLargeFixed256Object` | Type `0x40000000` handler: fixed 256-byte sub-header, plain copy | **Renamed** (was `FUN_10a3f690`) |
| `0x10a3f410` | `Spk_LoadCountPrefixedListObject` | Type `0x60000000` handler: reads a leading count via `Spk_ProcessCountPrefixedList` — a list of references, not a single sound | **Renamed** (was `FUN_10a3f410`) |
| `0x10a3f610` | `Spk_LoadSelfReferentialObject` | Type `0x70000000` handler: copies then fixes up an internal offset into an absolute pointer | **Renamed** (was `FUN_10a3f610`) |
| `0x10a51690` | `Spk_ProcessCountPrefixedList` | Single caller (`Spk_LoadCountPrefixedListObject`) — confirmed sound-specific, not shared generic code | **Renamed** (was `FUN_10a51690`) |
| `0x10a51750` | `Spk_TransformFixed128Payload` | Single caller (`Spk_LoadTransformedFixed128Object`) | **Renamed** (was `FUN_10a51750`) |

**Deliberately left un-renamed** — touched and understood algorithmically, but each is a generic,
heavily-shared engine utility with 90+ unrelated call sites (confirmed via xref count), not
something specific to this registry — and unlike `CRC32_Hash`/`GetNameHash`, no generic-but-accurate
name presents itself without inventing one:

- `FUN_1057a030` — generic map/tree `find`, reused by dozens of unrelated maps.
- `FUN_10769180` — generic map `insert`, same story as the find function.
- `FUN_1066b660` — the diamond-pickup/reward handler that calls `FunctionRegistry_Invoke("AddDiamond", ...)`. Its own identity/class isn't pinned down yet (heavy vtable/property-reflection use, not yet named) — only its *role in this specific chain* is confirmed, not its overall single responsibility.
