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
`+0x3c`, a context mask, and `+0x40`, the developer-only flag. One predicate tests both, and the
compiler emitted it **six times** — once out of line at `0x10291ae0`, and inlined at five call
sites. Each copy is the same pair of tests:

```c
if (console+0x68 == 0 && element+0x40 != 0) return;   // developer-only, developer mode off
if ((console+0x64 & element+0x3c) == 0)      return;   // context mask mismatch
```

The copies matter individually, because they gate different things and lifting one does not lift the
others. The four on the console's own path are:

| Copy | Site | Gates |
|---|---|---|
| out of line, called by `ExecuteString` | `0x10291ae8` | whether a looked-up name counts as **found** |
| `CXConsole::ExecuteCommand` | `0x1029616d` | whether a found command **runs** |
| enumerator loop 1 | `0x1029714d` | whether a command is **listed** by `?` |
| enumerator loop 2 | `0x102971d7` | the same, for the second collection |

The remaining two (`0x10292ed4`, `0x10292f28`) sit behind element lookups the engine makes for
itself — `InitCVars`, `SetupOnlineEngineRegistery`, per-frame `Update` — and are not on the path
from a typed line to a command.

Because the lookup copy runs first, a gated command is not merely refused — it is unknown to lookup
and absent from the listing. Typing one reports `Unknown command: %s` rather than a permissions
error. Lifting only the listing copies makes hidden commands *visible but still unrunnable*, which
is the distinction the four-way split above exists to capture.

`console+0x68` is zero for player-typed input. The only code that raises it (`0x10298ac0`) does so
transiently around one internal call and lowers it again, so **there is no persistent "developer
mode" reachable from data** — no config setting, command-line flag or batch file turns the hidden
commands on. Lifting the gate takes a code change.

What the gate reduces to is a single `jnz` that skips the developer test when the flag is set,
repeated at three sites: one in `ExecuteCommand` and one in each of the enumerator's two loops.
Turning each into `jmp` — one byte, `75` → `EB`, leaving the displacement alone — takes the test out
of the path without touching the flag, and leaves the context mask working. UFCP ships that as an
opt-in `Developer console` setting; see `mods/UFCP/src/options/developer_console.cpp`.

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

:::info[Verified in a running game]
`console_dump_elements` is itself developer-gated, so it only runs with the gate lifted — UFCP's
`Developer console` option is one way. It writes `ConsoleElementsDump.txt` to the save folder
(`Documents\My Games\Far Cry 2\`), one command name per line. On retail v1.03 it lists **416
commands** — the engine's own account of its console, and the authority for this section, so
regenerate it rather than trusting the summary below if the two ever disagree:

| Group | Count | What it is |
|---|---|---|
| `gfx_*` | 178 | Render settings, from config entries carrying `console="…"` |
| `domino_*` | 117 | Registered at runtime by shipped mission graphs |
| everything else | 121 | Hand-registered commands and the other config groups |

Highlights not obvious from the disassembly:

- **The cheats are console commands**, not only `-GameProfile_*` launch flags: `cheat_GodMode`,
  `cheat_UnlimitedAmmo`, `cheat_UnlimitedReliability`, `cheat_AllWeaponsUnlock`,
  `cheat_add_playerweapon`, `Cheat_AddDiamonds`, `cheat_set_pillar`. See
  [cheats](../modding/guide/cheats.md) for the launch-flag form of the same switches.
- **Time of day and weather are direct commands** — `env_Hour`, `env_Minutes`, `env_Seconds`,
  `env_TimeScale`, `env_StormHour`, `env_WindDir`, `env_WindForce`, plus `set_weather`,
  `set_weatherHour`, `set_weatherTimeScale`, `set_stormFactor`, `set_windDir`, `set_windForce`. No
  Lua needed.
- **Input preferences** the options screen never exposes: `look_Sensitivity`, `look_Sensitivity_x`,
  `look_Sensitivity_y`, `look_Invert_x`, `look_Invert_y`, `look_HelpCrosshair`,
  `mouse_Smoothness`, `mouse_Smoothness_Ironsight`.
- `Magma_ToggleHUD`, `ai_IgnorePlayer`, `ai_DisplayAILimitInfo`, `rt_lod_freeze`,
  `hack_draw_counters`, `scriptcallbacks`, `SetSetting`, `net_log_enable`/`disable`,
  `snd_oppeak`/`snd_opstat`.
:::

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

## Why an unlocked console still has inert commands

:::info[Verified in a running game]
With the developer gate lifted, roughly half the listed commands do something and the rest do not.
Two mechanisms account for that, and neither is the developer flag:

**`domino_*` needs its graph loaded** — 117 of the 416, every one an entry point registered by a
shipped mission graph (`AK47`, `A2SM07`, `StartLibraryMissions`, `Diff1`). Registration happens when
the owning graph loads, so in the wrong act or world the name is listed and does nothing.

**The context mask still applies.** The console's constructor sets `console+0x64 = 1`, and every
element carries its own mask at `element+0x3c`; a command whose mask does not intersect the active
context is refused no matter what the developer flag says. This is the gate that keeps
multiplayer-only and editor-only commands out of a single-player session, and lifting it is a
separate change from lifting the developer flag.

A third, milder case: some `gfx_*` settings are read at startup or level load, so setting one takes
effect on the next load rather than immediately.
:::

## Batch files and startup hooks

`CXConsole::RunBatch` reads a file and executes it line by line through the same `ExecuteString`
path as typed input, resolving names via `CXConsole::GetBatchFileFullPath` under `scripts\Console\`.
Because it uses the ordinary path, batch files receive no elevated privileges — developer-only
commands inside one are gated exactly as if typed.

Retail ships 14 such files under `scripts\console\` — QA batch lists such as `qc-sp-all-1.console`
and `aidebugview.console`. Checked against `ConsoleElementsDump.txt`, most of what they call still
exists: `cheat_GodMode`, `cheat_UnlimitedAmmo`, `set_current_primary_buddy`, `env_Hour`,
`env_TimeScale`, `gfx_DisableShadowGeneration`, `gfx_ShowFPS` and `gfx_SceneObjectMinSize` are all
present (command lookup is case-insensitive, so the files' spellings match). What is gone is the
debug-view and QA instrumentation: `dv_add_debugview`, `qc_ShowPlayerPos`, `Cheat_speed_factor`,
`gfx_KillLodScale`, `SetRank`. So these batches are largely live, minus their overlays — with the
caveat that every command in them is developer-gated.

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
| `console_dump_elements` | `unknown command` — developer-gated. With the gate lifted it runs and writes 416 command names to `ConsoleElementsDump.txt` |
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

The command dump closes the third route: of the 416 commands the engine lists, **none is a free or
ghost camera**. The only camera-adjacent entries are `set_debug_fov`, the six
`SetFPCameraOffset*`/`SetWeaponCameraOffset*` nudges, `gfx_WidescreenFOV`, and
`gfx_UpdateCullingCamera`/`gfx_UpdateRenderCamera` — culling-freeze toggles for inspecting what the
renderer culls, not a camera you can fly.

The practical conclusion: retail has the camera *classes*, the shipped input maps still bind keys to
them, and the console can reach 416 commands — and not one of those three routes reaches a free-fly
camera in the shipped PC build. Free-fly remains an editor-only capability, as
[editor API surface](./editor-api-surface.md) describes.

The one untested route left is a Domino box. `SwitchCamera` fails from the console only because it
needs a script context, and a Domino box runs inside one — so a custom graph calling
`SwitchCamera("Cameras.Camera.Editor")` is the remaining lead. None of the 117 shipped `domino_*`
commands does anything camera-related, so this would mean authoring a graph, not triggering one.

## Unknowns

- Whether any route reaches the free/ghost camera in the retail PC build. `SwitchCamera` is ruled
  out from the console (it needs a script context); whether a Domino box, which *does* run inside
  one, can drive it is untested.
- The working range for `Game:SetHealth`. The handler reads a float defaulting to `1.0f`, which
  implies a 0.0–1.0 fraction, yet `0.5` still kills. The value is passed on with two `0xffffffff`
  sentinels and a constant hash (`0x59f2984f`), suggesting it routes through the stim/damage system
  rather than writing a health field directly — which would explain why no partial value survives.
- Whether `-exec` and the `ConsoleCommands` config section function in retail; both are traced but
  neither has been run.
- The context mask semantics of `element+0x3c` versus `console+0x64`. The console starts at `1`, and
  a mismatch refuses the command, but which bit means single-player, multiplayer or editor — and
  which commands carry which mask — has not been enumerated.
- `inputactionmapcommon.xml` binds several QA signals — `create_issue`, `scry_openclose`,
  `debugmenu`, `active_camerafree`, `active_cameraghost`, `cheatpause_toggle`, `debug_logstats` —
  whose names do not appear in the retail PC binary, suggesting the handlers were stripped while the
  data kept the bindings. `toggle_console` is present, which is why the console itself works.
