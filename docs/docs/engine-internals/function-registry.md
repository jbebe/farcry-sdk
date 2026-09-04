---
sidebar_position: 3
---

# `Dunia.dll` — The Named Function-Callback Registry

:::info[Verified via reverse engineering]
See [the overview](./overview.md) for binary identification and the address table referenced
throughout this page.
:::

The launcher's debug/QA commands (`AddDiamond`, `MalariaCurve`, `SaveGame`, etc. — see [the launcher
exe notes](./launcher-exe.md)) are registered into, and dispatched from, a single global registry
inside `Dunia.dll`.

## Mechanism

1. `RunGame` parses `-openautomate` off the command line first (a separate QA-automation code path,
   `FUN_10005fa0`, not otherwise explored). Otherwise it loops: `FUN_10006130` → `InitDuniaEngine(...)`
   → on success, calls through `g_pGameFunctionProvider` — this is where the exe's
   `RegisterDebugCommands` callback actually runs, *after* engine init, not from `WinMain` directly.
2. `AddFunctionCB(void *fn, char *name)` is a thin export wrapper around `FunctionRegistry_Insert`,
   whose `this` (`g_pFunctionRegistry`) is loaded from a fixed global — one singleton for the whole
   engine, not per-caller state. The insert is a classic find-or-insert into what's structurally a
   `std::map<uint32, void*>` (or an equivalent hand-rolled tree) — **keyed by `CRC32(name)`, not the
   string itself**: `GetNameHash` computes the hash via `CRC32_Hash` (`0xffffffff` sentinel for a
   null/empty name), the generic `find` helper looks it up, and if the result equals the map's `end()`
   sentinel, a new node is inserted and the callback pointer stored.
3. **Dispatch** — `FunctionRegistry_Invoke`, `__thiscall`, ~17 call sites engine-wide:
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

## Call-site survey

Every one of the ~17 call sites engine-wide has been identified (string literal read either from the
caller's decompiled pseudocode, or from the raw string data where the decompiler failed to propagate
it):

| Event name | Caller address | Context |
|---|---|---|
| `"incHB"` | `0x1065aea0` | Health-bar-looking float math (clamp/compare) — a **live gameplay tick**, not dead code, despite being a no-op stub (`SaveGame`/`incHB` shared) on the exe side. **Tested live in-game** (`reverse/patch_incHB.py`): the value is passed **by raw bits through EAX**, not FPU/ST(0) convention — confirmed via the caller's own disassembly (`MOVSS`/`COMISS` around the call, no `CVTSI2SS`/`FILD`). A first patch attempt using `FLD` crashed the game (unpopped x87 stack push on every call, overflowing the 8-deep FPU stack within seconds) — corrected to a pure `MOV EAX,[ESP+4]` echo, stable and confirmed to restore the real time-driven value into the downstream comparison. What bit `0x8` of the flags value passed to `FUN_104cfc90` actually controls is unconfirmed — no visible in-game difference observed, so the "heartbeat" reading of `"HB"` is unconfirmed, not ruled out. |
| `"carJoke"` | `0x100e66a0` | Not one of the 12 names registered by `FarCry2.exe` — always hits the silent no-op path in retail. A binary patch registering a handler that writes `false` into the veto flag `cStack_6e` (`reverse/patch_carJoke.py`) was tested live in-game and **confirmed to fully disable car interaction** — the veto path produces the identical outcome to the function's separate "no valid interaction target" early-return, meaning `FUN_100e66a0` is a vehicle-entry/interaction handler and `carJoke` is a full gate over it. |
| `"InitializeUseableEvent"` | `0x106c44a0` | Matches a known registered name. |
| `"mapJoke"` | `0x106f07d0` | Matches known (shares a stub with `LoadGame` on the exe side). |
| `"LoadGame"` | `0x1072ef00` | Matches known; walks a `PersistenceDB` list — a real save/load routine. |
| `"SelectStoryMission"` | `0x10755ee0` | Matches known; also calls `SelectLibraryMission` immediately after — a paired mission-ID-resolution step. |
| `"SelectLibraryMission"` | `0x10755ee0` | See above. |
| `"menuJoke"` | `0x108c8830` | Matches known; this function is literally building the main menu (Story Mode / Multiplayer / Options / Credits / Exclusive Content / Quit) — confirms `menuJoke` gates something during menu construction. |
| `"SetLoadingText"` | `0x100d1370` | Matches known; a loading-screen text/localization setup routine. |
| `"SetLoadingText"` (2nd site) | `0x1007dd90` | A separate, synchronous loading path (`"LOADING_SYNC"`, `"p_loading"` strings nearby) — same event, independent call site. |
| `"toRed"` | `0x105fb0e0` | Guarded by a one-time-init flag. **Tested live in-game**: flipping the exe-side handler from `*param_1 = 1` to `*param_1 = 0` (`reverse/patch_toRed.py`) made all 2D graphics render red-channel-only — a UI/HUD color-channel toggle, not weapon/vehicle init as the nearby reload-timer-shaped math had suggested. `param_1` here is more likely a 2D-renderer/UI state object than a weapon/vehicle instance, and `param_1+0xf8` a "full color enable" style flag. |
| `"PlayerSPFinalize"` | `0x106a60b0` | End of what looks like a player-controller finalize/setup routine. |
| `"CheckDomino"` | `0x109f71b0` | Tiny dedicated function whose sole purpose is this one call — same minimal-wrapper pattern as `RegisterDebugCommands` in the exe. |
| `"MalariaCurve"` (×3) | `0x106a6140` | One function, three back-to-back calls on three distinct curve-stage values (`param_1+0x104`, `+0x10c`, `+0x108`), each from a lookup table (`FUN_10765f60`, indices 10/11/12-13 — plausibly first-attack time / between-attack time / duration). Confirms the exe-side `*param_1 *= <float constant>` handler independently scales each malaria stage. |
| `"AddDiamond"` | `0x1066b660` | Diamond-pickup/reward handler. |

Of the 12 names registered by `FarCry2.exe`, 10 have at least one confirmed live call site. Two don't
line up on both sides:

- **`"SetDefaultTimeOut"`** is confirmed genuinely dead, not just unsurveyed: the exe registers a
  handler (`0x401080`), but `Dunia.dll` never calls `FunctionRegistry_Invoke` with this name anywhere
  — a real orphaned hook, registered but never dispatched.
- **`"carJoke"`** goes the other way: invoked (`0x100e66a0`), but never registered by the exe — the
  observed "not registered → silent no-op" case.
- Bare `"SaveGame"` (as opposed to its alias `"incHB"`, which does have a confirmed call site) hasn't
  turned up in this survey — status less certain than `SetDefaultTimeOut`'s, since a call site could
  plausibly exist outside the ~17 surveyed here.

## What this means

This is not merely a QA-only hook system — real gameplay code (diamond pickup, the main menu, mission
selection, loading-screen text, malaria progression, player finalize, at minimum) calls out to the exe
by name on genuine game events, with a silent no-op fallback if the exe never registered that name
(`"carJoke"` is a concrete observed instance of that fallback in retail). This registry is an internal
instrumentation seam rather than the console itself — the two are distinct, though they meet: the
[developer console](./developer-console.md) dispatches its `Namespace:Function(%%)` commands through
this registry. It also explains why most exe-side handlers were harmless dead stubs in retail: the
call site tolerates them not existing at all. This is empirically confirmed, not
just structurally inferred: registering a handler for `"carJoke"` via a binary patch and testing live
in-game measurably changed engine behavior (car interaction fully disabled).

## Unknowns

- Where `g_pFunctionRegistry` itself gets constructed — presumably inside or just before
  `InitDuniaEngine`, unconfirmed.
- What `FUN_1066b660`, `FUN_106a6140`, and the other callers above actually are/do beyond their role
  in this one chain — none have been named, only their involvement in this dispatch pattern confirmed.
- Whether bare `"SaveGame"` is invoked from a call site outside this survey.
