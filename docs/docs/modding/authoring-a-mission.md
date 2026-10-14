---
sidebar_position: 8.5
---

# Authoring a mission

How a Far Cry 2 mission is put together: who hands it out, how it shows on the map and in the
journal, how it decides success or failure, and which world entities it switches on and off. Then
one retail side mission is taken apart file by file, and a new one is built the same way.

No new mission has been loaded in a running game yet. Every step below is marked:

| Mark | Meaning |
|---|---|
| **(proven)** | seen working in a running game, by this project |
| **(RE-verified)** | read in the engine's code; see the linked engine page |
| **(seen in data)** | read in the retail files |
| **(inferred)** | follows from the above, not checked |
| **(untested)** | a step the recipe needs that nothing has exercised |

Background pages: [Domino scripts](../engine-internals/domino-scripts.md) for the graph format,
[the campaign](../engine-internals/campaign.md) for the mission manager's rules, and
[from placed data to a live object](../engine-internals/data-to-live-object.md) for how layers spawn
entities.

## What a mission is made of

A mission is not one file. It is a **Domino graph** (plain Lua) that drives five kinds of data
**(seen in data)**:

| Piece | Where it lives | What it does |
|---|---|---|
| Mission graph | `domino\user\<code>.<graph>.lua` | the whole logic: when it starts, what counts as done, what it pays |
| Mission layers | declared in `<world>.game.xml` under `MissionsDef`; filled in sector files and `mapsdata` | groups of world entities the graph switches on and off |
| Map markers | `Domino.Objectives.*` entities, mostly in `<world>.mapsdata.fcb` | the map icon and the journal line, chosen by a state id |
| Text | `languages\<lang>\oasisstrings.rml`, sections `Objectives` and `Subtitles` | journal lines, objective lines, subtitles |
| Speech | sound banks `soundbinary\<id>.spk`; bark banks for characters | phone calls, briefings, character dialogue |

Story, library and buddy missions also have a record in `engine\gamemodes\gamemodesconfig.xml`,
which the mission manager reads for rewards and reputation. Side missions do not: the phone
assassinations and the convoys have no record **(seen in data)**.

## Who hands a mission out

Every giver is a graph. The engine supplies the pieces each one is built from — the phone, pickups,
character interactions, the map markers — but no giver is engine code **(seen in data)**. What each
job says and pays, as the player meets it, is in [side missions](../lore/missions/side-missions.md).

| Giver | Graph | How the player gets the job |
|---|---|---|
| The phone: assassinations | `SideMissions/Assassination_Missions` | four **cell towers** per world are pickups; using one plays a phone call and starts a random contract. Worked through [below](#worked-example-a-phone-assassination). |
| The gun shop: convoys | `SideMissions/ConvoyMissions` | `SelectRandomMission` picks one of four convoys per world, then `Convoy_PawnBriefing` has the arms dealer brief it. Success runs `UnlockWeapons`; a failed convoy is offered again. |
| Faction HQs: library missions | `MASTER_World1.World1_HQdoorman`, `BASEBRIEF_LIB-MISSION_LOADING` boxes | the HQ doorman and a briefing graph; `LibraryMission_Accepted` starts the mission graph. See [which missions are offered](../engine-internals/campaign.md#which-missions-are-offered). |
| Buddies: side quests | `SideMissions/MikesPlace_BSQManager_W1/W2`, `BSQ_AssignMission` | between faction jobs, a buddy at Mike's in Leboa-Sako or the Marina in Bowa-Seko offers a `BSQ<nn>` job; accepting any other job cancels it. |
| Story | `MASTER_World1.World1`, `MASTER_World2.World2` | `StartScript` boxes in the master graph start each story and buddy mission graph. See [how a mission's graph starts](../engine-internals/campaign.md#how-a-missions-graph-starts). |

**One job at a time.** `Globals.MASTER_GameGlobals.AcceptedMissionID` holds the accepted mission's
id, or `"none"`. The `MissionAcceptedBroadcast` custom box sets it and broadcasts
`StoryMission_Accepted`, `LibraryMission_Accepted` or `SideMission_Accepted`.
`MissionCompleteBroadcast` calls the engine's `MissionCompleted` with it, broadcasts the matching
`…Completed` message and resets it to `"none"` **(seen in data)**. A cell tower used while another
job is accepted asks first, with `SQ_CANCEL`: "Are you sure you want to accept? This will abort
any current objective."

**Side jobs pause during main jobs.** The master graph pauses the assassination system when a story
or library mission is accepted, and resumes it when one completes (box 159 in `master_world1`; its
debug twin names the wiring) **(seen in data)**.

## The building blocks

### Objectives, the journal and the map marker

A marker is a `Domino.Objectives.*` entity with a `CMapElementComponent` **(seen in data)**:

| Field | Meaning |
|---|---|
| `StateId` | the journal line, a key in `oasisstrings`' `Objectives` section |
| `ObjectiveId` | the objective line under it, also an `Objectives` key |
| `entEntityToFollow` | an entity id; the marker sits on that entity |
| `selState`, `selType` | Disabled / Enabled / Available, and the icon kind |

The `ObjectiveState` node calls `SetObjectiveState(state, showPopup)`. That enables every marker
whose `StateId` equals the state and **disables every other marker that has one**, so one objective
shows at a time **(RE-verified;** [objectives](../engine-internals/campaign.md#objectives-and-map-markers)**)**.

The `GameElementObjective` node is the other switch. It sends a marker the entity event
`CMapElementComponent_Enable`, `_Disable` or `_Available`; that is how the cell-tower icons appear
**(seen in data)**.

Markers sit in `<world>.mapsdata.fcb`, which is world-wide and always loaded. A marker placed in a
sector file would exist only while that sector is streamed in, so it could not show on the map from
far away **(inferred)**.

### Triggers and waits

Domino nodes wait on engine events through `CScriptCallbackSystem:RegisterEventCallback(entity,
box, handler, eventName)` **(seen in data)**. The ones a simple mission uses:

| Node | Waits for | Engine event |
|---|---|---|
| `PickupListener` | a pickup being used | `DominoCallbackPickupPicked` |
| `HealthEvents` | a pawn hurt or killed | |
| `CheckPlayerInFOV` | the player inside a pawn's view cone, within a distance | |
| `CheckIsUnderAttack`, `CheckCombatMercsInRadius` | the pawn shot at, or alerted soldiers near it | |
| `ConvoyMission` | a convoy destroyed or escaped | `SuccessfulConvoy`, `FailedConvoy` |
| `PhoneCall` | the call picked up, finished, timed out or hung up | `call_picked`, `voice_finished`, `call_timeout`, `hangup_call` |
| `PawnInteraction` | the player pressing use on a pawn | |
| `ConsoleCommand` | a console command the graph registers | |

Area triggers are `CProximityTriggerComponent` entities, wired by
[event links](../engine-internals/entity-instancing.md#event-links) or watched by a graph
**(seen in data)**.

### Success and failure

There is no engine-side success test for most missions. **The graph decides**, by which of its
handlers runs, and reports it through `MissionCompleteBroadcast` (success) or by ending without it
**(seen in data)**.

Convoys are the exception: `CConvoyMission` (`FarCry2_server` `0x08613c90`) answers the
`StartConvoy` and `CancelConvoy` events, and its `CDispatcherConvoy` decides. Success is
`CheckCargoMissionSuccess` (the cargo unit reaches state 3) or `CheckAssassinationMissionSuccess`
(the target passenger's agent reports dead). `CoreRun` calls `MissionFailed` **(RE-verified on the
server)**.

### Rewards

`GiveMissionReward(id)` pays the record's diamonds. Assassinations pay through the ids `ASSW1`,
`ASSW2` and `ASSBUDDY`, which are `gamemodesconfig.xml`'s `AssassinationRewardWorld1` (10),
`World2` (15) and `Buddy` (20) **(RE-verified;** [rewards](../engine-internals/campaign.md#rewards)**)**.
`MissionCompleted` counts an id with `AS` at positions 2–3 as an assassination and `CV` as a convoy.

### Mission layers

A layer is enabled with the `SetMissionState` node, which calls
`CGameMissionMgr_GetInstance():GetMission(path):Enable()`. The path is the layer's name as
`MissionsDef` spells it, `Missions/AssassinationMissions/Target_01` **(seen in data)**. Enabling
spawns the layer's entities in every loaded sector and in `mapsdata`; `Disable` and `Off` remove
them at world scope **(RE-verified;** [layer states](../engine-internals/data-to-live-object.md#stage-3-layer-states-switch-spawning)**)**.

### Dialogue and subtitles

Two separate systems carry speech **(seen in data)**:

- **Sound ids.** `PhoneCall(soundId)` and the `PlaySound` node play `soundbinary\<id:08x>.spk`. The
  subtitle is the `Subtitles` entry keyed by the **decimal** id. The contract call `0x004ef967` is
  subtitle `5175655`: "Listen, very carefully. There is an available target of opportunity…".
- **Bark banks** carry character dialogue: greetings, briefings, mission lines. A graph loads a
  mission's banks with `SetMissionBarkBankState`, which takes a mission *tag*, and plays lines with
  `PlayBark`. The tag becomes a number from a **fixed engine table of 165 missions**, and the bank
  file number is built from it ([bark banks](../file-formats/bark-banks.md#the-bank-number))
  **(RE-verified)**. See [blockers](#blockers).

### Buddies

Buddies hook into story and library missions, not into phone or convoy jobs. The engine picks the
primary and rescue buddy before a story mission
([buddies](../engine-internals/campaign.md#buddies)). A graph brings one into play with the
`SpawnBuddy`, `SpawnPrimaryBuddy` and `AssignBuddy` nodes, and the side quest a buddy offers is its
own `BSQ<nn>` graph and record **(seen in data)**. The phone-assassination graph uses none of these.

## Worked example: a phone assassination

The world-1 contract `A1AS01`, slot 1 of six.

### The files it touches

| File | What it holds for `A1AS01` |
|---|---|
| `domino\user\master_world1.world1.lua` | creates the assassination system (box 159) and starts, pauses and resumes it |
| `domino\user\sidemissions\assassination_missions.assassination_missions.lua` | the contract loop: pick, offer, run, pay |
| `…assassination_missions.setassassinationvariables.lua` | the six contracts' data per world |
| `…assassination_missions.assassinationcelltowerinteracted.lua` | the four cell towers and the phone call |
| `…assassination_missions.disablecelltowers.lua` | hides the tower icons while a contract runs |
| `…assassination_missions.assassination_console.lua` | test commands `A1AS01`–`A1AS06`, `A2AS07`–`A2AS12` |
| `domino\user\common_customboxes.pawn_combat.lua` | the target's behaviour and the kill detection |
| `domino\user\common_customboxes.missionacceptedbroadcast.lua`, `…missioncompletebroadcast.lua` | `AcceptedMissionID` bookkeeping |
| `worlds\world1\generated\world1.game.xml` | declares the layers `Missions/AssassinationMissions/Target_01`–`06` and `Envelope_01`–`06`, all off by default |
| `levels\w1_c_3\generated\worldsectors\worldsector3160.data.fcb` | the target, under layer `target_01` |
| `worlds\world1\generated\world1.mapsdata.fcb` | the marker, under layer `target_01`; the four cell-tower icons, under `main` |
| `levels\w1_b_4\…\worldsector4772.data.fcb` and three others | the four cell-tower pickups |
| `worlds\world1\generated\entitylibrary.fcb` | the archetypes `enemy_archetypes.Missions.Assassination_Target`, `Domino.Objectives.MissionObjective`, `Domino.Objectives.CellTower` |
| `languages\english\oasisstrings.rml` | `Objectives/A1AS01_01`, `A1AS01_01_SAT_01`, `SQ_CANCEL`; `Subtitles/5175655` |
| `soundbinary\004ef967.spk` (in `worlds.dat`) | the contract phone call |
| `engine\gamemodes\gamemodesconfig.xml` | `AssassinationRewardWorld1="10"`, `MaxAssassinationMissions="12"` |

All of it is **(seen in data)**.

### The data

**The contract** is a block of `SetAssassinationVariables`. `In_1`, for world 1, sets:

| Variable | Value |
|---|---|
| `TargetLayer` | `Missions/AssassinationMissions/Target_01` |
| `Target` | entity `"2054317409333947953"` |
| `TargetMarker` | entity `"2054317444568198717"` |
| `MissionID` | `A1AS01` |
| `ObjectiveState` | `A1AS01_01` |
| `FOVDistance` | 1 |

Entities are named by their `disEntityId` **as a decimal string**; a Lua number cannot hold a 64-bit
id. The block also writes `Missions/AssassinationMissions/Envelope_01` and then overwrites it with
the target layer, so the `Envelope_0n` layers are never switched by this graph.

**The target**, in `worldsector3160` under `missions\assassinationmissions\target_01`, at (2606.9,
2513.9, 18.0):

- archetype `enemy_archetypes.Missions.Assassination_Target`: a `CPawnEntity` with the
  special-character brain `::SpecialCharacter/BrainSpecialCharacter` in `specialcharacter.ai.rml`,
  inventory pack `warlord`, and `CPersistComponent` level 2, so the save keeps him
- on the instance: a `CGraphicKitComponent` outfit, the model
  `graphics\actors\assassination_target\assassinationtgt.xbg` with its part list, and a
  `CMissionComponent` naming the same layer
- the layer's `BindingHierarchy` node makes him the root of one bound child, at bone `R Thigh`; the
  child, id `2058714331347944380`, sits in `main`
- a dozen `STP_archetypes.*` smart-terrain points within 12 m (`Rest.Piss`, `Duty.WriteOnNotepad`,
  `Rest.CellPhone`), all in `main`

**The marker**, in `mapsdata` under the same layer: `Objective_Assassination_1`, archetype
`Domino.Objectives.MissionObjective`, with `StateId = A1AS01_01`, `ObjectiveId = A1AS01_01_SAT_01`
and `entEntityToFollow` = the target's id.

**The cell towers**: four `Domino.Objectives.CellTower` entities in `mapsdata` under `main`
(`Objectives.CellTower_0` at 3359, 3838 is one), each with a matching pickup in a sector file.

### The run, step by step

1. **Start.** Once an act-1 milestone passes, the master graph calls `StartWorld1` on box 159. It
   sets `WorldNumber = 1` and registers the test commands. Which milestone was not traced; the
   chain runs through `Once_Only_244`, fed by story `SetMissionState` boxes.
2. **Pick.** `Switch` box 36 counts contracts done, and `Random` box 40 picks one of six slots,
   never the same twice. On the sixth, `LastMission_` is set.
3. **Load the contract.** `SetAssassinationVariables.In_<n>` fills the variables above.
4. **Offer.** `AssassinationCellTowerInteracted` makes the four tower icons Available, makes the
   tower pickups usable and listens to them.
5. **Use a tower.** With `AcceptedMissionID == "none"`, it plays `PhoneCall(0x004ef967)`. Otherwise it
   shows the `SQ_CANCEL` question first. The graph continues on the box's `Out`, right after the
   call starts; it does not wait for the call to be picked up.
6. **Accept.** `DisableCellTowers` hides the tower icons. `MissionAcceptedBroadcast` sets
   `AcceptedMissionID = A1AS01` and broadcasts `SideMission_Accepted`.
7. **Arm the world.** `SetMissionState.Enable(Target_01)` spawns the target and the marker.
   `ObjectiveState.Set("A1AS01_01")` shows the marker and the journal line, with its popup.
8. **Run the target.** The target layer is not `Target_03`, so `Pawn_Combat.StartSTP` runs. That
   sends the target a `UseSTP` command, and he goes about a smart-terrain routine. It also watches
   three things: the player inside his view cone within `FOVDistance`, him being attacked, and
   alerted soldiers within 30 m. Any of the three switches him to `DoSpecialCharacterCombat`. The
   health monitor fires `Killed`. `Target_03`'s target rides a vehicle, and that slot uses the
   `ConvoyMission` node instead.
9. **Count.** `Achievement_AssassinationsDone` goes up by one. Below 12 it sends achievement data
   `assassination`, at 12 `assassination_all`, then prints the debug text "You killed the target.
   You are one cold-blooded mofo."
10. **Pay.** `GiveMissionReward("ASSW1")`: 10 diamonds in world 1.
11. **Close.** `MissionCompleteBroadcast.SideMission_Complete` calls `MissionCompleted("A1AS01")`,
    broadcasts `SideMissionCompleted` and resets `AcceptedMissionID`. Then
    `SetMissionState.Disable(Target_01)` removes the layer.
12. **Next.** It loops to step 2 unless that was the sixth contract. A convoy-type failure prints
    "You blew it. The target has escaped." and closes the job without paying.

All of this is read from the graphs **(seen in data)**. `TextToScreen` lines are debug prints; that
the retail game shows them is **(inferred)**, since `DrawTextToScreen` has not been checked on PC.

## Recipe: a new assassination contract

Two routes. The first changes no graph logic and should be built first, because it proves the data
half on its own.

### Route A: move an existing contract

Retarget slot 1 to a new person in a new place. Only data changes:

| # | File | Change | Tool | Status |
|---|---|---|---|---|
| 1 | the sector file at the new spot | add the target under layer `missions\assassinationmissions\target_01`: a copy of `Target_01` with a new `disEntityId`, a new position and its own outfit | JackAll Map tab; the layer move is written to the container's `_layout.xml` | placing **(untested)**; layer move built, in-game check outstanding |
| 2 | `worldsector3160.data.fcb` | remove the old target, `2054317409333947953` | `_layout.xml` delete | **(untested)** |
| 3 | `world1.mapsdata.fcb` | point `Objective_Assassination_1`'s `entEntityToFollow` at the new id, and move its `hidPos` | JackAll fragment, one object | **(untested)** |
| 4 | `assassination_missions.setassassinationvariables.lua` | in `f_30_Output_0`, replace `"2054317409333947953"` with the new id | plain text in a layer; `jackall-cli mod lint` checks it | the loader takes edited source **(RE-verified)**; never seen in game |
| 5 | `oasisstrings` `Objectives` | optional: new wording for `A1AS01_01` / `A1AS01_01_SAT_01` | `oasisstrings.fragment.xml` | string edits **(proven** for weapon names**)** |

Put the new target **next to existing smart-terrain points**. `UseSTP` only asks the pawn to use
one, and the retail target stands among a dozen **(inferred)**. Keep him on navmesh: world 1 ships
one everywhere the player walks.

Step 4 is needed only because the graph names the target by id. Reusing the old id at the new
place makes step 4 unnecessary, but two entities must not share an id within a class
([spawning](../engine-internals/data-to-live-object.md#stage-4-one-entity-spawns)).

### Route B: add a seventh contract

Everything from route A, under a new layer and new ids, plus:

| # | File | Change | Tool | Status |
|---|---|---|---|---|
| 1 | `world1.game.xml` | a `Mission` named `Missions/AssassinationMissions/Target_07` with one `Layer`, `PathId` `missions\assassinationmissions\target_07`, default off | JackAll `game.xml` fragment, one mission | built; that `GetMission` finds a layer declared this way is **(untested)** |
| 2 | the sector file | the target, moved into the new layer | JackAll Map tab, `_layout.xml` | new layers supported by JackAll; **(untested)** in game |
| 3 | `world1.mapsdata.fcb` | a new `Domino.Objectives.MissionObjective` in the new layer: `StateId A1AS07_01`, `ObjectiveId A1AS07_01_SAT_01`, following the target | JackAll fragment | **(untested)** |
| 4 | `oasisstrings` | `Objectives/A1AS07_01`, `Objectives/A1AS07_01_SAT_01` in every language the mod supports | `oasisstrings.fragment.xml` | new keys **(untested)**; edits proven |
| 5 | `…setassassinationvariables.lua` | an `In_7` block, copying `In_1`'s chain: layer, target, marker, `A1AS07`, `A1AS07_01`, FOV | text | **(untested)** |
| 6 | `…assassination_missions.lua` | `Random` box 40 and `Switch` box 36 to 7 outputs (`_DynamicAnchors`), a seventh handler for each that calls `In_7`, and `LastMission_` moved to the seventh `Switch` output | text | **(untested)** |
| 7 | `…assassination_console.lua` | optional: a `Launch_7` and an `A1AS07` command | text | **(untested)** |
| 8 | the `.debug.lua` twins of the three graphs | keep them matching, or delete the twin from the layer | text | `mod lint` reports `stale-twin` otherwise |

`MissionCompleted("A1AS07")` counts it as an assassination because of the `AS` **(RE-verified)**.
The achievement test compares against 12, so with 13 contracts the "all" achievement fires one
contract early **(inferred)**. `MaxAssassinationMissions="12"` is read by the mission manager;
whether it caps anything beyond statistics is unknown.

**A save made before the mod** already holds the graph's state: which slots `Random` has used and
how far `Switch` has counted live in the master graph's `LuaState`
([script state](../engine-internals/domino-scripts.md#script-state-in-the-savegame)). How a saved
`Random` table loads into a graph that now has 7 outputs is untested. Test on a new game first.

### Route C: a contract outside the shipped graph

A standalone graph needs something to run it. Retail starts its DLC weapon graphs from
`DominoEntity_9`–`_12`, placed in sector files under `main` with `hidStartOnLoad`
([campaign](../engine-internals/campaign.md#how-a-missions-graph-starts)) **(seen in data)**. A mod
can place the same kind of entity, with `fileBoxPath` naming its own graph. That entity respawns
every time its sector streams in, so the graph has to guard against starting twice, for example
with a flag in `Globals` **(inferred)**. This route avoids editing the shipped graphs, at the cost
of rebuilding the offer, accept and pay steps.

## Blockers

None of these stops a contract like route A or B. They stop richer missions.

| Blocker | What it stops | What would clear it | Estimate |
|---|---|---|---|
| **No graph editor.** The `.domino.xml` sources are lost and JackAll's Domino view is read-only. | authoring large graphs comfortably | write Lua in BlackBox's shape by hand, with `jackall-cli domino check` and `mod lint` as the safety net; or finish the editing half of JackAll's Domino view | none for small graphs; weeks for an editor |
| **Bark-bank missions are a fixed list.** `GetMissionFromTag` maps a tag to one of 165 numbers, and bank files are numbered from it. | voiced character dialogue under a new mission tag | reuse an existing tag's number; or an FCSE hook on `GetMissionFromTag` mapping new tags to new numbers, plus new `.bank`/`.pfe`/`.spk` files and `SPBarkData.banklist` entries | 2–4 days: the hook, then one bank as proof |
| **New speech by sound id is untested.** | a new phone call or narration | one experiment: a new `.spk` under a new id, played with `PhoneCall`, plus a `Subtitles` entry with the decimal id. New banks named after new ids already load for weapon sounds ([editing sound banks](./editing-sound-banks.md#how-the-game-finds-a-bank)) | one launch |
| **`UseSTP`'s choice of point is untraced.** | a target that routines somewhere with no smart-terrain points | trace `UseSTP` in the brain; or place the target beside existing points | half a day of RE |
| **The marker cannot be set from Lua.** `SpawnEntityFromArchetype` can spawn a target, but no binding sets a marker's `StateId` or `entEntityToFollow`. | fully runtime-spawned contracts | place markers as data; or an FCSE binding that writes component fields | a day for the binding |

## Open questions

- Which act-1 milestone starts the assassination system (`Once_Only_244`'s inputs, step 1).
- Whether `CGameMissionMgr::GetMission` needs the layer in `MissionsDef`, or creates it on demand.
- What `PhoneCall` shows besides the audio: the HUD phone, and whether the subtitle appears.
- What the target's bound child at `R Thigh` (`2058714331347944380`) is.
- Whether the `Envelope_0n` layers, declared and filled but never switched by this graph, are cut
  content or used elsewhere.
- How a saved `Random`/`Switch` state loads into a graph whose output count changed.
- The exact console spelling of graph-registered commands: probably `domino_<name>`, see below.

## Cheapest next experiments

Each is one launch of the GOG install (`"C:\Games\Far Cry 2\bin\FCSE.exe"`), continuing a world-1 save
past the start of the assassination contracts, with the console on `~`. DevTools' **Developer
console** option should be on, in case the graph commands are gated. Do not save afterwards.

1. **Run a retail contract from the console.** No files. Type `domino_A1AS01`, then
   `domino_CellTowerInteraction`. If neither is found, run `console_dump_elements` and look up the
   spelling in `Documents\My Games\Far Cry 2\ConsoleElementsDump.txt`. Expect the journal popup
   "Received a cellular message to terminate a target.", a marker in Pala's `w1_c_3` area, and the
   target there. This proves the console path the later tests use and the whole accept chain,
   without touching a tower.
2. **Switch the target layer by hand.** No files.
   `#CGameMissionMgr_GetInstance():GetMission("Missions/AssassinationMissions/Target_01"):Enable()`
   then `#SetObjectiveState("A1AS01_01", 1)`. Expect the target and marker without the contract
   running. That separates "the data spawns" from "the graph runs", which route A depends on.
3. **Route A, moved target.** One layer with the steps in route A. Then experiment 1's commands.
   Expect the marker on the new spot and the target there.
4. **A new phone call.** One layer: any retail voice bank copied to a new id, a `Subtitles` entry
   for the new decimal id, then `#PhoneCall(0x<newid>)`. Expect the phone to ring, and the line with
   its subtitle. This clears the third blocker.
