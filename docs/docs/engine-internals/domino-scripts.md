---
sidebar_position: 10
---

# Domino Scripts — The Node Library and What Missions Actually Cover

:::info[Confirmed via a leaked prototype build's file manifest]
Source: `tools/third-party/Far Cry 2 Sep 8 2008 prototype/common.nfo` — a plaintext
`<FatInfo><File Path="..." Crc="..." FileTime="..."/></FatInfo>` sidecar manifest that ships next to
every `.fat` archive in this prototype, listing every packed file's path without needing to touch the
binary `.fat`/`.dat` format at all. This is a real, complete file listing, not RE-derived — but it's
*only* filenames; the script contents below come from the retail corpus. Cross-referenced against the
binary-side Domino architecture documented in [Engine
Architecture](./architecture.md#domino--lua-loads-through-the-same-generic-vfs-as-every-other-asset)
and [the Lua API surface](./lua-api-surface.md).
:::

`common.nfo` lists **1,069 `.lua` files** under `domino\`, split cleanly into two roles that map
directly onto the `CDominoBox*` classes in the binary: `domino\system\` is a fixed
library of reusable node types, and `domino\user\` is every mission's own authored graph, built by
wiring those nodes together.

## `domino\system\` — Domino is node-based visual scripting, not hand-written Lua

229 files (~115 distinct node types, each shipped as both `name.lua` and a `name.debug.lua`
instrumented twin — a real build convention, not a naming accident). Every file is a **single reusable
node type** — confirming that "Domino" is FC2's node-based visual-scripting
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
catalogued in [the Lua API surface page](./lua-api-surface.md) (e.g. `SpawnReinforcementScenario`,
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
content.

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
| `export:ex_N()` | An "exit node N" epilogue that copies box `N`'s data-outs into graph variables, called from box `N`'s handlers. |
| `self[M]._type.Condition(self[M], k)` | Firing slot `k` of a `Dynamic="True"` control-in. 569 of these, all on MultipleAND. |
| `self[M]._DynamicAnchors = { Condition = 2, }` | How many slots each dynamic pin has - `Condition`, `Out` and `Output`. |
| `self._sld_<Pin>_<N>` | A generated temporary holding box `N`'s data-out for a direct box-to-box data link. |

Graph sizes: median 10 boxes, p90 48, maximum 232
(`a1bu00_tutorial.a1bu00_storymission.lua`).

### Every generated name carries an editor box ID

BlackBox names each function after the box it serves: `f_N_Pin` runs when box `N`'s control-out `Pin`
fires, `en_N` configures box `N`, `ex_N` reads box `N`'s outputs. This holds for pooled boxes too, and it
is the only place a pooled box's identity survives in the release file:

- A pooled slot wired as `Boxes[...].Out = self._type.f_1_Out` is editor box 1, whatever handler it is
  configured in. Across the corpus, all 8,047 persistent wires `self[M].Pin = self._type.f_N_...` have
  `N = M`.
- Every one of the 4,536 `self._type.en_N(self);` calls followed by a persistent fire fires `self[N]`.
- A handler reads the pooled slot it continues before it configures anything: `f_2_Out` reading
  `Boxes[PathID("…SetEntity.lua")].Target` is reading box 2's output, even when the same handler then
  configures the slot again as box 1.
- BlackBox writes a pooled box's configuration inline only when one function fires it; a box fired from
  several functions gets an `en_N` prologue instead. So an inline configuration with no wire naming it is
  still exactly one editor box.

Retail uses 16,768 boxes. JackAll recovers the ID of 16,738 of them from the release code alone, or
from the debug twin for the 871 pooled boxes nothing else names. The 30 left over sit in the 21 graphs
that ship without a twin.

### A graph's own pins

A user graph is itself a box with pins:

- **Control-ins** are its exported functions that are neither lifecycle (`Create`, `Init`, `ShutDown`,
  `LuaDependencies`) nor generated (`f_`, `en_`, `ex_`), for example `In`, `Start`, `Cancel`, `Enable` and
  `Disable`. A parent graph fires them, or the engine does for a mission's top graph.
- **Control-outs** are declared as `self.Pin = DummyFunction;` in `Init`, left for a parent to overwrite,
  with an empty `function export:Pin() end` stub under `-- Empty out anchor definitions`. The graph fires
  one with `self:Pin();`.

211 of the 427 graphs have more than one control-in. Each starts its own chain, which is why one file
often reads as several unconnected graphs.

### Pooled slots are shared, and keep their values

`Boxes[PathID(path)]` is one runtime instance per node type, shared by every box of that type. A box
that leaves a data-in unset runs with whatever value the slot last held, set by any graph. BlackBox
normally writes `nil` for every data-in left empty, but not always:

- 147 pooled `ObjectiveState` boxes never set `ShowPopup`, which the node declares, so each runs with
  whatever `ShowPopup` the last `ObjectiveState` left on the slot.
- 38 `SetMusicState` boxes set `MissionId`, which the node doesn't declare (it takes `WorldId`).

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
matter:

- A variable no box writes is the graph's own **data input**, supplied by a parent graph.
- A variable written by several handlers needs reaching definitions to attribute. Walking control flow
  backwards from the consumer, every writer met before another writer of the same variable is a source.
  Writers on different story branches are therefore all sources, each for its own branch, as with four
  `GetLocalPlayer` boxes all feeding `self.Player`. Writers on parallel event chains, which no control
  path connects to the reader, stay genuinely undetermined: the value is whichever ran last.

### The `.debug.lua` twins are a topology oracle

Every graph ships twice, `name.lua` and `name.debug.lua`. The twin is the same graph compiled with
instrumentation that restates every control connection verbatim. Each `TraceConnection` sits directly
before the fire it describes, so the twin traces every fire, **21,625 of them across the corpus**, entry
pins and pooled boxes included. Graph exits (`self:Pin();`) are never traced:

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

A twin's functions come in the same order as the release file's. With the traces dropped, each twin
function says exactly what its release counterpart says once the twin's names are rewritten:
`self.box_X_N` becomes `self[N]`, `f_box_X_N_Pin` becomes `f_N_Pin`, `OnEnter_box_X_N`/`OnExit_box_X_N`
become `en_N`/`ex_N`, `_sld_Pin_box_X_N` becomes `_sld_Pin_N`, and sub-graphs named `X.debug.lua` become
`X.lua`. That makes trace *k* of a function the description of the function's *k*-th fire.

**As a check on reconstruction this is decisive**: in all 406 graphs that have a twin, every one of the
21,625 traced fires matches the reconstructed edge that fires it, source and target alike. That
includes pooled boxes and the graph's own control-ins. `jackall-cli domino check <extracted>\domino\user`
repeats the comparison over a full extraction, and `DebugTwinTests` runs it on named fixtures.

### Checking a graph before it ships

`jackall-cli domino check` and `jackall-cli mod lint` check user graphs with the rules below. For a graph
a mod layer replaces, `mod lint` reports only what the edit introduced. Counts are over the 427 retail
graphs. A rule is an error when what it finds breaks at runtime; every error-level hit in retail is a
real bug.

| Rule | Severity | Retail | What it finds |
|---|---|---|---|
| `undeclared-in` | error | 3 | A fire on a control-in the box type doesn't declare, which calls nil. All 3 are in the QA gym `gym_testsubvertbriefing.briefingsubv`, which fires `GameElementObjective.SetAsOpen`/`SetAsSucceeded`. |
| `dynamic-index-range` | error | 0 | A dynamic slot beyond what `_DynamicAnchors` gives the pin. |
| `unregistered-box` | error | 0 | A box type `Create` doesn't register. |
| `stateful-pooled` | error | 0 | A box type that isn't `<Stateless/>` on a shared pooled slot. |
| `identity-conflict`, `bare-fire`, `unbound-read`, `wire-conflict` | error | 0 | Pooled box use the naming rule above can't account for. |
| `undefined-handler`, `undefined-box` | error | 0 | A wire or call to a function that doesn't exist, or a box that is never created. |
| `twin-disagrees` | error | 0 | A fire the debug twin traces differently although its code matches - a reconstruction defect. |
| `parse`, `round-trip` | error | 0 | A file that doesn't parse, or that JackAll's writer can't reproduce. |
| `non-blackbox-shape`, `unrepresented-statement`, `unrepresented-read` | warning | 0 | Code outside the shapes BlackBox writes, which the graph view can't show. |
| `stale-slot` | warning | 147 | A pooled box that leaves a declared data-in unset. |
| `undeclared-param` | warning | 38 | A parameter the box type doesn't declare. |
| `unused-slot` | warning | 2 | A MultipleAND slot nothing fires, so it can never complete. |
| `undeclared-out`, `literal-type` | warning | 0 | A wire on an undeclared control-out, or a literal of the wrong kind for its data-in. |
| `stale-twin`, `unparseable-twin` | warning | 0 | A debug twin whose code no longer matches its release file, or that doesn't parse. |
| `unfired-out-anchor` | info | 160 | A declared control-out the graph never fires. |

## Script state in the savegame

:::info[Verified via reverse engineering]
Traced in `FarCry2_server` (`CDominoService::RegisterProperties`, `GetLuaGlobals`, `SetLuaGlobals`,
`CSerializableScriptObject::SaveFromScript` and `LoadToScript`) and matched in retail `Dunia.dll`,
whose Domino service registers the same `LuaGlobals` property (`0x10596b80`). The table and its keys
are in a save the retail game wrote.
:::

Domino keeps two kinds of script state through a save and reload:

- **The `Globals` table.** The Domino service has a property, `LuaGlobals`, written only to
  savegames. Saving serializes the Lua global named `Globals`, and loading writes the saved values
  back into it. It is the one piece of script state that belongs to the playthrough rather than to an
  entity. The game keeps its story progress there: `Globals.MASTER_GameGlobals`, defined in
  `domino\user\master_gameglobals.globals.lua` and used 1,033 times across the shipped graphs, holds
  flags for both worlds, such as `CarverTapes_World1_Closed` and `AcceptedMissionID`.
- **Each Domino entity's own state.** `CDominoComponent` has a savegame-only property of the same
  type, `LuaState`, so a Domino host's script state is saved with its entity.

Both go through `CSerializableScriptObject`, which stores each value as text with a type tag:

| Lua value | Saved as |
|---|---|
| number | text, formatted `"%f"`: six decimal places |
| string | the string |
| table | a nested object, recursively |

Functions are not saved as values; callbacks, listeners and delays a script registered are kept in
lists of their own. The traced value branches cover only strings, numbers and tables, so booleans are
not among them, and the shipped globals use 0 and 1 instead.
