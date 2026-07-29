---
sidebar_position: 2
---

# `FarCry2.exe` — The Launcher Stub

:::info[Verified via reverse engineering]
See [the overview](./overview.md) for binary identification and toolchain notes shared across this
note set.
:::

Compiled with MSVC 2008 (confirmed via `___tmainCRTStartup` library-function match). This binary is a
thin launcher stub — essentially all real game/engine logic lives in `Dunia.dll`, loaded and driven
through a handful of imported entry points. There is very little FC2-specific code in the exe itself.

## Entry chain

```
entry (0x00401185ish)                stock CRT: __security_init_cookie(); __tmainCRTStartup();
  -> __tmainCRTStartup @ 0040122b    stock MSVC08 CRT startup (cmdline trim, TLS/init-term, etc.)
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

`RegisterGameFunctionProvider` and `RunGame` are both external imports, resolved into `Dunia.dll`
(confirmed via the mangled import name `?RunGame@@YA_NPAUHINSTANCE__@@PBD@Z` →
`bool __cdecl RunGame(HINSTANCE*, const char*)`). The CRT's own cmdline handling (quote/whitespace
trimming to strip the program-name token) is the only argument processing done in the exe — the raw
remaining command-line string is handed straight to `RunGame`. **All actual flag/argument parsing
happens inside `Dunia.dll`**, not here — see [command-line args](./command-line-args.md).

## `RegisterDebugCommands` @ `0x004010e0`

Not a config table — a callback registry. It calls the imported `AddFunctionCB(void* fn, const char*
name)` (also resolved into `Dunia.dll`) 15 times, registering 12 unique exe-side function pointers
under string names. This is an inversion-of-control pattern: `Dunia.dll` owns a generic, name-keyed
dispatcher and has zero built-in knowledge of FC2-specific concepts — the exe injects FC2-specific
behavior by handing over named function pointers at startup. The dispatch side inside `Dunia.dll` — the
`FunctionRegistry_Insert`/`FunctionRegistry_Invoke` mechanism, keyed by `CRC32(name)` — is documented
on [the function registry page](./function-registry.md), including a live-tested survey of the call
sites for most of the names below.

`AddFunctionCB` itself is `__cdecl(void* fn, const char* name)` — inferred from the compiler batching
stack cleanup (`ADD ESP, 0x40`/`0x38`) across runs of consecutive calls rather than cleaning up after
each one individually, characteristic of cdecl caller-side coalescing (a callee-cleans convention like
stdcall would never produce this).

## Registered debug commands

Most of these are dead stubs in the retail build — the real implementations likely live elsewhere, or
these hooks are QA-only and unused in shipped gameplay. Only a handful do real arithmetic. `param_1`/
`param_2` are raw pointers passed by whatever calls the callback (presumably `Dunia.dll`'s debug
console); their target types weren't recovered beyond what the decompiler inferred.

| Function (renamed) | Address | Registered name(s) | Behavior |
|---|---|---|---|
| `ToRed` | `0x401000` | `toRed` | `*param_1 = 1`. **Tested live in-game**: flipping this to `*param_1 = 0` made all 2D graphics render red-channel-only — a UI/HUD color-channel toggle. See [function registry](./function-registry.md). |
| `MenuJoke` | `0x401010` | `menuJoke` | `return *param_1`. Trivial passthrough getter. |
| `LoadGame_Stub` | `0x401020` | `mapJoke`, `LoadGame` | `return 1`, no params. Pure stub — real load logic lives inside `Dunia.dll`. |
| `SelectStoryMission` | `0x401030` | `SelectStoryMission` | `return *param_1 + 10`. Mission-ID offset. |
| `SelectLibraryMission` | `0x401040` | `SelectLibraryMission` | `return *param_1 + 0x15` (21). Mission-ID offset. |
| `MalariaCurve` | `0x401050` | `MalariaCurve` | `*param_1 *= <float constant @ 0x4020fc>`. In-place curve multiplier — a candidate for a "reduce malaria mechanic" tweak if the constant is patchable. |
| `AddDiamond` | `0x401070` | `AddDiamond` | `*param_1 += *param_2`. Accumulator (diamond-case pickup count). |
| `SetDefaultTimeOut` | `0x401080` | `SetDefaultTimeOut` | `*param_1 = *param_2`. Plain copy. |
| `SetLoadingText` | `0x401090` | `SetLoadingText` | `*param_1 = 0` (16-bit write). Clears/null-terminates a text buffer. |
| `PlayerSPFinalize` | `0x4010a0` | `PlayerSPFinalize` | `*param_1 = <constant @ 0x402100>`. Writes a fixed status/finalize code. |
| `InitializeUseableEvent_Stub` | `0x4010c0` | `InitializeUseableEvent`, `CheckDomino` | `*param_1 = 1` (byte write). Pure stub. |
| `SaveGame_Stub` | `0x4010d0` | `incHB`, `SaveGame` | `return 0`, no params. Pure no-op — real save logic lives inside `Dunia.dll`. |

Three addresses answer to two registered names each (`LoadGame_Stub`, `InitializeUseableEvent_Stub`,
`SaveGame_Stub`) — one stub implementation wired to multiple debug-console command names, consistent
with these being disabled/no-op paths in the shipped build rather than active dispatchers.

## `tools/FCSE`: a reimplementation of this exe's own `WinMain`

`tools/FCSE` (see its `README.md`, and [the FCSE flagship page](/fcse) for the player-facing summary)
is a from-scratch reimplementation of this file's `WinMain` as a separate launcher exe, `FCSE.exe`,
that adds SKSE-style third-party DLL plugin loading between the two calls documented above. Building it
confirmed two things about the exe's export-table dependencies:

- `RegisterGameFunctionProvider` and `AddFunctionCB` are both plain, undecorated `Dunia.dll` exports —
  `GetProcAddress` resolves them by literal name, no C++ decoration involved, unlike `RunGame` (which
  needs its mangled name, `?RunGame@@YA_NPAUHINSTANCE__@@PBD@Z`, also present alongside a plain
  `RunGame` alias entry).
- `FunctionRegistry_Insert` (`0x10299430`) is a find-first insert: the existing entry is never
  overwritten if the name is already present — the call is a silent no-op. First registrant for a
  given name always wins at the engine level.

## Unknowns

- The float constant behind `MalariaCurve` (`0x4020fc`) and the constant behind `PlayerSPFinalize`
  (`0x402100`) haven't been read/typed.
- What `menuJoke` actually gates in the main-menu construction it's called from (`toRed` is resolved —
  see [function registry](./function-registry.md)).
- `RunGame`'s own argument parsing beyond the `-openautomate` flag hasn't been dug into further.
