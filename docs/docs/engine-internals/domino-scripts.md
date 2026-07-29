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

## What's not here yet

This is a filename-level survey only — actual script *content* (real node wiring, real parameter
values, real variable names) hasn't been extracted. That would mean unpacking this prototype's
`common.dat`/`.fat` pair, which is entirely feasible with the already-fully-documented `.fat`/`.dat`
container format (see [archives](../file-formats/archives-fat-dat.md)) and existing tooling
(`tools/JackAll`) — just not attempted this pass. Doing so would turn this from "here's what mission
graphs exist" into "here's what a real mission graph actually looks like," which would be the natural
next step for anyone wanting to author new Domino content rather than just understand the existing
system.
