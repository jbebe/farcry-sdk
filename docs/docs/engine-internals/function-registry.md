---
sidebar_position: 2
---

# Dunia.dll — The Named Function-Callback Registry

Part of the Dunia.dll note set — see [the overview](./overview.md) for the binary identification
and the symbol/address table referenced throughout this file.

## Confirmed behavior: the named function-callback registry

The exe's debug/QA commands (`AddDiamond`, `MalariaCurve`, `SaveGame`, etc. — see
[the launcher exe notes](./launcher-exe.md)) are registered into, and dispatched from, the single
global registry documented in [the overview](./overview.md#named-symbols-address-table). Full
chain, verified end to end via decompilation:

1. `RunGame` parses `-openautomate` off the command line first (separate QA-automation code path,
   `FUN_10005fa0`, not otherwise explored yet). Otherwise it loops: `FUN_10006130` →
   `InitDuniaEngine(...)` → on success, calls through `g_pGameFunctionProvider` — this is where the
   exe's `RegisterDebugCommands` callback actually runs, *after* engine init, not from `WinMain`
   directly.
2. `AddFunctionCB(void *fn, char *name)` is a thin export wrapper around `FunctionRegistry_Insert`,
   whose `this` (`g_pFunctionRegistry`) is loaded from a fixed global — one singleton for the whole
   engine, not per-caller state. The insert itself is a classic find-or-insert into what's
   structurally a `std::map<uint32, void*>` (or an equivalent hand-rolled tree) — **keyed by
   `CRC32(name)`, not the string itself**: `GetNameHash` computes the hash via `CRC32_Hash`
   (`0xffffffff` sentinel for a null/empty name), the generic `find` helper looks that hash up, and
   if the result equals the map's `end()` sentinel (`*(int*)(this+0x14)`), a new node is inserted
   and the callback pointer stored into it.
3. **Dispatch side** — `FunctionRegistry_Invoke`, `__thiscall`, ~17 call sites engine-wide:
   ```c
   undefined4 __thiscall FunctionRegistry_Invoke(int registry, int hash_key, undefined4 arg1, undefined4 arg2)
   {
     find(&hash_key, hash_key);                    // generic map find, keyed by CRC32(name)
     if (hash_key != *(int *)(registry + 0x14)) {  // found (!= end())
       return (**(code **)(hash_key + 0x10))(arg1, arg2); // call stored fn ptr(arg1, arg2)
     }
     return 0;  // not registered -> silent no-op
   }
   ```
   First found via the diamond-pickup/reward handler at `0x1066b660`, which builds an `"AddDiamond"`
   key (string confirmed present inside this DLL's own data at `0x10e8e358`, distinct from the
   exe's copy) and calls `FunctionRegistry_Invoke(&key, arg1, arg2)`. Every one of the ~17 call
   sites engine-wide has now been surveyed (string literal read either inline from the caller's
   decompiled pseudocode, or — where the decompiler failed to propagate it — from the raw string
   data directly):

   | Event name | Caller address | Context |
   |---|---|---|
   | `"incHB"` | `0x1065aea0` | Health-bar-looking float math (clamp/compare) — a **live gameplay tick**, not dead code, despite being a no-op stub (`SaveGame`/`incHB` shared) on the exe side. **Tested live in-game** (`reverse/patch_incHB.py`): the arg/return value is a `float` passed **by raw bits through EAX**, not FPU/ST(0) convention — confirmed via the caller's own disassembly (`MOVSS`/`COMISS` around the call, no `CVTSI2SS`/`FILD` anywhere). A first patch attempt using `FLD` to return the value crashed the game (unpopped x87 stack push on every call, in a function that runs frequently, overflowing the 8-deep FPU stack within seconds) — corrected to a pure `MOV EAX,[ESP+4]` echo, which is stable and confirmed to restore the real time-driven value into the downstream threshold comparison. The comparison's *effect* (which bit `0x8` of a flags value passed to `FUN_104cfc90` actually controls) remains unconfirmed — no visible in-game difference observed, so the "heartbeat" reading of `"HB"` is unconfirmed, not ruled out |
   | `"carJoke"` | `0x100e66a0` | **New — not one of the 12 names registered by `FarCry2.exe`** (see [the launcher exe notes](./launcher-exe.md)). Since nothing registers this name in retail, this call always hits the silent no-op path — until patched: a binary patch registering a handler that writes `false` into the `cStack_6e` veto flag (`reverse/patch_carJoke.py`) was tested live in-game and **confirmed to fully disable car interaction** — the veto path produces the identical outcome to the function's separate `iVar4 == 0` ("no valid interaction target") early-return, meaning `FUN_100e66a0` is a vehicle-entry/interaction handler and `carJoke` is a full gate over it, not just a theoretical one |
   | `"InitializeUseableEvent"` | `0x106c44a0` | Matches a known registered name |
   | `"mapJoke"` | `0x106f07d0` | Matches known (shares a stub with `LoadGame` on the exe side) |
   | `"LoadGame"` | `0x1072ef00` | Matches known; function clearly walks a `PersistenceDB` list — a real save/load routine |
   | `"SelectStoryMission"` | `0x10755ee0` | Matches known; same function also calls `SelectLibraryMission` immediately after — confirms these are a paired mission-ID-resolution step |
   | `"SelectLibraryMission"` | `0x10755ee0` | See above |
   | `"menuJoke"` | `0x108c8830` | Matches known; this function is literally building the main menu (Story Mode / Multiplayer / Options / Credits / Exclusive Content / Quit) — confirms `menuJoke` gates something during menu construction |
   | `"SetLoadingText"` | `0x100d1370` | Matches known; function is a loading-screen text/localization setup routine |
   | `"SetLoadingText"` (2nd site) | `0x1007dd90` | A separate, synchronous loading path (`"LOADING_SYNC"`, `"p_loading"` strings nearby) — same event, independent call site |
   | `"toRed"` | `0x105fb0e0` | Guarded by a one-time-init flag (`param_1+0xee`). **Tested live in-game**: flipping the exe-side handler from `*param_1 = 1` to `*param_1 = 0` (`reverse/patch_toRed.py`) made **all 2D graphics render red-channel-only** — this is a UI/HUD color-channel toggle, not weapon/vehicle init as the nearby reload-timer-shaped math had suggested. Revises the earlier guess: `param_1` here is more likely a 2D-renderer/UI state object than a weapon/vehicle instance, and `param_1+0xf8` is a "full color enable" style flag (`1` = normal RGB, `0` = red-channel-only) |
   | `"PlayerSPFinalize"` | `0x106a60b0` | End of what looks like a player-controller finalize/setup routine |
   | `"CheckDomino"` | `0x109f71b0` | Tiny dedicated function whose *sole* purpose is this one call — same minimal-wrapper pattern as `RegisterDebugCommands` in the exe |
   | `"MalariaCurve"` (×3) | `0x106a6140` | One function, **three back-to-back calls** on three distinct curve-stage values (`param_1+0x104`, `+0x10c`, `+0x108`), each computed from a lookup table (`FUN_10765f60`, indices 10/11/12-13 — plausibly "first attack time / between-attack time / duration"). Confirms the exe-side `*param_1 *= <float constant>` handler independently scales each malaria stage |
   | `"AddDiamond"` | `0x1066b660` | Diamond-pickup/reward handler |

   Of the 12 names originally registered by `FarCry2.exe`, 10 now have at least one confirmed live
   call site here. Two names don't line up on both sides:

   - **`"SetDefaultTimeOut"`** — confirmed genuinely dead, not just unsurveyed: the exe registers a
     handler for it (`SetDefaultTimeOut` @ `0x401080` in [the launcher exe notes](./launcher-exe.md)), but Dunia.dll
     never calls `FunctionRegistry_Invoke` with this name anywhere. A real regression/orphaned hook
     between the two sides — registered but never dispatched.
   - **`"carJoke"`** goes the other way: invoked here (`0x100e66a0`), but never registered by the
     exe — the observed "not registered → silent no-op" case.
   - Bare `"SaveGame"` (as opposed to its alias `"incHB"`, which does have a confirmed call site at
     `0x1065aea0`) hasn't turned up in this survey either — status not yet as certain as
     `SetDefaultTimeOut`'s, since it's plausible a call site exists outside the ~17 surveyed here.

**What this means**: this is not merely a QA-only hook system — real gameplay code (the diamond
pickup path, the main menu, mission selection, loading-screen text, malaria progression, player
finalize, at minimum) calls out to the exe by name on genuine game events, with a silent no-op
fallback if the exe never registered that name (`"carJoke"` is a concrete observed instance of this
fallback in the retail build). Consistent with the [known gotchas](../modding/gotchas.md)'s "no
in-game dev console" finding (this is an internal instrumentation/automation seam, not a
player-facing console) while also explaining why most exe-side handlers were harmless dead stubs in
retail — the call site tolerates them not existing at all. **This is empirically confirmed, not
just structurally inferred**: registering a handler for `"carJoke"` via a binary patch
(`reverse/patch_carJoke.py`) and testing live in-game measurably changed engine behavior (car
interaction fully disabled) — proof this mechanism genuinely alters live gameplay, not just a QA
read-only probe.

**Still open**: where `g_pFunctionRegistry` itself gets constructed (presumably inside or just
before `InitDuniaEngine`, unconfirmed); what `FUN_1066b660`, `FUN_106a6140`, and the other callers
above actually are/do beyond their role in this one chain (none have been named — only their
involvement in this dispatch pattern is confirmed); whether bare `"SaveGame"` is invoked from a call
site outside this survey (`"SetDefaultTimeOut"` is confirmed dead, not just unsurveyed — see above).
