---
sidebar_position: 3
---

# `Dunia.dll` — The Named Function-Callback Registry

:::info[Verified via reverse engineering]
See [the overview](./overview.md) for binary identification and the address table referenced
throughout this page.
:::

The launcher's callbacks (`AddDiamond`, `MalariaCurve`, `SaveGame`, etc. — see [the launcher exe
notes](./launcher-exe.md)) are registered into, and dispatched from, a single global registry inside
`Dunia.dll`. The Linux server's symbols name it **`CSecurityManager`**: `RegisterFunction` stores a
callback and `InvokeProtection` calls one. These are copy-protection hooks — the engine hands a
callback a deliberately wrong value, and the stock handler puts it right — so they are live gameplay
paths, not debug commands **(RE-verified)**.

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

All ~17 call sites engine-wide (string literal read either from the caller's decompiled pseudocode,
or from the raw string data where the decompiler fails to propagate it):

| Event name | Caller address | Context |
|---|---|---|
| `"incHB"` | `0x1065aea0` | The per-frame tick: the argument is the frame time as float bits, and a return above `512.0` clears bit `0x8` of that frame's engine update mask, skipping the AI engine's pre- and post-update, the Domino delay, sound and sequence managers, steering and nav for that frame (`0x1064d246` → `0x104c2510` in the GOG build, **RE-verified**). The stock handler returns 0, so nothing is skipped. **Tested live in-game** (`reverse/patch_incHB.py`): the value is passed **by raw bits through EAX**, not FPU/ST(0) convention. A handler using `FLD` crashes the game (an unpopped x87 stack push on every call overflows the 8-deep FPU stack within seconds); a pure `MOV EAX,[ESP+4]` echo returns the frame time itself, well under 512, so it changes nothing visible. |
| `"carJoke"` | `0x100e66a0` | Not one of the 12 names registered by `FarCry2.exe` — always hits the silent no-op path in retail. A binary patch registering a handler that writes `false` into the veto flag `cStack_6e` (`reverse/patch_carJoke.py`) was tested live in-game and **confirmed to fully disable car interaction** — the veto path produces the identical outcome to the function's separate "no valid interaction target" early-return, meaning `FUN_100e66a0` is a vehicle-entry/interaction handler and `carJoke` is a full gate over it. |
| `"InitializeUseableEvent"` | `0x106c44a0` | `CDoor::OnEvent` answering whether a door can be used: the door becomes usable only when the callback writes 1, and a vehicle blocking the doorway still vetoes it. |
| `"mapJoke"` | `0x106f07d0` | `CGadgetMapStrategy::SpawnMarker`: a 0 return leaves that named marker off the map gadget. Shares a stub with `LoadGame` on the exe side. |
| `"LoadGame"` | `0x1072ef00` | `CCampaignGameFile::LoadGameFileData`: a 0 return — or a security status already set — skips restoring every subsystem and the `PersistenceDB`, and sets the status flag. The function still reports success to its caller. |
| `"SelectStoryMission"` | `0x10755ee0` | `CFCXMissionManager::SelectMission` passes the count of completed story missions minus 10; the stock `+10` puts it back, and the count picks the next mission. |
| `"SelectLibraryMission"` | `0x10755ee0` | The same for library missions, minus 21 (`0x15`). |
| `"menuJoke"` | `0x108c8830` | `CFCXMainPage::Setup` passes the class id of `CFCXStoryModePage`; the return is the page the Story Mode button opens. |
| `"SetLoadingText"` | `0x100d1370` | `CGameLoadingScreen::LoadPackage`: the argument is a wide-string buffer; empty shows the localized `Generic/LOADING`, anything else is shown as written. |
| `"SetLoadingText"` (2nd site) | `0x1007dd90` | `CGameUpdatingScreen::SynchCallback`, the same with `LOADING_SYNC` as the default. |
| `"toRed"` | `0x105fb0e0` | Guarded by a one-time-init flag. **Tested live in-game**: flipping the exe-side handler from `*param_1 = 1` to `*param_1 = 0` (`reverse/patch_toRed.py`) made all 2D graphics render red-channel-only — a UI/HUD color-channel toggle. `param_1` here is more likely a 2D-renderer/UI state object than a weapon/vehicle instance, and `param_1+0xf8` a "full color enable" style flag. |
| `"PlayerSPFinalize"` | `0x106a60b0` | `CFCXCountersComponentPlayerSP::Finalize`: the stock handler writes `1.0f` into a multiplier the player's damage handling reads. |
| `"CheckDomino"` | `0x109f71b0` | Reached from the Lua function `EnableCheck(bool, int)`; the callback's result is ignored. |
| `"MalariaCurve"` (×3) | `0x106a6140` | `CFCXCountersComponentPlayerSP::SetSicknessLevel`: three calls on the malaria time curves — first attack (`+0x104`), time between attacks (`+0x10c`) and minor-attack duration (`+0x108`) — but not on the minor-attack count. The stock handler multiplies each by 60. |
| `"AddDiamond"` | `0x1066b660` | `CEconomyComponent::AddDiamond`: the stock handler adds the pickup to `DiamondCount`, the wallet — the only arithmetic increase of it (saves and reflection can still set it). |

15 names are registered by `FarCry2.exe`, over 12 functions. `InvokeProtection` has exactly the 17
call sites above, which name 14 different callbacks; nothing else reaches the registry **(RE-verified)**.
Three names don't line up on both sides:

- **`"SetDefaultTimeOut"`** is registered (`0x401080`) but never invoked; the string does not even
  exist in `Dunia.dll`.
- Bare **`"SaveGame"`** is registered but never invoked either; the engine's only `SaveGame` string
  belongs to the Lua `Game:SaveGame` function.
- **`"carJoke"`** goes the other way: invoked (`0x100e66a0`), but never registered by the exe — the
  observed "not registered → silent no-op" case.

## What this means

Real gameplay code — the frame tick, diamond pickup, the main menu, mission selection, save loading,
doors, map markers, loading-screen text, malaria and the player's damage — calls out to the exe by
name and depends on the stock handler's answer. A launcher that drops a handler does not get a no-op:
missions stop advancing, doors stop opening, malaria attacks come 60 times too often. This registry
is separate from the [developer console](./developer-console.md), whose `Namespace:Function(%%)`
commands run as Lua.

The registry is constructed in `InitializeEngineServices` and deleted in `ShutdownEngineServices`.
