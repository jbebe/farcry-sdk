---
sidebar_position: 1
---

# `Dunia.dll` — Overview & Symbol Table

:::info[Verified via reverse engineering]
Static analysis in Ghidra (project `reverse/fc2.gpr`), driven interactively via the GhidraMCP bridge.
This file tracks binary identification and the address table referenced throughout the rest of the
engine-internals notes.
:::

## Toolchain

Live analysis is done through GhidraMCP (LaurieWired/GhidraMCP): decompiled pseudocode, disassembly,
xrefs, imports/exports/strings can be read and renames/comments/types written back directly into the
Ghidra DB. Requires Ghidra open with the target program loaded and analyzed; functions must exist as
`Function` objects before they're readable via MCP (raw unanalyzed bytes need a manual Disassemble +
Create Function pass in the GUI first — the MCP tool surface has no "create function" primitive).

## Two binaries in the same Ghidra project

### `Dunia.dll` — the PC client engine

- **Build**: `Far Cry 2\bin\Dunia.dll`, 20,183,176 bytes — matches Far Cry 2 **Steam v1.03** exactly.
  DVD/GOG/Uplay/1.00–1.02 builds are differently sized and not guaranteed to share these offsets.
- **Ubisoft's Dunia Engine**, publicly documented as derived from CryEngine (heavily modified). Also
  powers *Avatar: The Game* (2009) — confirmed at the asset level (shared skeleton/rig tooling works
  across both titles, see [`.xbm`/`.xbg`](../file-formats/xbm-xbg.md)), not yet confirmed at the
  binary level.
- **MSVC 2008-era toolchain** (the launcher exe links `MSVCR80.dll`). A real C++ engine core — RTTI,
  vtables, and class hierarchies throughout, unlike the launcher's flat C-style code. Keep Ghidra's
  RTTI Analyzer and Demangler on.
- **Confirmed exports** (recovered from the launcher's import table — see [launcher
  exe](./launcher-exe.md)): `RunGame(HINSTANCE*, const char*)`, `RegisterGameFunctionProvider(void*)`,
  `AddFunctionCB(void* fn, const char* name)`. Known-good entry points for navigating this DLL, rather
  than starting from `DllMain`.
- **Embeds a real Lua interpreter, not just native C++.** Statically compiled in (no `lua51.dll`
  import) — confirmed via strings: `"Lua 4.1 (alpha)"`, `"CLuaResource"`/`"LuaGlobals"`/`"LuaState"`,
  `"StopSoundMixingFromLua"`/`"StartSoundMixingFromLua"`/`"PlayMusicFromLua"`,
  `"SCRIPTS\MissionTools.lua"`, and the interpreter's own error strings (`"value for 'lua_getinfo' is
  not a function"`). `"Lua 4.1 (alpha)"` is a rare, semi-official PUC-Rio branch that briefly existed
  between the released Lua 4.0 and 5.0 — a known fingerprint of CryEngine's historical bundled Lua
  fork, and the first binary-level confirmation of the CryEngine-derivation claim. See [the Lua API
  surface](./lua-api-surface.md) for the full exposed API map.
- **Also links licensed Havok middleware** for physics/animation, confirmed via string: `"Havok
  Physics evaluation key has expired or is invalid...Please contact Havok.com..."` (and an equivalent
  Havok Animation string) — not a from-scratch physics/animation system.
  :::note[Community-reported]
  A specific version surfaced independently (Discord, 2022-12-10): **Havok 5.5.0 r1**. Not
  cross-checked against the binary by disassembly, but consistent with the evaluation-key string and a
  useful starting point for `.hkx`/physics RE.
  :::
- **Architecture picture**: native C++ core for performance-critical systems (weapons, AI, entities) +
  licensed Havok for physics/animation + a genuinely embedded Lua layer scoped to a narrower band of
  designer-tunable behavior (mission sequencing, reinforcement/respawn timers, some sound/music
  triggers) + external `.fcb`/XML data files for stat tuning. Not "everything hardcoded," but nowhere
  near a fully-scripted authoring model either. Three separate extension mechanisms coexist in this one
  binary: the Lua layer above; the pure-native, CRC32-keyed [function-callback
  registry](./function-registry.md) (unrelated to Lua); and a large flat C export surface (~338
  `FCE_*`-prefixed functions) the stock map editor drives directly via P/Invoke, not scripting at all
  — see [the editor-facing API surface](./editor-api-surface.md).

### `FarCry2_server` — the Linux dedicated-server build

Discovered while researching the [savegame format](../file-formats/savegame.md) — the Ghidra project
also contains a third program, named `FarCry2_server` in its project metadata. It's the Linux
dedicated-server binary: an ELF (`.dynamic`/`.got.plt`, load base `~0x08048000`), POSIX/glibc imports
(`pthread_create`, `mkdir`, `gethostbyname`, ...), GCC/Itanium-mangled C++ symbols
(`_ZN14CPersistenceDB...`), and — unlike `Dunia.dll` — largely **unstripped**, with a real
`.symtab`/`.strtab` giving genuine class/method names for shared engine code (persistence, save/load,
screenshot/thumbnail, and game-file-list systems are all present and linked in, even though a headless
server never itself writes a player `.sav`).

**Any address in this project starting `0x08`/`0x09`/`0x0a` belongs to `FarCry2_server`, not
`Dunia.dll`** — every other page in this note set uses `Dunia.dll`'s `0x10xxxxxx` PC load addresses
unless it says otherwise. Its better symbol coverage is worth cross-referencing against `Dunia.dll`
going forward: it can name a PC-side function whose Windows binary only has a bare `FUN_`/`DAT_`
address.

:::note[Community-reported]
The Linux dedicated server was reportedly shipped as an accidental debug build (Discord, 2022-07-13) —
consistent with the unstripped `.symtab`/`.strtab` confirmed above. Separately, a community member
("bajuh") reported independently reverse-engineering `Dunia.dll` with Ghidra, cross-referencing this
same Linux server binary, specifically to build an FCB-editing tool (Discord, 2026-07-17) — a possible
prior-art/collaboration lead if that tool or writeup surfaces publicly.
:::

## Named symbols (`Dunia.dll`, Steam v1.03)

| Address | Name | Role |
|---|---|---|
| `0x10006510` | `RunGame` | Entry point called from the launcher's `WinMain`; command-line dispatch + main loop |
| `0x10001cc0` | `RegisterGameFunctionProvider` | Stashes the launcher's callback pointer into `g_pGameFunctionProvider` |
| `0x10001cd0` | `AddFunctionCB` | Export wrapper; real logic is `FunctionRegistry_Insert` |
| `0x10004900` | `InitDuniaEngine` | Main engine init, called from `RunGame`; likely where `g_pFunctionRegistry` gets constructed (unconfirmed) |
| `0x10fd42c8` | `g_pGameFunctionProvider` | Global: holds the launcher's `RegisterDebugCommands` pointer between registration and invocation in `RunGame` |
| `0x10fd4280` | `g_hGameWindow` | Global: main window handle, passed to `DestroyWindow` each `RunGame` loop iteration |
| `0x1160629c` | `g_pFunctionRegistry` | Global: the one engine-wide named-function registry singleton — confirmed `this` for both `FunctionRegistry_Insert` and `FunctionRegistry_Invoke` |
| `0x10299430` | `FunctionRegistry_Insert` | `__thiscall`, single caller (`AddFunctionCB`). Find-or-insert `(name, fn)` |
| `0x102993b0` | `FunctionRegistry_Invoke` | `__thiscall`, ~17 callers engine-wide. Finds a name and calls the stored fn ptr with 2 args, silent no-op if not found |
| `0x10229400` | `CRC32_Hash` | Textbook CRC-32: reflected algorithm, `0xffffffff` seed, 256-entry lookup table (`DAT_10f95388`), final complement. Generic, 90+ callers engine-wide |
| `0x10228380` | `GetNameHash` | Wrapper: writes `CRC32(name)` into an output slot, `0xffffffff` sentinel for null/empty |
| `0x102487d0` | `ArchiveEntry_OpenAtOffset` | Opens a sub-stream at an entry's recorded offset; hands off to `ArchiveEntry_Decompress` if compressed |
| `0x102486d0` | `ArchiveEntry_Decompress` | 3-way dispatch on the entry's compression scheme — see [archives](../file-formats/archives-fat-dat.md) |
| `0x10258d60` | `ArchiveEntry_DecompressLzo1x` | Scheme-1 handler; wraps `Lzo1x_Decompress` |
| `0x1025a620` | `Lzo1x_Decompress` | The LZO1X token decoder — matches JackAll's `Lzo1x.cs` structurally |
| `0x10258d00` | `ArchiveEntry_DecompressZlib` | Scheme-2 handler; wraps `Zlib_DecompressChunked` |
| `0x1025d1c0` | `Zlib_DecompressChunked` | Custom blocked container wrapping raw-DEFLATE per block — not a plain deflate/zlib stream |
| `0x1025d110` | `Zlib_InflateRawBlock` | Inflates one block via `zlib_inflateInit2_`(-15)/`zlib_inflate`(Z_FINISH)/`zlib_inflateEnd` — genuine zlib 1.2.3, raw-DEFLATE mode |
| `0x10258e30` | `zlib_inflateInit2_` | `windowBits=-15`, version `"1.2.3"`, `stream_size=0x38` |
| `0x10259030` | `zlib_inflate` | Called with `flush=4` (`Z_FINISH`) |
| `0x10d75340` | `zlib_inflateEnd` | Paired with the two above |
| `0x10235080` | `Fcb_ReadHeader` | Validates an `.fcb` buffer's magic/version/flags, calls `Fcb_AllocateTree` — see [`.fcb`](../file-formats/fcb.md) |
| `0x10234fc0` | `Fcb_AllocateTree` | Allocates the output object-tree pool, kicks off `Fcb_ParseObject` |
| `0x10234d60` | `Fcb_ParseObject` | The recursive `.fcb` object-tree parser |
| `0x10234260` | `Fcb_ReadTypeHash` | Reads an object's TypeHash — a plain u32, or (flags bit 0) a hashed string |
| `0x10246200` | `Fcb_MagicConstant` | Returns `0x4643626e` ("FCbn") |
| `0x10246210` | `Fcb_SupportedVersionConstant` | Returns `2`, the only accepted `.fcb` version |
| `0x10624230` | `Spk_GetFileNameFromSoundId` | Builds a `.spk` filename from a sound id — see [`.spk`](../file-formats/spk.md) |
| `0x106242f0` | `Spk_BuildSoundFileNameString` | Wraps the filename into a `CryString`-like object |
| `0x1062c180` | `Spk_GetSoundResourceFromId` | Resolves a sound id: builds the filename, opens via `VFS_ResolvePath`, reads, dispatches to `Spk_ParseContainer` |
| `0x106243d0` | `Spk_SoundResourceCtor` | Sets the sound-resource vtable pointer before the dispatch above |
| `0x10624b80` | `Spk_ParseContainer` | The `.spk` container parser — magic/count/id-table/variable-record walk |
| `0x10a425b0` | `Spk_CreateSoundObjectFromRecord` | Generic resource-manager wrapper invoked per `.spk` record |
| `0x10a3f490` | `Spk_InitRecordDescriptor` | Stores `{id, dataPtr, size, extra}` — the payload is registered opaquely at load time |
| `0x10a3fb30` | `Spk_GetOrLoadSoundObject` | Consumer of the descriptor: inline data if present, else falls back to a standalone file |
| `0x10a3fb00` | `Spk_ResolveSoundObjectData` | Dispatcher: inline vs. standalone-file-load |
| `0x10a3f9f0` | `Spk_LoadStandaloneSoundFile` | Loads a sound object from its own standalone `.sbao`/`.bao` file (the "streamed" path) |
| `0x10a3f4b0` | `Spk_BuildSbaoOrBaoFileName` | `sprintf("%08x.sbao"/"%08x.bao", id)` — confirms the shared id-namespace between `.spk` records and standalone files |
| `0x10a3f960` | `Spk_ValidateAndDispatchSoundObject` | Validates the 40-byte descriptor's minimum size, dispatches by type |
| `0x10a3f820` | `Spk_DispatchSoundObjectByType` | Switches on the descriptor's type tag (offset `+0x20`); `0x50000000` ("streamed") rejected outright for inline loading |
| `0x10a3f280` | `Spk_LoadSimpleFixed68Object` | Type `0x10000000`: fixed 68-byte sub-header, plain copy |
| `0x10a3f310` | `Spk_LoadTransformedFixed128Object` | Type `0x20000000`: fixed 128-byte sub-header, then `Spk_TransformFixed128Payload` |
| `0x10a3f3c0` | `Spk_LoadFlatCopyObject` | Type `0x30000000`: no sub-header, whole remainder copied verbatim |
| `0x10a3f690` | `Spk_LoadLargeFixed256Object` | Type `0x40000000`: fixed 256-byte sub-header, plain copy |
| `0x10a3f410` | `Spk_LoadCountPrefixedListObject` | Type `0x60000000`: a count-prefixed list of references, not a single sound |
| `0x10a3f610` | `Spk_LoadSelfReferentialObject` | Type `0x70000000`: copies then fixes up an internal offset into an absolute pointer |
| `0x10a51690` | `Spk_ProcessCountPrefixedList` | Single caller (`Spk_LoadCountPrefixedListObject`) |
| `0x10a51750` | `Spk_TransformFixed128Payload` | Single caller (`Spk_LoadTransformedFixed128Object`) |

**Deliberately left un-renamed** — understood algorithmically, but each is a generic, heavily-shared
engine utility with 90+ unrelated call sites, not something specific to any one subsystem:

- `FUN_1057a030` — generic map/tree `find`, reused by dozens of unrelated maps.
- `FUN_10769180` — generic map `insert`, same story.
- `FUN_1066b660` — the diamond-pickup/reward handler that calls
  `FunctionRegistry_Invoke("AddDiamond", ...)`. Its own class isn't pinned down (heavy vtable/
  property-reflection use) — only its role in [the function-registry chain](./function-registry.md) is
  confirmed.
