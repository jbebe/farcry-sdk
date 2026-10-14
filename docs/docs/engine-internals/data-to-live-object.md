---
sidebar_position: 16
---

# From Placed Data to a Live Object

How something written into the world data becomes a running object with components, AI, scripts
and event links: what loads, in what order, what each stage reads, and where a mod can add,
remove or override something. NPCs, missions, interactive objects and new worlds all run through
this one path.

Most stages already have a page of their own, linked where they come up. This page puts them in
order and fills in the joins between them that had not been traced.

## How claims are marked

Every claim carries one of these:

| Mark | Meaning |
|---|---|
| **(RE-verified, server)** | read in `FarCry2_server`'s code via GhidraMCP; addresses are the server's |
| **(RE-verified, GOG)** | read in the GOG 1.03 `Dunia.dll` |
| **(seen in data)** | measured in the retail files |
| **(measured in game)** | observed in a running game |
| **(inferred)** | follows from the above but was not read or observed directly |

The new traces on this page were made on the server only: the Ghidra session had no `Dunia.dll`
open. The pages linked for the earlier stages say which of their claims were matched in retail.

## The path in one picture

```
FCSE / menu / save
  └─ CXGame::LoadWorld                      world-scope setup, once per world
       ├─ <world>.game.xml, depload, archetypes, movies, Lua
       ├─ managers / omnis / mapsdata  →  CGlobalSpawn per container
       ├─ GlobalSpawn(main)             →  world-scope entities of layer main
       ├─ MissionsDef                   →  mission manager, layer states
       └─ AI engine LevelLoaded + Start
streaming, every frame
  └─ sector data arrives  →  one layer resource per MissionLayer node
       └─ layer on  →  SBindingHierarchyEntry::Spawn
            └─ CEntitySystem::SpawnEntityFromNode
                 ├─ archetype merge (tplCreatureType)
                 ├─ SpawnGenericEntity: class factory, id, spawn listeners
                 └─ CNomadObject::Load: properties, Components → factory → Init
       └─ CWorld::ToFinalize  →  budgeted finalize queue
            └─ CEntity::FinalizeLoad
                 ├─ every component's FinalizeLoad, then every component's Finalize
                 │    └─ AI: agent → brain workspace → registered with the AI engine
                 └─ finalize listeners → CGhostManager: save record, restore from save
```

## Stage 1: the world loads

`CXGame::LoadWorld(name, …, flags)` (`0x08888f90`) runs the world-scope steps in this order
**(RE-verified, server)**:

| # | Step | Reads | Gated by flag |
|---|---|---|---|
| 1 | Parse the world descriptor | `<world>.game.xml` | |
| 2 | `LoadDepLoad` | the [dependency manifest](../file-formats/depload.md) | |
| 3 | `CWorld::Create` | the descriptor's sector grid | |
| 4 | Sound occlusion and ambiance setup for the sector grid | | |
| 5 | Particle libraries | | `0x10` |
| 6 | `LoadArchetypes` | the entity libraries; see [which libraries load](./entity-instancing.md#which-libraries-load-and-in-what-order) | `0x08` |
| 7 | Movie data, then `CMovieSystem::PlaySequences` | `moviedata.xml`, per world and per map | `0x04` |
| 8 | `CXSystemBase::LoadLuaScripts` for the world | | `0x02` |
| 9 | `CWorld::AddGlobalSpawn` | `<world>.managers.fcb` | `0x02` |
| 10 | `CWorld::AddGlobalSpawn` | `<world>.omnis.fcb` | `0x02` |
| 11 | `CWorld::AddGlobalSpawn`, once per map descriptor | `<world>.mapsdata.fcb` | `0x02` |
| 12 | `CWorld::GlobalSpawn(main layer)` | | `0x02` |
| 13 | `CGameMissionMgr::FromXML` | the descriptor's `MissionsDef` | |
| 14 | `CAIEngine::LevelLoaded`, then `CAIEngine::Start` | | `0x20` |

Which flags the single-player load passes was not read. That the campaign loads archetypes, scripts
and AI shows that at least `0x08`, `0x02` and `0x20` are set **(inferred)**.

Three things follow:

- **Archetypes are in place before anything spawns.** Step 6 precedes every spawn, so an archetype
  edit applies to everything placed in the world, world-scope and sector alike **(RE-verified,
  server)**.
- **World-scope `main` exists before the mission manager does.** Step 12 spawns every `main`-layer
  entity of the managers, omnis and mapsdata containers. Only at step 13 does the mission manager
  read its layer list **(RE-verified, server)**. A non-`main` world-scope layer spawns later, when
  its state becomes "on" and `CWorld::OnLayerStateChanged` calls `GlobalSpawn` for it (see stage
  3) **(RE-verified, server)**. That `FromXML` switching default-on layers is what fires that call
  is **(inferred)**.
- **No sector entity spawns here.** `LoadWorld` opens no sector file **(RE-verified, server)**.
  Placed sector content arrives through streaming, in stage 2. The load operation's streaming loop
  ([loading a save](./loading-a-save.md#what-each-step-waits-on), **RE-verified, GOG**) is where that
  first happens **(inferred)**.

The descriptor, the file set a world is made of and how travel calls this function are on
[entering a world](./world-loading.md).

## Stage 2: a sector's data arrives

Sectors are streamed inside a square ring around the player, ±4 sectors for entities
([ring sizes](./world-loading.md#how-far-around-the-player-each-kind-loads)). When a sector's data
resource arrives, `CSectorSpawnCategory::OnReceivedSectorData` (`0x095c5330`) does the following
**(RE-verified, server)**:

1. **One layer resource per `MissionLayer` node.** It walks the container's `MissionLayer`
   children, reads each one's `PathId` and calls `SetLayerResource(path, node)`.
2. **`main` always exists.** If the data had no `main` node it creates an empty one, then switches
   `main` on.
3. **Layers the save holds entities for.** `AddPersistenceLayers` (`0x095c5230`) asks the
   persistence database (`CPersistenceDB::GetLayerToSpawn`) which layers have saved entities in
   this sector. Each one missing from the data gets an empty layer resource. So an entity the save
   holds still has a layer to come back through in a sector whose data never named that layer
   **(inferred from the code above)**.
4. **Every other layer asks its current state.** For each non-`main` resource it calls
   `CWorld::GetLayerState`, and on a non-zero answer `OnLayerStateChanged(layer, state)`.

Which entities a layer owns is decided by nesting in the container, not by the entity's
`CMissionComponent`; see [mission layers](./entity-instancing.md#mission-layers-decide-when-a-placed-entity-exists).

## Stage 3: layer states switch spawning

`CWorld::GetLayerState` (`0x095d1150`) answers **1** when the layer is enabled, else **2** when it
is disabled, else **0**. The same three values are the save's
[`MissionManager` states](../file-formats/savegame.md#campaign-state-outside-the-entities)
**(RE-verified, server)**.

Each value does something different at the two scopes **(RE-verified, server)**:

| State | Sector scope (`CSectorSpawnCategory::OnLayerStateChanged`, `0x095c4fc0`) | World scope (`CWorld::OnLayerStateChanged`, `0x095d1ff0`) |
|---|---|---|
| 1, enabled | the layer resource is added; `CLayerResource::OnLayerStateChange` releases its `SpawnBlockerId` dependency, which lets the layer's entities spawn **(inferred)** | sectors notified, then `GlobalSpawn(layer)` |
| 2, disabled | the layer resource stays added; under two conditions not identified, `OnLayerStateChange` starts physics on it | sectors notified, `GlobalUnspawn(layer)`, `CPersistenceDB::DeactivateLayer` |
| 0, off | `OnLayerStateChange` marks every entity of the layer for removal, then the resource is removed | sectors notified, physics stop flushed, `GlobalUnspawn(layer)`, `DeactivateLayer` |

What a *disabled* layer's sector entities do is not settled. The world-scope column removes
them. The sector column keeps the resource, and the code does not say whether the entities stay,
freeze or go. [Experiment 1](#1-switch-a-layer-from-the-console) answers it.

Scripts switch states through `CBaseMission` (`Enable`, `Disable`, `Off`), reached by
`CGameMissionMgr_GetInstance():GetMission(path)`. The shipped `SetMissionState` node does exactly
that **(seen in data)**; see [mission layers](./campaign.md#mission-layers).

## Stage 4: one entity spawns

Spawning is spread across frames. `SBindingHierarchyEntry::Spawn` (`0x09676ab0`) spawns one
entry, then its children, attaching each child to it with `CEntity::Bind`. It stops part-way
through a list when `CBudgetTimer` says the frame's budget is spent, unless called in forced mode
**(RE-verified, server)**. It spawns an entry only if the entry's layer is enabled or the call is
forced. A layer id of -1 counts as always on (`SBindingHierarchyEntry::IsLayerEnabled`,
`0x096740a0`).

`CEntitySystem::SpawnEntityFromNode` (`0x09453010`) has four callers: `CGlobalSpawn::Spawn` for
world scope, `SBindingHierarchyEntry::Spawn`, `RespawnEntities`, and the editor's
`CreateFromArchetype` **(RE-verified, server)**. So sector entities reach it through the binding
hierarchy **(inferred)**. The data does not account for that alone: a `MissionLayer` node can carry a
`BindingHierarchy` child of `Root` entries, each an entity with bound `Child` entries (bone name,
layer). But only bound groups have one. In `worldsector3160` the 809 `main` entities have no root, and
the assassination target's layer has one root with one child bound at `R Thigh` **(seen in data)**.
Whether the loader builds a root at runtime for every entity without one is not read.

`SpawnEntityFromNode` does three things **(RE-verified, server)**:

1. **Merge.** With `tplCreatureType`, the node becomes a merge of archetype and instance; a name
   that resolves to nothing drops the entity. See
   [instancing merges](./entity-instancing.md#instancing-merges-it-does-not-replace).
2. **Create.** It reads `hidEntityClass` and `disEntityId` through the merged node and calls
   `CEntitySystem::SpawnGenericEntity(class, id)` (`0x094528f0`). That function:
   - takes a fresh id when the node's id is all ones
   - creates the object from the class factory at `+0x84`, keyed by the class id; an unknown class
     returns the invalid proxy, and nothing spawns
   - sets the id, and files the entity in a per-class `EntityId` map; an id already in that class's
     map is overwritten by the new entity
   - calls every entity-system listener's spawn slot (`+8`) with the new entity
3. **Load.** `CNomadObject::Load(entity, node)` reads every registered property, including the
   `Components` container, then the entity's own vtable slot `+0x14` runs with the node. The
   container creates each component through the component factory and runs its `Init`; an unknown
   component class is skipped. See
   [how an entity gets its components](./entity-component-schema.md#how-an-entity-gets-its-components).

### Prefabs

A placed `CPrefabEntity` lists its members by id, and the members are ordinary sibling entities
under the same layer **(seen in data)**; see
[prefab instances](./entity-instancing.md#prefab-instances-are-placed-entities). Nothing traced here
treats them specially. They spawn like any other entity, so a prefab is a grouping for the editor
**(inferred)**.

## Stage 5: the entity is finalized

A loaded entity is not live until it is finalized, and finalizing is a separate, budgeted queue
**(RE-verified, server)**:

- **`CWorld::ToFinalize`** (`0x095db920`) enqueues the entity with a priority. The priority starts
  from the entity's sector distance to a reference sector (`CWorld+0x210`), measured as the larger
  of the X and Y differences. Beyond one sector it adds 5 for the `landmarknear` category, 10 for
  the default category in the default layer, and 15 for the default category in any other layer.
  That the reference sector is the player's is **(inferred)**.
- **`CWorld::ProcessToFinalize`** (`0x095e0e20`) re-sorts the queue every 20 calls once it holds
  more than 16 entries, and finalizes entries until the frame budget is spent. Called with `true`,
  it ignores the budget.
- **`CEntity::FinalizeLoad`** (`0x0943b3c0`) skips an entity already marked deleted, marks the
  entity finalized and invalidates its update-task cache. It then calls **every component's
  `FinalizeLoad`, and after that every component's `Finalize`**. Those are server slots `+0x20` and
  `+0x24`, retail slots 7 and 8 in [the component vtable](./fcse-entity-data-abi.md#what-the-other-slots-are).
  Last, it calls every entity-system listener's finalize slot (`+0x18`).

So no component can see a sibling's `Finalize` from its own `FinalizeLoad`, and every component
has run `FinalizeLoad` before any runs `Finalize` **(RE-verified, server)**.

### The save comes in at the end

One finalize listener is `CGhostManager`. Its `OnFinalize` calls `CPersistenceDB::AddRecord`, then
`CPersistenceDB::RestoreEntity`, which overlays whatever the save holds for that id onto the
freshly spawned entity **(RE-verified, server)**; see
[the per-property overlay](../file-formats/savegame.md#mod-compatibility-a-per-property-overlay-not-a-full-freeze).
The entity's data is therefore always loaded first, and the save is applied last. A component the
save names but the data lacks is recreated through the factory
([restore](./entity-component-schema.md#adding-a-class-and-what-a-restore-does)).

The listener list at `CEntitySystem+0xc` has the same shape as `CMobileEventManager`'s
(`RegisterListener`, `0x09c3fb60`, also a list at `+0xc`). That the entity system is a
`CMobileEventManager`, and its listeners `IMobileEventListener`s, is **(inferred)**. How a listener
is added to the entity system was not traced.

## Stage 6: NPCs get an agent and a brain

`CAIComponent` registers two members: `Type`, a `CStringID`, and `AIObject`, a `CAIObject*`
loaded from data **(RE-verified, server; registry dump)**. The AI object's class comes from the
data, so a soldier, an animal and a smart-terrain point differ by what the archetype puts there
**(inferred)**.

The agent's own finalize chain runs inside the entity's finalize **(RE-verified, server)**:

1. **The class's own step.** `CAnimalAgent::Finalize` (`0x0855a0f0`), for example, enables its
   sensory system and steering. Three classes call the next step; one is the animal agent.
2. **`CGameAgent::Finalize`** (`0x0856e820`) caches the entity's `CGameObject` component and names
   the agent `<entity name>_<entity id>`.
3. **`CAgent::Finalize`** (`0x099cb670`) resolves the brain. If `aiwsBrainWorkspace` is set, it
   fetches the `CAIWorkspaceResource`, looks up the task named by `Brain` and calls
   `CAgent::SetBrain`, then finishes: personality, then registration with the AI engine
   (`CAIEngine+0xc`, slot `+0xc4`).

**A brain that does not resolve leaves the agent unregistered.** If the workspace has no task
repository, or holds no task with the `Brain` name, `CAgent::Finalize` returns before the
personality and before the AI-engine registration **(RE-verified, server)**. The visible result,
presumably an NPC that spawns and never acts, is **(inferred)**. An agent with no workspace at all
(`aiwsBrainWorkspace` of -1) skips the brain and still registers.

`SetBrain` (`0x099cb210`) accepts only a task that is a `CBrain`. Anything else is released and the
agent keeps no brain **(RE-verified, server)**.

The archetype fields, the workspace format and what brains do are on [`.ai.rml`](../file-formats/ai-rml.md)
and [AI](./ai.md). An NPC in a world without a navmesh crashes the game about 30 s in
([world loading](./world-loading.md#what-is-not-established)).

## Stage 7: scripts and event links

**Master graphs come from the omnis container.** `<world>.omnis.fcb` places `COmniEntity` Domino
hosts, which spawn at stage 1 step 12 because they sit in `main` **(seen in data; RE-verified,
server for the step)**. `CDominoComponent` registers `fileBoxPath` and `hidStartOnLoad`, so a host
names its graph file and whether it starts when loaded **(RE-verified, server; registry dump)**. A
running graph starts further graphs with `CDominoManager:SpawnDominoEntity`, using paths of the form
`Domino/User/<file>.lua` **(seen in data)**. See
[how a mission's graph starts](./campaign.md#how-a-missions-graph-starts) and
[how the engine loads a graph](./domino-scripts.md#how-the-engine-loads-a-graph).

**A graph can place entities itself.** `SpawnEntityFromArchetype(name, x, y, z, ax, ay, az)` spawns
an archetype by its fully qualified name into the default layer
([Lua API](./lua-api-surface.md)) **(RE-verified, server)**.

**Event links hold an id, not a pointer.** `CEventComponent::AddLink` (`0x094a0030`) stores the
input event name, the target's `EntityId` and the `CEntityEvent` to send **(RE-verified, server)**.
The target is looked up when the link fires, so it may spawn later than the source or live in
another sector **(inferred)**. The record layout is in [event links](./entity-instancing.md#event-links).

## Where a mod can step in

Ratings: **1** is a data edit with existing tools, **5** is new engine code. *Status* says whether
the point has been exercised in a running game.

| Stage | Injection point | What it can do | Tool | Rating | Status |
|---|---|---|---|---|---|
| 1 | Entity library archetype | change what every placed copy is | data layer, JackAll fragment | 1 | **measured in game** (magazine canary) |
| 1 | `<world>.game.xml` `MissionsDef` | add a mission layer, change a layer's default state | data layer; JackAll `rml decode`/`encode` | 2 | not tried |
| 1 | `managers`, `omnis`, `mapsdata` | add or remove world-scope entities: regions, spawn points, Domino hosts | data layer; whole-file override in JackAll | 3 | not tried |
| 2 | Sector container: add, remove or move an entity | anything placed | JackAll Map tab | 2 | not seen in game |
| 2 | Sector container: which layer an entity is under | make an entity mission-gated, or always present | JackAll `_layout.xml` | 2 | built; in-game check outstanding |
| 3 | Layer state at runtime | switch whole groups of entities on and off | Domino graph (data layer), console Lua | 1 | the shipped game does it; never driven by a mod |
| 4 | Instance overrides on an archetype | per-instance values, extra components | JackAll Map tab | 2 | not seen in game |
| 4 | Runtime spawn from script | place archetypes from a graph | Domino graph, console Lua | 1 | not seen in game |
| 4 | A new component class | new per-entity behaviour or data | FCSE plugin, registered in the component factory | 4 | **measured in game** (`CFCSEDataComponent`) |
| 4 | Detour `SpawnEntityFromNode` | rewrite, swap or drop any entity as it spawns, for every source | FCSE plugin | 4 | not tried |
| 4–5 | An entity-system listener | see every entity at spawn and at finalize; attach components, tag, log | FCSE plugin | 3 once the registration call is found | not tried |
| 5 | Save records | delete a record so the entity respawns from current data | JackAll `sav` tooling | 3 | not tried |
| 6 | Archetype `Brain` / `aiwsBrainWorkspace`; the workspace itself | give an NPC type other behaviour | data layer, JackAll AI tab | 2 | not seen in game |
| 7 | A new or edited Domino graph | missions, triggers, layer switches, spawns | data layer (plain Lua in `patch.dat`) | 2 | loader RE-verified on GOG; an edited graph not seen in game |
| 7 | Event links | wire entity to entity without a graph | JackAll Map tab | 2 | not seen in game |
| all | A whole new world | everything above in a new tree | data layer + JackAll world writing | 5 | see [building a new world](../modding/building-a-new-world.md) |

Two consequences for choosing a point:

- **Data edits reach everything a stage feeds.** An archetype edit reaches every entity of that
  type in every container. A sector edit reaches only that sector. A save that holds a record still
  overrides the captured properties for that one entity.
- **The two FCSE points sit on the narrowest joins.** Every spawn source meets at
  `SpawnEntityFromNode`, and every live entity passes the finalize listeners. A plugin there sees
  the whole world without knowing which file placed what **(RE-verified, server, for the call
  graph)**.

## Open questions

- **Retail addresses.** Every new trace here is on the server. None of `LoadWorld`'s order, the
  layer-state table, the finalize queue or the agent chain has been located in `Dunia.dll` yet.
- **A disabled layer's sector entities.** Do they stay, freeze or go? See stage 3.
- **The load flags.** Which flags single-player passes to `LoadWorld`, and what step 8's world Lua
  scripts are.
- **Entity vtable slot `+0x14`**, which runs right after `Load` with the node: unnamed.
- **Listener registration.** How a listener joins the entity system's list, and what its other
  slots are.
- **When a save's layer states apply.** Whether the save's `MissionManager` states are in place
  before the first sector streams, or arrive after and switch layers that already spawned.
- **How unbound sector entities reach the spawn.** Bound groups are listed in each layer's
  `BindingHierarchy` node; the rest have no entry in the data (stage 4).
- **Duplicate ids across classes.** The per-class id map overwrites within a class. What happens
  between classes was not read; retail `world1` ships 32 duplicate ids and loads.

## Cheapest next experiments

Each is one launch of the GOG install, which currently carries FCSE, UFCP, DevTools and Vehicle
Overhaul. Launch with:

```
"C:\Games\Far Cry 2\bin\FCSE.exe"
```

Continue a world-1 save, then open the console with `~`. A line starting with `#` runs as Lua
([developer console](./developer-console.md)). Do not save during or after experiments 1–3.

The place used below is **`SafeHouse_15`** in world 1, at about **x 1339, y 3230**, roughly 120 m
from the weapon shop marker `Objectives.WeaponShop_0` (read from `world1.mapsdata.fcb`). Its upgrade
layer `Level_06` places one entity in sector 4020: `MissionPickups.MedKit_56`, a medic station at
(1338.8, 3230.4, 35.2), 3.6 m from the safe house's marker **(seen in data)**. The same layer has
entities in 36 other world-1 sectors, most likely the other safe houses **(inferred)**.

### 1. Switch a layer from the console

No files. Stand inside `SafeHouse_15`. If the save has already reached upgrade level 6, the medkit
is already there; run step 4 first and continue from step 1.

1. `#CGameMissionMgr_GetInstance():GetMission("Missions/SafeHouse/Upgrades/W1/Level_06"):Enable()`
   — a medic station should appear beside the safe house's own marker without a reload.
2. `#CGameMissionMgr_GetInstance():GetMission("Missions/SafeHouse/Upgrades/W1/Level_06"):Disable()`
   — **the open question**: does the medkit stay, stay but stop working, or vanish?
3. `Enable()` again, as in step 1.
4. `#CGameMissionMgr_GetInstance():GetMission("Missions/SafeHouse/Upgrades/W1/Level_06"):Off()`
   — the medkit should vanish.

This settles the disabled-layer question and proves a runtime layer switch from Lua, the cheapest
way for a mod to show or hide content. If nothing happens at step 1, try the backslash spelling
`"missions\\safehouse\\upgrades\\w1\\level_06"`; the shipped graphs use forward slashes.

### 2. Spawn an animal, a soldier and a typo at runtime

No files. Stand outside `SafeHouse_15`. The call does not find the ground, so each spawn starts
3–5 m above the medkit's height and should drop.

1. `#DrawTextToScreen(tostring(SpawnEntityFromArchetype("Animals.Quadrupeds.CapeBuffalo", 1354, 3230, 40, 0, 0, 0)), 10)`
2. `#DrawTextToScreen(tostring(SpawnEntityFromArchetype("enemy_archetypes.Blue_Faction.Assault_Caucasian", 1354, 3240, 40, 0, 0, 0)), 10)`
3. `#DrawTextToScreen(tostring(SpawnEntityFromArchetype("Animals.Quadrupeds.NoSuchThing", 1354, 3230, 40, 0, 0, 0)), 10)`

Look for:

- **The buffalo:** appears about 15 m east, lands and wanders. That is a whole archetype-to-agent
  path with a brain, from a script.
- **The soldier:** appears dressed rather than as a 111-part wardrobe, and notices and fights you.
  That proves stage 6 for a runtime spawn.
- **The typo:** spawns nothing.
- **The on-screen numbers:** an id each time, and a different value for the typo. If no text ever
  shows, `DrawTextToScreen` is not live on PC. That is a finding too, and the spawns still answer
  the main question.
- **If anything hangs in mid-air:** physics did not start for a runtime spawn.

### 3. Travel to a world the campaign never visits

No files. This is step 1 of [building a new world](../modding/building-a-new-world.md#step-1-travel-to-tmpla),
which has the commands. They name a spawn point, because `GameChangeWorldDefaultSpawnPoint` leaves
the arrival position undefined ([where the player arrives](./world-loading.md#where-the-player-arrives)).

### 4. The mission-layer override, already built

One `jackall-cli mod build`. The experiment written down when `_layout.xml` shipped: convert
Scubrah's Patch and check that the `w1_b_2` outpost guard with id `2053840442929193718` in
`worldsector4027.data.fcb` is absent while its mission layer is off, and present once it is on.
Experiment 1's console call, given that layer's path, can switch it on without playing up to it.

### 5. An NPC with a brain that does not resolve

One layer with one archetype edit, built with `jackall-cli mod build`. It needs the Vehicle Overhaul
layer built alongside it, since the build replaces `patch.dat`. In
`enemy_archetypes.Blue_Faction.Assault_Caucasian`, change `Brain` to a name the workspace does not
hold, then spawn one with experiment 2's line 2. Stage 6 predicts a soldier who never reacts; a crash
would mean the engine assumes a brain. Not prepared yet.
