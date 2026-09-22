---
sidebar_position: 14
---

# Entities — Archetype Resolution and Instancing

:::info[Verified via reverse engineering]
Traced via GhidraMCP against `FarCry2_server` (`CEntityLibraryManager::BuildArchetypesMap`,
`CEntitySystem::SpawnEntityFromNode`, `CReadOnlyMergeNode::BuildChildrenEntries` and its attribute
accessors) and `Dunia.dll` (`CXGame::LoadArchetypes`, the spawn and the merge node's accessors), with
counts measured from a retail install.
:::

An object in the world is described by up to three things at once: the library that defines its
archetype, a library that overrides that definition, and the placed instance itself. This page covers
how the engine collapses those into one live entity — which decides, for a modder, **which file an
edit has to go in to be visible at all**.

## The archetype table is one name-keyed map

`CEntityLibraryManager::BuildArchetypesMap(SerializableNodeRef const&)` walks a library's categories,
then each category's `EntityPrototype` children, reads the `hidName` of each prototype's `Entity`
child, and inserts into a single

```cpp
hashtable<CNoCaseStringID, ISerializableNode const*>
```

Three properties follow directly from that code, and any tool that reproduces it must match all
three:

- **The key is the fully qualified `hidName`** — `Animals.Quadrupeds.CapeBuffalo`, not the
  prototype's own shorter `Name` attribute.
- **Matching is case-insensitive**, because the key type is `CNoCaseStringID`.
- **Insertion replaces.** It walks the bucket chain for an existing entry with that id and, when it
  finds one, overwrites the node pointer instead of appending. Nothing is merged field by field at
  this level: a later definition replaces an earlier one whole.

Two callers fill that one map — `ReadFromXML` for a base library and `Override` for an override
library — so **the last library loaded wins**.

### A library's group order is not read

`BuildArchetypesMap` is the only code that walks a library's tree. Its two callers do nothing with the
root but retain it and hand it straight on, and `CXGame::LoadArchetypes` — their only caller — passes
each loaded file through once. The two accessors that read the map back,
`CEntityLibraryManager::GetArchetypeDescription` and `GetArchetypeName`, are bucket lookups keyed by
the id; neither touches a group.

Traversal order therefore reaches the game only as *which duplicate declaration survives the replace*,
and a duplicate is confined to a single group by construction, since the key is
`<group>.<prototype>`. **Reordering a library's groups cannot change any archetype the game
resolves.** Reordering prototypes *within* a group still can.

## Which libraries load, and in what order

`CXGame::LoadArchetypes` in the client:

```
if (flag at +0xC4 == 0)  load "\entitylibrary.fcb"
else                     load "\entitylibrary_full.fcb"
                         load "generated\EntityLibraryPatchOverride.fcb"
                         then a loop over further libraries
```

The base is an **either/or**, not a stack — one of the two, never both. The patch override then loads
unconditionally *after* whichever base was chosen, so it wins over both.

The trailing loop is the DLC libraries. That is read directly in the dedicated server, where the
equivalent function calls `CDlcService::GetEntityLibraries` and passes each path it returns to
`CEntityLibraryManager::Override`; Dunia's loop matches in shape but has not been read as the same
call (see [Unknowns](#unknowns)). Their order among themselves is not established, and they load
after the patch, so a DLC library wins over it.

:::info[Measured in a running game — the flag selects the suffix-less library in single-player]
Which branch the campaign takes is measured, not inferred: the same weapon archetype staged into all
six single-player containers at once, each carrying a different magazine size, shows which one the
game reads:

```
worlds\tmpla\generated\entitylibrary.fcb        21
worlds\tmpla\generated\entitylibrary_full.fcb   22
worlds\world1\generated\entitylibrary.fcb       23
worlds\world1\generated\entitylibrary_full.fcb  24
worlds\world2\generated\entitylibrary.fcb       25   <- observed, playing act 2
worlds\world2\generated\entitylibrary_full.fcb  26
```

So the single-player client takes the **`flag == 0`** branch and reads
**`worlds\<world>\generated\entitylibrary.fcb`** — the suffix-less library — for the world it is in.
`_full` is not read, and `tmpla` is not read.

Note this is the opposite of the reading the table below invites. `_full` being absent from the
dedicated server binary shows it is *not a server* library; it does not follow that it is the
client's base, because the client can take either branch and in the campaign it takes the other one.
:::

`entitylibrary_full.fcb` appears only in the client: the dedicated server binary contains no
reference to the string anywhere, while the suffix-less library appears in both. Measured over
`world1`:

| library | archetypes | relationship |
|---|---|---|
| `worlds\world1\generated\entitylibrary.fcb` | 1,419 | shared by client and server; **what single-player reads** |
| `worlds\world1\generated\entitylibrary_full.fcb` | 5,566 | strict superset, adds 4,147; not read in single-player |
| `generated\EntityLibraryPatchOverride.fcb` | 915 | loads last, wins — but see below |
| `worlds\ige_map\generated\entitylibrary.fcb` | 5,566 | identical content to `_full` |

**121** of the patch override's names are also declared by the world's own library, and **912** of
them are declared by `_full`. That second number is why the either/or matters: if the two bases
stacked, they and the patch would be contesting nearly every archetype the patch declares.

The replace-by-name rule also applies *within* one file: `_full`'s 5,735 prototype nodes carry only
5,566 distinct names, and the 169 redundant nodes belong to **29** names it declares more than once.
Only the last declaration of each survives the map. The base library and the patch override contain no
such duplicates.

:::warning[`EntityLibraryPatchOverride.fcb` does not ship in every edition]
It is **absent from the GOG Fortune's Edition**. That install's `patch.fat` holds 215 entries, its
only entity library is `worlds\tmpla\generated\entitylibrary.fcb`, and its four hash-only entries are
208–587-byte XML fragments. The name `generated\entitylibrarypatchoverride.fcb` (`F43E63CB`) is in
the hashlist, so a present file would have been resolved by name — it is genuinely not there.

The 915-archetype measurement above therefore describes a different edition (the community guides
reach it through a retail `patch_unpack`). Do not assume the override step exists before checking the
install in front of you, and do not attribute a dead archetype edit to it.
:::

## Instancing merges, it does not replace

`CEntitySystem::SpawnEntityFromNode(ISerializableNode const*)` branches on the instance's
`tplCreatureType`:

- **Absent** — the instance node is used directly. No archetype is consulted at all.
- **Present** — the archetype is looked up in `CEntityLibraryManager`, its `Entity` child taken, and
  the entity is loaded from a **`CReadOnlyMergeNode(archetype, instance)`** rather than from either
  node alone.

`CReadOnlyMergeNode::BuildChildrenEntries` defines the merge. It seeds its child array from the
**archetype**, then for each **instance** child reads that child's tag and scans for an unpaired
archetype child with the same tag:

| case | result |
|---|---|
| archetype has the child, instance does not | inherited unchanged |
| both have it | paired into a **nested** `CReadOnlyMergeNode`, recursing all the way down |
| only the instance has it | appended as a new child |

So an instance **overrides what it names and inherits the rest**, at node granularity, recursively.
It is a lazy read-only view over both trees — nothing is copied or flattened at load time.

### Attributes: the instance wins

:::info[Verified via reverse engineering]
Every `CReadOnlyMergeNode::getAttr*` and `getAttrEncode64` overload in `FarCry2_server`
(`0x09cc6d30`–`0x09cc78e0`), and the same accessors in retail `Dunia.dll` (merge node built by
`CReadOnlyMergeNode_Construct` at `0x10cf45b0`, vtable `0x10f49ab8`).
:::

The merge node holds the archetype at `+0x8` and the instance at `+0xC`. Every attribute accessor
asks the **instance** first and returns its value when the instance has the attribute; only on a
miss does it ask the archetype. A field present on both sides therefore takes the instance's value,
and a field only the archetype has is inherited.

`getAttributesCount` on a merge node returns 0: the engine never enumerates a merged node's
attributes, it only looks them up by id.

### A missing archetype drops the entity

`SpawnEntityFromNode` has three outcomes for an instance carrying `tplCreatureType`:

| case | result |
|---|---|
| the archetype resolves and has an `Entity` child | spawned from the merge |
| the archetype resolves but has no `Entity` child | spawned from the instance alone |
| the name resolves to no archetype | **not spawned**: the call returns the invalid entity proxy |

Retail `Dunia.dll` (`CEntitySystem_SpawnEntityFromNode`, `0x104e99a0`) takes the same three branches.
A typo in `tplCreatureType`, or an archetype missing from the library the world loads, makes the
entity silently absent.

After the merge, the spawn reads `hidEntityClass` and `disEntityId` through the merged node, so a
library `Entity` supplies the class and the instance supplies the id.

### Minimal placed instance

:::info[Verified against the retail corpus]
786 archetype-bound entities across 60 `w1_b_2` worldsector files.
:::

| field | carried by |
|---|---|
| `tplCreatureType`, `hidName`, `disEntityId`, `hidPos`, `hidPos_precise` | 786 of 786 |
| `hidAngles` | 622 |
| `hidResourceCount` | 567 |
| `hidEntityClass` | 0 — the library `Entity` supplies it |
| `Components` → `CEventComponent` (`hidHasAliasName`, empty `hidLinks`) | 786 of 786 |
| `Components` → `CGraphicComponent` with only the baked ambient fields | 682 |

The attributes appear in the order `tplCreatureType`, `hidName`, `disEntityId`,
`hidResourceCount`, `hidPos`, `hidAngles`, `hidPos_precise`. The graphic component on an instance
holds only the export's baked sky-occlusion and ground-colour values; the mesh comes from the
archetype.

Sampling 146 placed entities across `world1` sectors: **48** carry `tplCreatureType` and therefore
merge against an archetype; the other 98 stand alone. All 146 carry their own `Components` child, and
61 name their own `.xbg` mesh directly — which is why a renderer can draw most of a map without
opening a library, while a property inspector cannot.

## `hidMeshName` picks parts out of a wardrobe

:::info[Verified against the retail corpus]
:::

A graphics slot's `objModel` names the `.xbg`; its `hidMeshName` names **which parts of that file to
draw**, semicolon-delimited with empty ends:

```
;P_MC_CAUCASIAN_HEAD03;P_MC_UB_SHIRT_VEST02;P_MC_CAUCASIAN_SHIRT_ARMS;P_MC_LB_PANT_JEANS01;
 P_MC_DUMMYARMDEALERBEARD;P_MC_LB_BOOT02;P_MC_CAUC_HAIR01;…;P_MC_EYES_CAUCASIAN_HEAD03;
```

The names match the mesh's `DNKS` part names exactly. Empty — which it is on almost everything —
means draw the whole file.

It matters for exactly the files where it is set. `merc_kit.xbg` is a 111-part wardrobe referenced by
469 campaign sector files; without the list every mercenary in the game renders all 111 parts at
once, which is one body wearing seventeen faces and ten times the triangles it should have.

Note the shape: on worldsector entities the slot fields sit **flat on the component**, but on a
character they sit in a nested `object` child, and that is where `hidMeshName` lives. Reading only
the flat form finds nothing.

Outfits are effectively unique per NPC — 709 mercenaries in `world1` wear 682 distinct part lists —
so a tool that bakes geometry per outfit is baking almost per entity.

## Where entities are actually placed

:::info[Verified against the retail corpus]
:::

Three files per sector can place entities, and reading only the obvious one loses the set pieces:

| File | Holds |
|---|---|
| `worldsector<id>.data.fcb` | the bulk — props, vehicles, NPCs, fine building detail |
| `landmarknear<id>.data.fcb` | large-silhouette geometry, plus vegetation and spline volumes |
| `landmarkfar_<id>.data.fcb` | the same, biased to the largest features |

Counting distinct meshes placed per tier across both campaign worlds:

| | shells | roofs | windows/doors | interiors |
|---|---|---|---|---|
| `worldsector` | 65 | 58 | 104 | 17 |
| `landmarknear` | 26 | 22 | 6 | 0 |
| `landmarkfar_` | 1 | 0 | 0 | 0 |

The landmark tier is shell-and-roof heavy and holds **zero** interiors; the fine detail stays in the
worldsector file. In `world1` the landmark files are almost entirely cliffs and rock formations —
the skyline. **72 meshes are reachable no other way**, including every HQ building, the forts,
churches, the hotel, the C-130 and several roof and door pieces.

Landmark files also carry a `SectorEntity_*` vegetation container and `SplinePrimitive_*` occlusion
volumes, which have no geometry of their own and belong to the collection and spline systems.

### Buildings are kits

A medium building is not one mesh. `colonialmd01` ships as a shell, `roof_01`/`roof_02`,
`roofcap`, `roofshelter`, `roofshelter_open`, `windowsdoors_01/04/05`, `windowsdoors_open`,
`balcony_01/02` and an interior — one shell, many combinations, including open and closed shutter
variants.

The split is a visibility budget as much as a content one. The pieces carry deliberately unequal LOD
ladders, so detail retires before the silhouette does:

| piece | LODs |
|---|---|
| shell | 4 |
| roof, windows/doors, balcony | 3 |
| interior | 2 |

And they are authored to butt together exactly: `colonialmd01building_01` spans z −1.00→**7.75**,
`colonialmd01roof_01` spans **7.75**→9.40. Small buildings are not kits — `colonialsmall02_bld01` is
self-contained and includes its own roof.

## Mission layers decide when a placed entity exists

:::info[Verified via reverse engineering]
Traced via GhidraMCP against `FarCry2_server` (`CSectorSpawnCategory::OnReceivedSectorData`,
`CLayerResource::Spawn`/`Unspawn`, `CGameMissionMgr::IsLayerEnabled`, `CEntity::GetLayerName`) and
confirmed against the retail `Dunia.dll`, which runs the same walk.
:::

Every placed entity belongs to a **mission layer**, and the layer is what decides whether the entity
is in the world at all. Two separate bindings carry a layer, and they do different jobs — confusing
them produces a mod that looks right and behaves wrong.

**Where the entity sits in the sector container gates spawning.** Reading a sector's data walks the
container's mission-layer nodes and creates one layer resource per node, each owning the entities
nested under that node. When a layer turns on, that resource spawns exactly its own entities; when it
turns off, it removes exactly them. An entity nested under a layer is therefore reachable only
through that layer's on/off state.

**The `main` layer is always on.** The enabled test returns true for it unconditionally, without
consulting mission state. Every other layer is enabled only while some enabled mission lists it —
missions and their layer lists come from the world's mission definitions, which is why a mod that
adds a layer must also declare a mission that enables it.

**A mission component on the entity files a *live* entity into a layer.** It is read when an already
existing entity is added to or removed from a sector, when its owning layer is queried, and when it
is persisted to a savegame, so it survives a save/reload as instance state. It is not consulted when
the sector is first read, so it cannot decide whether the entity spawns.

**The component's layer field holds -1 when it names no layer**, and the read tests for that before
using it, answering `main` instead. That is the usual case rather than an edge one: across 40
untouched `w1_b_2` containers, 46 entities carry -1 and none carries a real layer id. A tool that
reads the field as an id concludes that most of the shipped game is mis-filed.

The practical consequence: **setting only the component moves nothing.** An entity that still sits
under `main` in the container spawns unconditionally at sector load, whatever its component says, and
the layer named by the component never controls it. Moving an entity between layers means moving it
in the container itself.

`entSpawnMissionTrigger` on an entity is a spawn-point property whose accessor has no callers on the
traced binary. It is not part of this mechanism.

### World-scope `mapsdata` is gated the same way

:::info[Verified via reverse engineering]
Traced via GhidraMCP against `FarCry2_server` (`CXGame::LoadWorld`, `CWorld::AddGlobalSpawn`,
`CGlobalSpawn::AddLayer`, `CWorld::GlobalSpawn`, `CWorld::OnLayerStateChanged`). Not yet read in
the retail `Dunia.dll`.
:::

Sector data is not the only layered container. `CXGame::LoadWorld` opens `<world>.mapsdata.fcb`,
and for each map descriptor hands the matching node to `CWorld::AddGlobalSpawn`. That walks the
node's `MissionLayer` children and registers each with `CGlobalSpawn::AddLayer`, one
`CGlobalSpawn::SLayerSpawn` per layer. `CWorld::GlobalSpawn(CPathID const&)` spawns a layer across
every registered global spawn, and has two callers: `LoadWorld` and `CWorld::OnLayerStateChanged`.
A mapsdata entity nested under a mission layer therefore appears when that layer turns on, like a
sector entity.

### Which containers carry mission layers

:::info[Verified against the retail corpus]
Every `worldsector*.data.fcb` under `worlds/` (5,100 files: both campaign worlds and the
multiplayer maps), `world1`'s `mapsdata`, `managers` and `omnis`, and a one-in-ten sample of the
landmark files (727 `landmarkfar_*`, 498 `landmarknear*`).
:::

| Container | Content under `MissionLayer` | Under a layer other than `main` |
|---|---|---|
| `worldsector*.data.fcb` | every placed entity | 9,139 of 188,632 entities, in 436 layers |
| `<world>.mapsdata.fcb` | shapes (`CBasicShapeEntity`), regions (`CGameRegion`, `CSocialRegion`, `CZoneLogicRegion`, `CBurnableRegion`, `CWagerRegion`), `COmniMapEntity`, `CSpawnPoint`, `CGameElementEntity`, `CFcxSplineCollectionEntity` | `world1`: 163 of 1,210 entities, in 52 layers; all 25 spline collections sit in `main` |
| `<world>.managers.fcb` | the manager singletons | none in `world1` |
| `<world>.omnis.fcb` | `COmniEntity` Domino hosts | none in `world1` |
| `landmarkfar_*.data.fcb` | `CSectorEntity` vegetation collections and event entities | 2 entities in 1 file of 727, under `missions\librarymissions\a2lm12\misnsubv_planeflight` |
| `landmarknear*.data.fcb` | `CSectorEntity` collections, event entities, a few Realtree and prefab entities | none |

In the sector files the non-`main` layers hold every kind of mesh-less entity from the table
[below](#a-third-of-a-worlds-entities-draw-nothing), not only props:

| Component on the entity | Under a non-`main` layer |
|---|---|
| `CFCXAIComponent` | 993 |
| `CProximityTriggerComponent` | 246 |
| `CNewParticlesComponent` | 102 |
| `CEntranceInfoComponent` | 77 |
| `CSoundComponent` | 74 |
| `CDynamicLightComponent` | 21 |
| `CRealtreeComponent` | 1 |

Sector descriptors (`sector<id>.desc.fcb`), `sectorsdep`, preload lists, terrain, water and navmesh
(`.nvm`) contain no `MissionLayer` node.

Layer names are paths. Across the sector files and `world1`'s mapsdata, `main` accounts for 5,124
layer nodes. The other names outside `missions\` are the multiplayer modes `fcxvip` (111), `fcxctf` (76), `fcxdeathmatch` (47), `fcxteamdeathmatch` (44),
plus `benchmark` (14) and a handful under `leveldesign\w2c3\a10_capital\`. Mission layers group under
`missions\` by family:

| Family | Layer nodes |
|---|---|
| `safehouse` | 698 |
| `weaponbazaar` | 280 |
| `storymissions` | 238 |
| `librarymissions` | 211 |
| `_disableformission` | 134 |
| `buddyunlockmissions` | 54 |
| `buddysidequests` | 39 |
| `convoymissions` | 30 |
| `openingsequence` | 28 |
| `assassinationmissions` | 27 |
| `grinmissions` | 24 |
| `randomencounters` | 19 |
| `carvertapemissions` | 17 |
| `vendormissions` | 15 |
| `ubidays` | 5 |

### Prefab instances are placed entities

Sector files place `CPrefabEntity` (5,248, 126 of them outside `main`) and
`CScriptedScenePrefabEntity` (160, 75 outside `main`) entities, e.g. `Lighting.LanternExplotator_62`.
They sit directly under a mission layer like any other entity.

:::info[Verified against the retail corpus]
All 233 prefab entities in `w1_b_2`'s 250 sectors.
:::

A prefab entity carries no `tplCreatureType`. It names its class in the string at hash `D2B3429E`
(`CPrefabEntity`), and its members are a `Children` list of `Child` records, each a `Name` string
and the child's `disEntityId` at hash `11D3633A`. The children are **not nested**: each is an
ordinary sibling entity elsewhere in the file.

| Measured over 2,866 `Child` records | |
|---|---|
| child found in the level's sectors | 2,019 |
| of those, under the parent's own mission layer | 2,019 |
| of those, in the parent's own sector file | 1,922 |
| child distance from the parent's `hidPos` | 0 to 19 m, median 3.9 m |
| a child that is itself a prefab | 0 |

So a child's `hidPos` is global like every other entity's, and moving a prefab means moving each
child. The 847 unresolved children most likely sit in sectors of neighbouring levels.

## Event links

:::info[Verified against the retail corpus]
All 320 links in `w1_b_2`'s 250 sectors. `ige_map.managers.fcb` carries 25 `hidLinks`, all empty.
:::

`CEventComponent/hidLinks` is a list of `Link` records. Each one wires an event the owning entity
raises to an event sent to a target entity.

| where | name or hash | type | meaning |
|---|---|---|---|
| `Link` | `AB7AED5F` | string | the output raised, e.g. `OnStateChange`, `STPSpecialActionTriggered` |
| `Link` | `7D1A6B64` | Int64 | target `disEntityId` |
| `Link` | `Event` | child | the event sent |
| `Event` | `CF68E402` | string | event class, e.g. `CLightEvent` |
| `Event` | `25368426` | u32 | CRC32 of that class name |
| `Event` | `hidEventName` | string | e.g. `DeactivateLight`, `PlaySound` |
| `Event` | `83F9B027` | u32 | 1 in all 320 |
| `Event` | `DCC35857` | Int64 | the target id again, equal in all 320 |
| `Event` | `hidType` | u32 | |

Some event classes append their own parameters, `CSoundEvent` five of them. 313 of the 320 targets
resolve inside the level, 309 in the source's own sector file. Link sources are mostly AI smart
terrain points, compound physics objects and time-of-day or relay triggers.

## Components read off an instance

Two component layouts confirmed from shipped sector data. Both hang off an entity's `Components`
child and are read the same way whether they came from the instance or were inherited. Every
component's full property list, with types and enum choices, is in
[the component and property registry](./entity-component-schema.md).

### `CDynamicLightComponent` — every placed light

There is no light file. Lights are a component on ordinary entities, named `OmniLight_*`,
`SpotLight_*`, `Lighting.*CampFire*` and similar; roughly 1,400 in `world1`, about half of them
spots, and many shipping disabled for mission logic to switch on.

| field | meaning |
|---|---|
| `hidType` | **1 = omni (point), 3 = spot** |
| `clrColor` | vec3, 0–1 |
| `fIntensity`, `fRadius`, `bEnabled` | |
| `bCastShadow`, `fShadowFactor` | |
| `llgLightGroup` | |
| `fTurnOffFallOff`, `fTurnOffDistance` | |
| `fFlickeringFrequency`, `fFlickeringAmplitude`, `fFlickeringNoise` | campfire flicker |
| `fOuterAngle`, `fInnerAngle` | spots only |

:::caution[Not lights]
`<world>.omnis.fcb` contains **no lights**. "Omni" there means *omnipresent*: world-scope entities
outside the sector grid. Retail `world1` holds five `COmniEntity` DLC Domino hosts; most maps ship a
22-byte empty shell.
:::

### `CProximityTriggerComponent` — the only trigger with geometry

Around 4,000 in `world1`, over half rotated, with meaningful names
(`ProximityTrigger_SafehouseCheck_*`, `W1C3_RE_trigger_Arena`). `vectorSize` is the box; the entity's
`hidAngles.Z` is the yaw in degrees. `CTimeOfDay`, `CDelay` and `CLookAtTriggerComponent` fire on
their own conditions and carry nothing to draw.

`CProximityTriggerComponent::IsInside` is **not** a geometric test — it walks a membership list that
physics maintains, so the box test lives in collider registration.

:::caution[Open]
Whether `vectorSize` is the box's full extent or a half-extent, and whether the box is centred on the
entity, are both unconfirmed — a 2× error either way.
:::

## A third of a world's entities draw nothing

:::info[Verified against the retail corpus]
:::

Of `world1`'s ~90,600 positioned entities, roughly 35,000 resolve to no mesh on themselves. They are
not one undifferentiated pool — each carries a component that names its purpose:

| Component set | Count | What it is |
|---|---|---|
| `CEventComponent` alone | 10,366 | pure logic nodes, the largest group |
| `+ CFCXAIComponent` | 3,143 | AI reference points — cover, guard posts, lean and sit spots |
| `CEntranceInfoComponent` / `CBuildingInfoComponent` | 1,204 | the `DOOR` and `WINDOW` hints AI navigates buildings by |
| `+ CPersistComponent` | 1,130 | |
| `CRealtreeComponent` | 807 | vegetation |
| `CNewParticlesComponent` | 448 | particle emitters |
| `CSoundComponent` | 325 | sound emitters |
| `CDynamicLightComponent` | 291 | lights |
| `CProximityTriggerComponent` | 207 | triggers |

Two things fall out of this for a tool. Lights, triggers and Realtree entities are *already* drawn by
their own systems, so a generic "entity has no mesh, draw a marker" pass stacks a second marker on
each of them. And an entrance node carries an AI component **as well as** its entrance one, so a
classifier that tests for AI first files all 1,204 door and window hints among 3,000 cover markers.

The count is an upper bound: about 7,800 of those entities do carry a `CGraphicComponent` and resolve
through the archetype fallback, so they are only mesh-less if you skip that step.

## Consequences for tools

- Resolve archetypes by **case-insensitive fully qualified `hidName`**, keeping the whole chain so
  the shadowed definitions stay inspectable.
- Read every library **through the VFS**, so archive priority, whole-file mod replacement and partial
  FCB fragment overrides are already applied. Game-internal and mod layering then live in one chain.
- Do not resolve a placed entity against `_full` while claiming to model the server, and do not
  resolve it against the base while claiming to model the client.
- Editing an archetype **does** change the 48-in-146 that reference one, and does nothing for the
  rest.
- Treat an entity's mission layer as **structural**. A tool that offers to change an entity's layer
  by editing its mission component alone is offering a change the engine will not honour.

## Unknowns

- What the flag at `+0xC4` selects between the two bases. None of the obvious write sites writes this
  field.
- Whether Dunia's loop after the patch override is literally `CDlcService::GetEntityLibraries`. In
  `FarCry2_server` it is: `CXGame::LoadArchetypes` (`0x08888750`) calls
  `CDlcService::GetEntityLibraries(CryVector<CryStringBase<char>>&)` and feeds each returned path
  through `CEntityLibraryManager::Override`. Dunia's loop has the same shape — a vector of strings
  walked at `0x1c` stride, each loaded through the same resolver slot and merged the same way — but
  the call itself has not been read there. Either way the DLC libraries land *after* the patch, so
  they win over it.
- Whether a placed `CPrefabEntity` does anything at runtime beyond listing its children, and whether
  it relates to the `CPrefabManager` descriptions in `<world>.managers.fcb` (see
  [object inventory](../file-formats/object-inventory.md)).
- Whether an instance with only the minimal fields above, and no baked graphic component, spawns in a
  running game. The corpus shows 104 archetype-bound instances without one; none has been placed and
  watched.
