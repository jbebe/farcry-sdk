---
sidebar_position: 18
---

# `Dunia.dll` — The Developer Console

:::info[Verified in a running game]
Opened and driven in retail Far Cry 2 (Steam v1.03, vanilla install, stock `bin\FarCry2.exe`, no
command-line arguments, no mod layer) on 2026-09-04. The command inventory and gating logic below
were traced first in the disassembly and then confirmed against the running game.
:::

:::note[Corrects an earlier claim]
This page replaces the previous statement that "there is no in-game dev console in Far Cry 2", which
appeared in [modding gotchas](../modding/gotchas.md). That claim was drawn from the `-logFile`
investigation in [command-line args](./command-line-args.md), which correctly established that no
*logging* facility survives in the retail build. The console is a separate subsystem, and it does
survive. See [the overview](./overview.md) for binary identification and the address table.
:::

Retail Far Cry 2 ships a working developer console. It is constructed on the boot path, populated
with commands, bound to a key in the shipped input maps, and reachable by a player on an unmodified
install with no patching.

## Opening it

Press **`~`** or **`` ` ``** during gameplay.

The binding is shipped data, not a leftover. `config\inputactionmapcommon.xml` declares:

```xml
<ActionMap name="common_showconsole">
    <Binding input="kb:~" action="press" signal="toggle_console"/>
    <Binding input="kb:`" action="press" signal="toggle_console"/>
</ActionMap>
```

`inputactionmapsingle.xml` imports that map into its `system` action map, so it is live in
single-player. `inputactionmapcommon.xml` also imports `config\inputactionmapconsole.xml`, which
binds every printable character to a `console_char_*` signal plus Tab (autocomplete), Return
(execute), Up/Down (history) and PageUp/PageDown and the mouse wheel (scroll). Patch 1.03 changes
none of it.

## Boot path

Both halves of the console are reached unconditionally from `RunGame` — there is no flag, build
switch or QA gate on their construction:

```
RunGame (0x10006510)
  └─ InitDuniaEngine (0x10004900)
       ├─ InitializeEngineServices (0x104ce650)
       │    └─ CXConsole ctor (0x10297bf0)   → singleton at DAT_11606280
       └─ Init (0x1065f370)
            └─ InitConsoleCommands (0x1065d660)
                 └─ InitDebugCommands (0x1065b7f0)
```

`CCryEngine::Initialize` (`0x104d0510`) registers a further set directly, and calls
`CXConsole::ExecuteCommandsFromSetting`.

The constructor zeroes two flags that matter later: `console+0x68` (developer mode) and
`console+0x69` (open/closed). The latter is what the editor-facing export
`FCE_Engine_IsConsoleOpen` (`0x1088d420`) returns.

## The two gates

Console elements carry two fields set at registration by the element constructor (`0x102927a0`):
`+0x3c`, a context mask, and `+0x40`, the developer-only flag. Both the executor
(`CXConsole::ExecuteCommand`, `0x10296150`) and the enumerator behind `?` (`0x102970e0`) apply the
same pair of tests:

```c
if (console+0x68 == 0 && element+0x40 != 0) return;   // developer-only, developer mode off
if ((console+0x64 & element+0x3c) == 0)      return;   // context mask mismatch
```

Because the enumerator applies the same filter, a gated command is not merely refused — it is absent
from the listing and unknown to lookup. Typing one reports `Unknown command: %s` rather than a
permissions error.

`console+0x68` is zero for player-typed input. The only code that raises it (`0x10298ac0`) does so
transiently around one internal call and lowers it again, so there is no persistent "developer mode"
reachable from data.

:::info[Verified in a running game]
`?` lists only ungated commands — observed set: `screenshot`, `clear`, `exec`, `showFps` and the
`gfx_*` settings. `console_dump_elements`, which `CCryEngine::Initialize` registers with the
developer flag set, reports "unknown command". This matches the registration flags exactly: the
commands registered with a final argument of `0` (`screenshot`, `clear`, `evict_resources`, `help`)
appear; those registered with `1` (`snapshot`, `snapshot_viewport`, `render_menu_only`,
`console_dump_elements`) do not.
:::

## `#` — the Lua escape

`CXConsole::ExecuteString` (`0x102973c0`) special-cases a leading `#` **before** any command lookup,
passing the remainder of the line straight to `CScriptSystem::ExecuteBuffer` (`0x102a9ff0`):

```c
if (line[0] == '#') {
    CScriptSystem::ExecuteBuffer(line + 1, len, 0);   // raw Lua, no element lookup
}
```

Since the gates live inside `ExecuteCommand`, which this path never enters, `#` reaches the whole
Lua binding surface regardless of the developer flag. This is the practical way into everything the
plain console hides.

Ordinary console commands are themselves Lua. Each registered command stores a template such as
`Game:AddDiamonds(%%)`; `ExecuteCommand` substitutes arguments into `%%`, `%line` or `%1`…`%n` and
executes the result as a Lua buffer. A gated command is therefore reproducible by hand — take its
template and substitute the arguments yourself:

```
#Game:AddDiamonds(500)
```

See [the Lua API surface](./lua-api-surface.md) for the full binding inventory. Manager singletons
follow the `<ClassName>_GetInstance()` idiom used throughout the shipped Domino scripts:

```
#CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay(12, 0)
```

## Command inventory

Commands arrive from four independent sources.

**1. Engine commands**, registered in `CCryEngine::Initialize`: `screenshot`, `snapshot`,
`snapshot_viewport`, `render_menu_only`, `console_dump_elements`, `clear`, `evict_resources`,
`showFps`, `help`. Elsewhere: `quit`, `quitToMainMenu`, `slowframe`.

**2. Function-registry commands** — 57 in total, each registered with a `Namespace:Function(%%)`
dispatch template, recoverable by sweeping the binary for that pattern. They are dispatched through
the [named function-callback registry](./function-registry.md). Grouped by area:

| Area | Commands |
|---|---|
| Level / flow | `load_level` (alias `map`), `EndOfGame`, `InGameCredits`, `PopUpObjective`, `runtests` |
| Player state | `set_health`, `hit_me`, `set_weapon_reliability`, `set_no_weapon_mode`, `debug_set_player_sickness`, `dbg_start_malaria`, `dbg_force_malaria` |
| Cheats | `Cheat_AddDiamonds`, `SetWeaponDifficultyLevel` |
| Movement | `teleport_to_current_objective` |
| Camera | `set_debug_fov`, `SetFPCameraOffsetX/Y/Z`, `SetWeaponCameraOffsetX/Y/Z` |
| Rendering / perf | `draw_method` (wireframe/solid), `Stats`, `SetMaxFrameRate` |
| AI | `aidebugtool` |
| Vegetation | `RTGenesis`, `RTRegen`, `RTDefoliant`, `RTSetWindForce`, `RTSetDeltaTime` |
| Narrative | buddy setters, bonus-plan add/remove/log, `activate_challenge`, `complete_challenge`, `set_winning_faction`, `force_beautifier` |
| Batch | `exec`, `runbatch` |
| Chat (MP) | `say`, `say_team`, `tell` |
| Misc | `debug_phonecall`, `debug_machetetest`, `anim_start_recording`, `get_local_player_id`, `get_mission_manager_status`, `get_buddies_manager_status` |

**3. Config-derived settings.** `CConfig::LoadConfig` registers any config setting carrying a
`console="…"` attribute as a command named `<Group>_<setting>`, built with `sprintf("%s_%s", …)`.
This is why names such as `gfx_ShowFPS` and `Stats_Trace` appear in the game but exist nowhere in
the binary as string literals. In retail, `config\defaultengineconfig.xml` exposes six settings this
way, all in the `Stats` group.

**4. Domino-registered commands.** Shipped mission graphs register their own commands at runtime via
`CDominoConsoleCommandManager::RegisterConsoleCommand` — see [Domino scripts](./domino-scripts.md).

## Batch files and startup hooks

`CXConsole::RunBatch` reads a file and executes it line by line through the same `ExecuteString`
path as typed input, resolving names via `CXConsole::GetBatchFileFullPath` under `scripts\Console\`.
Because it uses the ordinary path, batch files receive no elevated privileges — developer-only
commands inside one are gated exactly as if typed.

Retail ships 14 such files under `scripts\console\` — QA batch lists such as `qc-sp-all-1.console`
and `aidebugview.console`. Several reference commands whose backing config groups do not exist in
the retail data (`Cheat_godmode`, `env_Hour`, `dv_add_debugview`), so they are not expected to work
as shipped.

Two startup hooks exist:

- `CXConsole::ExecuteCommandsFromSetting` executes every entry of a `ConsoleCommands` config section
  at boot.
- `-exec <file>` is parsed on the command line (in `Process`, at `0x10663da2`).

## Live results

:::info[Verified in a running game]

| Input | Result |
|---|---|
| `~` / `` ` `` | Console opens |
| `?` | Lists ungated commands only |
| `console_dump_elements` | `unknown command` — developer-gated |
| `#Game:AddDiamonds(500)` | Works; diamond count increases |
| `#CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay(h, m)` | Works; time of day changes immediately |
| `#Game:SetHealth(100)`, `(25)`, `(0.5)` | All kill the player. The handler reads a **float** defaulting to `1.0f`, so the argument is a 0.0–1.0 fraction rather than a percentage — but partial values still kill, so the working range is not yet established |
| `#Game:ChangeFOV(n)` | A **preset index, not degrees**: `-1` default, `1` narrow, `2` wide, `3` very wide. `100` is rejected |
| `#System:Log("…")` | No visible effect — the binding exists, the sink does not |
| `#SwitchCamera(…)` | Silent no-op, no error — see below |

`#Game:AddDiamonds` is the load-bearing result: as a plain command (`Cheat_AddDiamonds`) it is
developer-gated and unavailable, yet the same call succeeds through `#`. That demonstrates the Lua
path bypasses the gate rather than merely duplicating the public command set.
:::

## Cameras — why there is no free-fly from the console

`SwitchCamera` is a Lua global registered in `RegisterEngineHelpers`. Called with no arguments it
resets to `cameras.Camera.First`; with arguments it selects a named camera. Three camera names exist
in the binary — `Cameras.Camera.First`, `Cameras.Camera.Editor` and `Cameras.Camera.Spectator` — and
`CCameraFreeComponent` and `CCameraGhostComponent` are both real classes with live factories.

:::info[Verified in a running game]
`#SwitchCamera(…)` does nothing from the console — no error, no effect, for every camera name tried.
The implementation bails out before acting: it requires a **script/entity context** (the state a
Domino box executes inside), and the console supplies none. So this is a property of the binding,
not of the argument spelling, and no invocation from the console will reach it.

The camera signals in the shipped input maps (`active_camerafree`, `active_cameraghost`, F3/F4) are
a separate route, and their handler names are absent from the retail PC binary — see Unknowns.
:::

The practical conclusion: retail has the camera *classes*, and the shipped input maps still bind
keys to them, but neither route reaches a free-fly camera in the shipped PC build. Free-fly remains
an editor-only capability, as [editor API surface](./editor-api-surface.md) describes.

## Unknowns

- Whether any route reaches the free/ghost camera in the retail PC build. `SwitchCamera` is ruled
  out from the console (it needs a script context); whether a Domino box, which *does* run inside
  one, can drive it is untested.
- The working range for `Game:SetHealth`. The handler reads a float defaulting to `1.0f`, which
  implies a 0.0–1.0 fraction, yet `0.5` still kills. The value is passed on with two `0xffffffff`
  sentinels and a constant hash (`0x59f2984f`), suggesting it routes through the stim/damage system
  rather than writing a health field directly — which would explain why no partial value survives.
- Whether `console+0x68` can be raised from data rather than by patching, which would expose the
  developer-only commands to `?` and to normal lookup — and with it `console_dump_elements`, whose
  output (`ConsoleElementsDump.txt`) would be an authoritative, engine-generated command table.
- Whether `-exec` and the `ConsoleCommands` config section function in retail; both are traced but
  neither has been run.
- The context mask semantics of `element+0x3c` versus `console+0x64`.
- `inputactionmapcommon.xml` binds several QA signals — `create_issue`, `scry_openclose`,
  `debugmenu`, `active_camerafree`, `active_cameraghost`, `cheatpause_toggle`, `debug_logstats` —
  whose names do not appear in the retail PC binary, suggesting the handlers were stripped while the
  data kept the bindings. `toggle_console` is present, which is why the console itself works.
