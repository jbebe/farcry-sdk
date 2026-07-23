---
sidebar_position: 6
---

# Far Cry 2 — Reverse Engineering Notes

Findings from static analysis in Ghidra (project: `reverse/fc2.gpr`), driven interactively via the
GhidraMCP bridge. This file tracks binary structure and named/annotated functions as they're
recovered — update it whenever a new function is identified or renamed in the Ghidra DB, so the
prose stays in sync with the actual project state.

## Toolchain

- Ghidra project: `reverse/fc2.gpr` / `reverse/fc2.rep/`.
- Live analysis is done through GhidraMCP (LaurieWired/GhidraMCP) — Claude can read decompiled
  pseudocode, disassembly, xrefs, imports/exports/strings, and write back renames/comments/types
  directly into the Ghidra DB. Requires Ghidra open with the target program loaded and analyzed;
  functions must exist as `Function` objects in Ghidra before they're readable via MCP (raw
  unanalyzed bytes need a manual Disassemble + Create Function pass in the GUI first — the MCP
  tool surface has no "create function" primitive).

## `FarCry2.exe`

Compiled with **MSVC 2008** (confirmed via `___tmainCRTStartup` library-function match).
This binary is a **thin launcher stub** — essentially all real game/engine logic lives in
`Dunia.dll`, loaded and driven through a handful of imported entry points. There is very little
FC2-specific code in the exe itself.

### Entry chain

```
entry (0x00401185ish)                stock CRT: ___security_init_cookie(); ___tmainCRTStartup();
  -> ___tmainCRTStartup @ 0040122b   stock MSVC08 CRT startup (cmdline trim, TLS/init-term, etc.)
       -> WinMain @ 0x004011b0       the only FC2-specific code called from the CRT
```

`WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, char* lpCmdLine)` body is exactly two calls:

```c
void WinMain(HINSTANCE__ *param_1, undefined4 param_2, char *param_3)
{
  RegisterGameFunctionProvider(&RegisterDebugCommands);
  RunGame(param_1, param_3);
  return 0;
}
```

- `RegisterGameFunctionProvider` and `RunGame` are both **external imports**, resolved into
  `Dunia.dll` (confirmed via the `Dunia.dll` string and the mangled import name
  `?RunGame@@YA_NPAUHINSTANCE__@@PBD@Z` → `bool __cdecl RunGame(HINSTANCE*, const char*)`).
- The CRT's own cmdline handling (quote/whitespace trimming to strip the program-name token) is
  the *only* argument processing done in the exe. The raw remaining command-line string is handed
  straight to `RunGame` — **all actual flag/argument parsing happens inside `Dunia.dll`**, not in
  this binary (see [the command-line args notes](./command-line-args.md)).

### `RegisterDebugCommands` @ `0x004010e0`

Not a config table — a **callback registry**. It calls the imported `AddFunctionCB(void* fn, const
char* name)` (also resolved into `Dunia.dll`) 15 times, registering 12 unique exe-side function
pointers under string names. This is an inversion-of-control pattern: `Dunia.dll` owns a
generic, name-keyed dispatcher (almost certainly backing its dev/QA debug console) and has zero
built-in knowledge of FC2-specific concepts — the exe injects the FC2-specific behavior by handing
over named function pointers at startup.

`AddFunctionCB` itself is `__cdecl(void* fn, const char* name)` — inferred from the compiler
batching stack cleanup (`ADD ESP, 0x40` / `0x38`) across runs of consecutive calls rather than
cleaning up after each one individually, which is characteristic cdecl caller-side coalescing (a
callee-cleans convention like stdcall would never produce this).

**Resolved** (see [the function-registry notes](./function-registry.md)): the dispatch side inside
`Dunia.dll` — the `FunctionRegistry_Insert`/`FunctionRegistry_Invoke` mechanism, keyed by
`CRC32(name)` — is now fully mapped, including a live-tested survey of ~17 call sites covering most
of the names below.

### Registered debug commands

Most of these are **dead stubs in the retail build** — the real implementations likely live
elsewhere (or these hooks are QA-only and unused in shipped gameplay). Only a handful do real
arithmetic. `param_1`/`param_2` are raw pointers passed by whatever calls the callback (presumably
`Dunia.dll`'s debug console); their target types were not recovered beyond what the decompiler
inferred.

| Function (renamed) | Address | Registered name(s) | Behavior |
|---|---|---|---|
| `ToRed` | `0x401000` | `toRed` | `*param_1 = 1`. **Tested live in-game** (see [the function-registry notes](./function-registry.md)): flipping this to `*param_1 = 0` made all 2D graphics render red-channel-only — a UI/HUD color-channel toggle, not a weapon/vehicle flag as the caller context had suggested. |
| `MenuJoke` | `0x401010` | `menuJoke` | `return *param_1`. Trivial passthrough getter. |
| `LoadGame_Stub` | `0x401020` | `mapJoke`, `LoadGame` | `return 1`, no params. **Pure stub** — real load logic must be inside `Dunia.dll`. |
| `SelectStoryMission` | `0x401030` | `SelectStoryMission` | `return *param_1 + 10`. Mission-ID offset. |
| `SelectLibraryMission` | `0x401040` | `SelectLibraryMission` | `return *param_1 + 0x15` (21). Mission-ID offset. |
| `MalariaCurve` | `0x401050` | `MalariaCurve` | `*param_1 *= <float constant @ 0x4020fc>`. In-place curve multiplier — **candidate for a "reduce malaria mechanic" tweak** if the constant is patchable. |
| `AddDiamond` | `0x401070` | `AddDiamond` | `*param_1 += *param_2`. Accumulator (diamond-case pickup count). |
| `SetDefaultTimeOut` | `0x401080` | `SetDefaultTimeOut` | `*param_1 = *param_2`. Plain copy. |
| `SetLoadingText` | `0x401090` | `SetLoadingText` | `*param_1 = 0` (16-bit write). Clears/null-terminates a text buffer. |
| `PlayerSPFinalize` | `0x4010a0` | `PlayerSPFinalize` | `*param_1 = <constant @ 0x402100>`. Writes a fixed status/finalize code. |
| `InitializeUseableEvent_Stub` | `0x4010c0` | `InitializeUseableEvent`, `CheckDomino` | `*param_1 = 1` (byte write). **Pure stub.** |
| `SaveGame_Stub` | `0x4010d0` | `incHB`, `SaveGame` | `return 0`, no params. **Pure no-op** — real save logic must be inside `Dunia.dll`. |

Three addresses answer to two registered names each (`LoadGame_Stub`, `InitializeUseableEvent_Stub`,
`SaveGame_Stub`) — one stub implementation wired to multiple debug-console command names, consistent
with these being disabled/no-op paths in the shipped build rather than active dispatchers.

## Open threads / next steps

- `Dunia.dll`'s side of most of these is now mapped — see [the function-registry
  notes](./function-registry.md) for the registry/dispatch mechanism and the call-site survey (also
  [command-line args](./command-line-args.md), [save-data path](./save-data-path.md),
  [Lua API surface](./lua-api-surface.md), and [the overview](./overview.md) for the rest of the
  Dunia.dll notes). `RunGame`'s own argument parsing beyond the `-openautomate` flag hasn't been
  dug into further.
- The float constant behind `MalariaCurve` (`0x4020fc`) and the constant behind `PlayerSPFinalize`
  (`0x402100`) haven't been read/typed yet — worth pulling their actual values.
- Unresolved: what "menuJoke" actually gates in the main-menu construction it's called from (`toRed`
  is now resolved — see above and [the function-registry notes](./function-registry.md)).
