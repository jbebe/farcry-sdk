---
sidebar_position: 2
---

# `FarCry2.exe` — The Launcher Stub

:::info[Verified via reverse engineering]
See [the overview](./overview.md) for binary identification and toolchain notes shared across this
note set.
:::

Compiled with Visual C++ 2005: it imports `MSVCR80.dll`, the same runtime as `Dunia.dll`. This binary is a
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

## Registered callbacks

These are the `CSecurityManager` copy-protection hooks (see [the function registry](./function-registry.md)):
the engine passes a deliberately wrong value and the stock handler repairs it, so most of these small
bodies are load-bearing, not debug leftovers. 13 of the 15 names are invoked by live engine code
**(RE-verified)**. The table gives what each handler does; the registry page gives what the engine
does with the result.

| Function (renamed) | Address | Registered name(s) | Behavior |
|---|---|---|---|
| `ToRed` | `0x401000` | `toRed` | `*param_1 = 1`. **Tested live in-game**: flipping this to `*param_1 = 0` made all 2D graphics render red-channel-only — a UI/HUD color-channel toggle. See [function registry](./function-registry.md). |
| `MenuJoke` | `0x401010` | `menuJoke` | `return *param_1`: hands back the Story Mode page the main menu passes in. |
| `LoadGame_Stub` | `0x401020` | `mapJoke`, `LoadGame` | `return 1`: lets a save load and a map marker spawn. |
| `SelectStoryMission` | `0x401030` | `SelectStoryMission` | `return *param_1 + 10`: undoes the engine's −10 on the completed-story-mission count. |
| `SelectLibraryMission` | `0x401040` | `SelectLibraryMission` | `return *param_1 + 0x15` (21): the same for library missions. |
| `MalariaCurve` | `0x401050` | `MalariaCurve` | `*param_1 *= 60.0f`, the float at `0x4020fc`: turns the three malaria time curves from minutes into seconds. |
| `AddDiamond` | `0x401070` | `AddDiamond` | `*param_1 += *param_2`: adds a pickup to the diamond wallet. |
| `SetDefaultTimeOut` | `0x401080` | `SetDefaultTimeOut` | `*param_1 = *param_2`. Never invoked. |
| `SetLoadingText` | `0x401090` | `SetLoadingText` | `*param_1 = 0` (16-bit write): leaves the loading screen its localized default text. |
| `PlayerSPFinalize` | `0x4010a0` | `PlayerSPFinalize` | `*param_1 = 1.0f`, the dword `0x3F800000` at `0x402100`: a multiplier the player's damage handling reads. |
| `InitializeUseableEvent_Stub` | `0x4010c0` | `InitializeUseableEvent`, `CheckDomino` | `*param_1 = 1` (byte write): lets doors be used; `CheckDomino`'s result is ignored. |
| `SaveGame_Stub` | `0x4010d0` | `incHB`, `SaveGame` | `return 0`: keeps the AI running every frame; `SaveGame` is never invoked. |

Three addresses answer to two registered names each (`LoadGame_Stub`, `InitializeUseableEvent_Stub`,
`SaveGame_Stub`): the names share a body because they need the same answer. The two constants were
read from the GOG and the Steam `FarCry2.exe` and are the same in both; `FCSE.exe` uses them directly.

## `tools/FCSE`: a reimplementation of this exe's own `WinMain`

`tools/FCSE` (see its `README.md`, and [the FCSE flagship page](/fcse) for the player-facing summary)
is a from-scratch reimplementation of this file's `WinMain` as a separate launcher exe, `FCSE.exe`,
that adds SKSE-style third-party DLL plugin loading between the two calls documented above. It relies
on two facts about the exe's export-table dependencies:

- `RegisterGameFunctionProvider` and `AddFunctionCB` are both plain, undecorated `Dunia.dll` exports —
  `GetProcAddress` resolves them by literal name, no C++ decoration involved, unlike `RunGame` (which
  needs its mangled name, `?RunGame@@YA_NPAUHINSTANCE__@@PBD@Z`, also present alongside a plain
  `RunGame` alias entry).
- `FunctionRegistry_Insert` (`0x10299430`) is a find-first insert: the existing entry is never
  overwritten if the name is already present — the call is a silent no-op. First registrant for a
  given name always wins at the engine level.

## Not `FC2Launcher.exe` — that one is an updater

`bin/FC2Launcher.exe` shares nothing with this binary but the word "launcher", and needs no modding
attention. It is Ubisoft's generic **Game Update** auto-patcher: an MFC/ATL application
(`CUpgradeLauncherApp`, `Evil::UpgradeHost`, statically-linked libcurl and TinyXML) shipped across
many Ubisoft titles and branded per-game by stamping its resource string table — string ID 124 is the
un-branded sentinel ("The game update application must be modified with the resource tool"), and
startup aborts if the product name still equals it. It imports nothing from `Dunia.dll` and contains
no FC2-specific code at all.

Its whole job is to fetch
`http://gconnect.ubi.com/MatchMakingConfig.aspx?action=g_guc&gid=<info>&locale=en-US`, parse the
patch manifest (`PatchUrl`/`PatchSize`/`PatchCRC`/`Signature`/`Mirrors`), download and verify a
payload (CRC plus an Authenticode `WinVerifyTrust` pass), then start the game:

```c
ShellExecuteA(NULL, "open", execPath, NULL, installDir, SW_SHOWNORMAL);
```

`execPath`, `installdir`, `info` (the update service's game id) and `language` all come from
`HKLM\Software\Ubisoft\Far Cry 2\GameUpdate`; `execPath` points at `FarCry2.exe`. While running it
also hosts a local RPC server (`ncalrpc` endpoint `ubiUpdateService`) — a control/logging channel for
the updater, not DRM.

`gconnect.ubi.com` is long dead, so the update check always fails and falls straight through to
launching the game. Two consequences:

- `lpParameters` is `NULL`: the launcher forwards **no** command-line arguments, so nothing in
  [command-line args](./command-line-args.md) survives a launch made through it.
- Redirecting what gets launched needs no binary edit — it is the registry's `execPath` value.
