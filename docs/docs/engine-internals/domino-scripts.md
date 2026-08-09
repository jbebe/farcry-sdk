---
sidebar_position: 10
---

# Domino Scripts — The Node Library and What Missions Actually Cover

:::info[Confirmed via a leaked prototype build's file manifest]
Source: `tools/third-party/Far Cry 2 Sep 8 2008 prototype/common.nfo` — a plaintext
`<FatInfo><File Path="..." Crc="..." FileTime="..."/></FatInfo>` sidecar manifest that ships next to
every `.fat` archive in this prototype, listing every packed file's path without needing to touch the
binary `.fat`/`.dat` format at all. This is a real, complete file listing, not RE-derived — but it's
*only* filenames; no script text has been extracted (see "What's not here yet" below). Cross-referenced
against the binary-side Domino architecture already documented in [Engine
Architecture](./architecture.md#domino--lua-loads-through-the-same-generic-vfs-as-every-other-asset)
and [the Lua API surface](./lua-api-surface.md).
:::

`common.nfo` lists **1,069 `.lua` files** under `domino\`, split cleanly into two roles that map
directly onto the `CDominoBox*` classes already found in the binary: `domino\system\` is a fixed
library of reusable node types, and `domino\user\` is every mission's own authored graph, built by
wiring those nodes together.

## `domino\system\` — Domino is node-based visual scripting, not hand-written Lua

229 files (~115 distinct node types, each shipped as both `name.lua` and a `name.debug.lua`
instrumented twin — a real build convention, not a naming accident). Every file is a **single reusable
node type** — this is the concrete confirmation that "Domino" is FC2's node-based visual-scripting
system (its own in-house Blueprint/Kismet equivalent), and that a "box" in `CDominoBoxInstance::CreateBox`
/`CDominoBoxResource::RegisterBox` (see [Engine Architecture](./architecture.md)) is literally one
instance of one of these node types dropped into a mission's graph. A level designer wires nodes
together in a visual editor; each node's actual behavior is one of these `.lua` files.

Grouped by what they do (representative members, not exhaustive — the full list is in `common.nfo`
directly):

| Category | Representative nodes |
|---|---|
| Flow control | `foreach`, `switch`, `sequence`, `sequencetimer`, `delay`, `onceonly`, `multipleand`, `indexlist`, `outputorder`, `startscript`, `stopscript`, `closescript` |
| Comparisons / conditions | `compareanims`, `compareboolean`, `compareentity`, `comparefloats`, `compareintegers`, `comparestrings`, `testifnil` |
| Variables / data | `setboolean`, `setfloat`, `setinteger`, `setstring`, `setentity`, `floatarithmetics`, `integerarithmetics`, `stringconcatenate`, `random`, `randomboolean`, `randomfloat`, `randominteger` |
| Mission/story state | `missioncompleted`, `missionfaction`, `missionsubverted`, `selectmission`, `setcurrentmission`, `setmissionstate`, `setlibrarymissionstate`, `givemissionreward`, `heardbriefing`, `getcurrentgreeting` |
| Buddy system | `assignbuddy`, `buddyavailability`, `buddybetrayal`, `buddydied`, `buddyrescue`, `buddywager`, `spawnbuddy`, `spawnprimarybuddy`, `removebuddy`, `killbuddy`, `defencereversal`, `setbuddysavepointmode`, `cheat_setrescuebuddy` |
| Faction / world state | `winningfaction`, `changemaparmy`, `bypass_setwinningfaction`, `changeworld`, `overridemap`, `desertstorm` |
| Environment / time | `gettimeofday`, `settimeofday`, `overrideenvironmentfog`, `overrideenvironmentwind`, `overrideenvironmentlighting`, `overrideenvironmentadaptivebloom`, `setwaterlevel` |
| AI / social | `socialregion`, `forcesocialregiontocombat`, `detectsocialengagement`, `sendsocialeventtopawn`, `scriptedaimode`, `navmeshdeadzone`, `reinforcementregion`, `spawnreinforcement`, `lookattarget`, `shootattarget` |
| Pawn / animation / interaction | `playanim`, `playsyncanim`, `interruptanim`, `animalfollowpath`, `vehiclefollowpath`, `moveto`, `teleportentity`, `playemotion`, `pawninteraction`, `door`, `usableentity`, `compoundobject`, `particlesystem` |
| Combat / health | `healthevents`, `playerheal`, `sendpiercestim`, `dospecialcharactercombat`, `vehicledamage`, `manageweapon`, `manageinventory`, `pickupmissionitem`, `weaponbazaar` |
| Player state / misc | `setmalaria`, `playermalariaevents`, `getmalariapillscount`, `changehealpreference`, `sethudmode`, `setscripteddeathmode`, `jackaltapes`, `partnertapes`, `safehousestatus`, `bedroll`, `convoymission`, `bargeassault`, `dentalplan` |
| UI / messagebox | `popupconfirmationmessagebox`, `popuptutorialmessagebox`, `floatingtutorialmessagebox`, `popupendofgame`, `popupingamecredits`, `texttoscreen`, `consolecommand` |
| Audio | `playsound`, `playmusic`, `playbark`, `interruptbark`, `setmissionbarkbankstate`, `setmusicstate`, `soundmixing`, `camerashakeandrumble` |
| Entity/world plumbing | `getentityname`, `getentityinprefab`, `removeentity`, `setvisibility`, `setcamera`, `triggerstate`, `inputlistener`, `messagelistener`, `stopdominobrain`, `achievementdata` |

This lines up closely with — and gives concrete node-level granularity to — the global Lua functions
already catalogued in [the Lua API surface page](./lua-api-surface.md) (e.g. `SpawnReinforcementScenario`,
`StartDefenceReversal`, `PopUpObjective`, `PlayEmotion` all have an obvious node-name counterpart here).

## `domino\user\` — 832 authored mission graphs

Named by **mission code**: `a<act><type><number>_<slug>`. The type letter is a real, consistent
taxonomy:

| Type | Count | Meaning | Example |
|---|---|---|---|
| `lm` | 82 | Library mission (side/job-board missions) | `a2lm12_bunkerbuster` |
| `sm` | 81 | Story mission (main faction missions) | `a2sm06_hornetnest` |
| `bu` | 25 | Buddy mission — including the game's opening tutorial | `a1bu00_tutorial`, `a1bu02_arena` |
| `gm` | 5 | All one code, `a1gm00_grindelivery` — the opening delivery/intro sequence specifically | `a1gm00_grindelivery` |

Plus dedicated subfolders for content that isn't a single mission's graph:

| Folder | Count | Content |
|---|---|---|
| `sidemissions\` | 76 | Safe-house/buddy-management side logic (Mike's Place buddy spawning, removal, health tracking) |
| `gyms\` | 51 | Isolated test/sandbox graphs (e.g. `gym_buddywager_twobuddy`) — QA scaffolding, not shipped mission content |
| `debugboxes\` | 36 | Per-world mission-state-skip triggers for testers (e.g. `bypass_world1_finished_4_librarymissions`) |
| `randomencounters\` | 28 | The random-encounter system (roadblocks, ambushes) |
| `partnerspecialmissions\` | 25 | Buddy-specific special/side missions |
| `dlc\` | 16 | DLC-specific mission graphs |
| `savepoints\` | 8 | Save-point logic |
| `fasttravel\` | 4 | Fast-travel logic |
| `openingsequence\` | 2 | The game's opening cinematic/intro |

A handful of loose `ubidays.*` files (`techdemo`, `stagedemo`, `playdemo`, `longdemo`, `benchmarkdemo`,
`briefing_warren`, `briefing_frank`) are trade-show demo scripts — internal Ubisoft event build content
(the name is almost certainly "Ubi Days," an internal Ubisoft showcase event), not shipped retail
content. A nice bit of archaeology: pre-release demo builds got their own dedicated mission graphs,
separate from the real campaign.

## What a graph file actually contains

:::info[RE-verified against the retail script corpus]
The sections below are derived from all 1,072 extracted `domino\` scripts, not from the prototype's
filename manifest. Retail Far Cry 2 ships these as **plain Lua source, not bytecode** — `fc2.hashlist`
resolves 1,072 `domino\` paths and every one extracts as readable text with its comments intact.
Reconstruction is implemented in `tools/JackAll/src/JackAll.Tools/Domino/` and cross-checked against the
debug twins described below.
:::

### `system\` nodes declare themselves in a comment header

Every one of the 234 `system\*.lua` files opens with a `-- DOMINO REFLECTION BOX START ... END` block —
XML-in-comments that is literally the visual editor's palette entry for that node:

```lua
-- DOMINO REFLECTION BOX START
--
-- <Display Category="Script Flow" Text="Delay"/>
--
-- <ControlIn  Name="Start"/>
-- <ControlIn  Name="Pause"/>
-- <DataIn     Name="Seconds"      Type="Core|float"/>
--
-- <ControlOut Name="TimeElapsed"  Delayed="true"/>
--
-- DOMINO REFLECTION BOX END
```

Coverage is 234/234 and the vocabulary is closed: six tags, 15 `Category` values, and exactly 11
`Type` values — `Nomad|entity` (160 uses), `Core|string` (118), `Core|int` (109), `Core|bool` (60),
`Core|float` (49), then `Nomad|animation`, `Nomad|Sound`, `Nomad|SoundType`, `Nomad|SoundMixing`,
`Nomad|texture` and `Core|boxclass` in single digits. Five nodes have `Dynamic` pins (`switch`,
`random`, `indexlist`, `multipleand`, `outputorder`), 78 declare at least one `Delayed="true"`
control-out, and 129 are `<Stateless/>`. `Name` attributes are always Lua identifiers — never the
spaced display form.

`user\` sub-graphs have **no** reflection header, so a graph used as a box by another graph has to have
its interface inferred from its generated code.

### `user\` graphs are flattened codegen, not hand-written Lua

Each file's header names its generator and its lost source document:

```lua
-- Generated by BlackBox 2.1.2.9   Plugin: Domino 1.0.1.0
-- Script document: R:\main\data\Domino\User\A1LM02_ReapSew.domino.xml
-- User graph: A1LM02_BriefingSubvPawnBrief
```

The `.domino.xml` originals — which held box positions and the real graph layout — are not in any
shipped build. The generated code is rigidly mechanical, which is what makes reconstruction possible:

| Idiom | Meaning |
|---|---|
| `self[N] = cbox:CreateBox(path)` | A persistent box. `N` is the box's original `.domino.xml` ID. |
| `self.box_<Type>_<N> = cbox:CreateBox(path)` | The same thing under a descriptive name — a codegen variant, used in roughly half the corpus. |
| `Boxes[PathID(path)]` | A **pooled** slot: one shared runtime instance per node type, reconfigured and re-fired at each use site. 763 files use these. |
| `self[N].Pin = self._type.f_N_Pin` | A control connection — box `N`'s out-pin wired to the generated continuation that runs next. 17,732 such `f_N_<pin>` handlers exist. |
| `self[M]._type.Pin(self[M])` | Firing box `M`'s named control-in. |
| `self[N].Pin = DummyFunction` | An out-pin left unconnected in the editor. |
| `export:en_N()` | A generated "enter node N" prologue that pushes every data-in onto box `N` immediately before it fires. 2,725 of these. |

Graph sizes: median 10 boxes, p90 48, maximum 232
(`a1bu00_tutorial.a1bu00_storymission.lua`).

### Data flows through graph variables, not box to box

Only **20 places in the entire corpus** wire one box's data-out directly to another's data-in. Instead
a value is parked on a graph-level field and picked up in a different handler:

```lua
function export:f_29_Out()          -- producer, in one handler
    self.BuddyPawn = self[29].SpawnedBuddy;
end;
function export:en_18()             -- consumer, in another
    self[18].Pawn = self.BuddyPawn;
end;
```

There are ~1,700 such producer reads and ~5,300 consumer writes. Neither statement is an edge on its
own, so any tool that wants to show data flow has to join them through the variable name. Two wrinkles
matter: a variable no box writes is the graph's own **data input** supplied by a parent graph, and a
variable written by several handlers needs control-flow reachability to attribute — with the important
special case that those writers are usually several occurrences of *the same operation* repeated per
story branch (four `GetLocalPlayer` boxes all feeding `self.Player`), which is one logical source
rather than four rival ones.

### The `.debug.lua` twins are a topology oracle

Every graph ships twice, `name.lua` and `name.debug.lua`. The twin is the same graph compiled with
instrumentation that restates every control connection verbatim — **16,005 of them across the corpus**:

```lua
CDominoManager_GetInstance():TraceConnection(
  "DocumentContainer|R:\\main\\data\\Domino\\User\\A1LM02_ReapSew.domino.xml|@A1LM02_BriefingSubvPawnBrief|1006789459",
  "box_SCRIPTEDPAWN_WAIT_BECKON_GREET_1.Greet finished",
  "box_SCRIPTEDPAWN_DIALOG_INTERACT_2.Start", ...)
```

That recovers four things the release file discards:

- **The connection's original `.domino.xml` ID** (the trailing number in the container string).
- **Human pin labels**, spaces and all. The generated Lua only has the mangled identifier; the mangling
  rule is that every character outside `[A-Za-z0-9_]` becomes `_`, and a name then starting with a digit
  gains a leading `_`. So `"4a. Wager finished, Buddy healthy"` is `_4a__Wager_finished__Buddy_healthy`.
- **Box names**, formed as `box_<Display Text with spaces→underscores>_<original ID>` — so
  `<Display Text="Set Entity"/>` at box 2 becomes `box_Set_Entity_2`. Since the ID equals the `self[N]`
  slot, this names every persistent box in the release file.
- **Confirmation that pooled boxes were separate boxes in the editor.** A graph whose release code only
  ever mentions one `Boxes[PathID("Domino/System/SetEntity.lua")]` has its twin naming
  `box_Set_Entity_1` through `box_Set_Entity_4` — four distinct boxes sharing one runtime slot.

Twins cover control connections only; data links never appear in them.

**As a check on reconstruction this is decisive**: across all 406 extracted graphs that have a twin,
the control edges inferred from the release file match the twin's connection table exactly, with no
disagreements. `tools/JackAll` runs that comparison as a test
(`DebugTwinTests.Every_reconstruction_agrees_with_its_debug_twin_on_box_to_box_control_flow`); point it
at a full extraction with `JACKALL_DOMINO_CORPUS`.
