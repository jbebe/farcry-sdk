---
sidebar_position: 17
---

# Entering a World — Travel, and What a World Is Made Of

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against `FarCry2_server` (the unstripped Linux dedicated-server binary —
see [Engine Architecture](./architecture.md) for why it is preferred for logic tracing). Addresses
below are that binary's unless marked otherwise. Cross-checked against the retail world trees and the
extracted `domino\` script corpus.
:::

How the engine gets from "not in a world" to "in a world", what identifies a world, and what files
one is made of. The companion pages cover the cooked payloads themselves: [`.fc2map`](../file-formats/fc2map.md)
for the editor's own level tree, [`.sdat`](../file-formats/sdat.md) for terrain,
[`.fcb`](../file-formats/fcb.md) for sector data.

## Every world entry is the same call

There is one mechanism, used by both the main menu and mission scripts. Both build a game-op node,
set a world **name** on it, and hand it to `CGame::SwitchContext`:

| Entry point | Address | What it does |
|---|---|---|
| `CFCXStoryAvatarSelectionPage::LaunchGame` | `0x08ec7f80` | The New Game button. Sets the avatar name, `SetWaitPlayGame(true)`, then `CryStringBase<char>("world1")` → `CGOContextNode::SetWorld` → `CGame::SwitchContext`. |
| `GameChangeWorld(CryStringBase<char>, EntityId)` | `0x088a0730` | The Lua binding. Builds a `CGOTravelNode`, `SetWorld(name)`, `SetSpawnPointId(id)`, `SetKeepPlayerState(true)` → `CGame::SwitchContext`. |
| `GameChangeWorldDefaultSpawnPoint(CryStringBase<char>)` | `0x088a0890` | Copies the string and calls `GameChangeWorld` with an invalid (`0xFFFFFFFF`) spawn-point id. |

The act 1 → act 2 transition is therefore **not** a special-cased story event; it is the general
travel node with `"world2"` in it. `GameChangeWorld` also calls
`AchievementUtils::UpdateAchievementsData_ChangeWorld` with the target name before switching.

`CGOTravelNode` carries three settable fields relevant here — world name, spawn-point entity id, and
a keep-player-state flag that `GameChangeWorld` always sets true.

## The world name is a string, and nothing enumerates worlds

Every world-scoped path is built by format string from the name, so the name is the only identifier:

| Format string | Produces |
|---|---|
| `%sWorlds/%s/%s/` | the world's generated directory (root, name, `CNomadPath` platform dir) |
| `%s%s.game.xml` | the world descriptor |
| `%s%s.managers.fcb` | managers |
| `%s%s.omnis.fcb` | omnipresent entities |
| `%sentitylibrary.fcb` | the world's archetype library |
| `%s%s_depload.dat` | the [dependency manifest](../file-formats/depload.md) |
| `%s%s_deploadnewparticles.rml` | the particle dependency manifest |
| `%sLevels/%s/%s/` | the level tree |
| `%sworldsector%d.data.fcb` | one sector's entity data |

There is no single-player world registry, whitelist or enum anywhere in the load path. The only
directory enumeration of `worlds\*` in the binary is at `0x08dfb4e0`, inside `SetupPage` — the
multiplayer map-select page.

`"world1"` appears as a hardcoded literal in exactly two places: `LaunchGame` above, and `LoadState`.

## The scripting surface is already wired

`Domino/System/ChangeWorld.lua` is a stock [Domino node](./domino-scripts.md) whose reflection header
declares a `Switch` control-in and two data-ins:

```lua
-- <DataIn Name="World"      Type="Core|string"/>
-- <DataIn Name="SpawnPoint" Type="Nomad|entity"/>
```

Its body calls `GameChangeWorld(self.World, self.SpawnPoint)` when a spawn point is supplied and
`GameChangeWorldDefaultSpawnPoint(self.World)` when it is not, then prints
`"Switching to world <name>"` via `DrawTextToScreen`. Both bindings are in the [Lua API
surface](./lua-api-surface.md) global batch 2.

Retail graphs that instantiate the node: `master_world1.world1`, `master_world2.world2`,
`sidemissions/grin_missions`, and two `gyms/gym_psm` test scripts. In `master_world1` it is box 239.

Domino ships as **plain Lua source**, and `patch.dat` outranks `common.dat` in the resolver chain
([archives](../file-formats/archives-fat-dat.md)), so a world change can be scripted with no binary
patching.

For a trigger volume to fire it, `CProximityTriggerComponent` is the only trigger type with geometry
— roughly 4,000 in `world1`, `vectorSize` is the box and the entity's `hidAngles.Z` the yaw. See
[Entities](./entity-instancing.md); whether `vectorSize` is a full or half extent is still open
there.

## What a world is made of

`CFCXEditorDocument::ExportWorld(char const* root, char const* name)` (`0x08cb6000`;
`0x107E28B0` in `Dunia.dll`) is the whole world-level cooker, and it is short. It takes the world
name as a parameter — `ige_map` is simply what the shipped editor passes:

| Output | How it is produced |
|---|---|
| `<name>.game.xml` | `XmlParser::parse` of `Worlds\tmpla\generated\tmpla.game.xml` as a **template**, then patched: `Grids/GridWorldMaps/Maps` gains `MainMap` and a `Map Name` of `Levels\<name>\<name>.nomad`, and `Environment/DefaultEnvSettings` gets `DefaultStormFactor`, `DefaultHour`, `DefaultMin` from the editor's terrain manager. |
| `<name>.managers.fcb` | `CopyFileFromTemplate` of `Worlds\tmpla\generated\tmpla.managers.fcb` |
| `<name>.omnis.fcb` | copied from `tmpla.omnis.fcb` |
| `entitylibrary.fcb` | copied from tmpla's |
| `<name>_depload.dat` | copied from `tmpla_depload.dat` |
| `<name>_deploadnewparticles.rml` | copied from tmpla's |
| `moviedata.xml` | copied from tmpla's |
| `<name>.mapsdata.fcb` | cooked by `ExportMapData` (`0x08cb57f0`) |
| `<name>.sectorsdep.fcb` | cooked by `ExportSectorDependencies` |

Five of the nine are verbatim template copies. `worlds/tmpla/` is the un-stripped development twin of
the editor slot ([`.fc2map`](../file-formats/fc2map.md)), which is why it is the template source.

The sector payloads are cooked separately by `ExportWorldSectors` (`0x08cb72b0`) into the level tree.

### The world descriptor's schema

`tmpla.game.xml` ships as plain text (campaign and MP worlds ship the same document as binary FCB), so
the schema is directly readable:

```xml
<WorldDescriptor>
  <Grids>
    <GridMapSectors CountX="8" CountY="8" Granularity="64" />
    <GridWorldMaps CountX="1" CountY="1" Granularity="1">
      <Maps>
        <Map Name="Levels\tmpla\tmpla.nomad" OffsetX="0" OffsetY="0"
             SectorOffsetX="0" SectorOffsetY="0" CountX="8" CountY="8" Granularity="64" />
      </Maps>
    </GridWorldMaps>
  </Grids>
  <Environment …>   <!-- biome GUID sets, DefaultEnvSettings, Sky, Clouds, Fog, Shadow,
                         CurvedHorizon, FakeTerrain, RealTreeCaps, TempHack -->
  <Layers>          <!-- the terrain layer table -->
  <MissionsDef>     <!-- Missions + MissionLayers -->
</WorldDescriptor>
```

A world is therefore a **sector grid plus a set of level windows into it**: each `Map` element claims
a rectangle of the sector grid and names the level that supplies it. That is the mechanism behind the
[grid sizes](../file-formats/fc2map.md) seen on disk — editor worlds are one 8×8 map, MP worlds 10×10,
campaign worlds 80×80 covered by 16×16 level cells.

`MissionsDef` matters for content: entities spawn from the mission layer they are nested under, so a
world needs at least one enabled layer for anything to appear.

### The per-level side

Per sector, a level supplies `worldsector<n>.data.fcb`, `sector<n>.desc.fcb`,
[`sector<n>.srl`](../file-formats/srl-zsr.md), `zonesector<n>.zsr`, and an `sdat/` group. Retail
single-player levels add `nv_<n>.nvm` ([navmesh](../file-formats/nvm.md)),
`sector<n>.preload_x.fcb`, and the `landmarkfar`/`landmarknear` LOD files, none of which the editor's
cooker emits.

Two of those are trivial to synthesize: `.srl` is exactly 1024 bytes and `.zsr` exactly 4096 bytes,
both raw fixed-size memory dumps with no header.

## Shipping a world

Archive resolution is CRC32-of-path across the mounted archives with `patch.dat` first, and the
`.fat` index is keyed by hash rather than by any per-archive path prefix. A world tree at a path the
retail archives never contained therefore resolves out of `patch.dat` with no archive-mounting work —
the fixed archive table and the six-name `worlds/*.dat` vector are not a gate on *content*, only on
which files the engine opens by name. See [archives](../file-formats/archives-fat-dat.md).

## What is not established

:::caution[Open]
Everything above is the mechanism. None of it amounts to a demonstration that a **new** world loads.
:::

- **No world outside the shipped set has ever been loaded.** Travel is proven only for
  `world1` → `world2`, both of which ship complete trees. Whether the single-player boot path,
  mission manager or save state assume a known world is untested — note that `LoadState` carries the
  `"world1"` literal too.
- **AI is blocked without a navmesh.** The editor's cooker emits none, and AI entities in a
  navmesh-less world crash the engine roughly 30 s after load, deterministically. Animals
  (`CAnimalAgent`) and interactables are unaffected. The generator (`CNavmeshGenerator`,
  `BuildNavMeshLevel0`, ~226 symbols) is compiled in but has no `FCE_Nav*` export.
- **Spawn-point semantics.** `SetSpawnPointId` takes an `EntityId`; which entity kind qualifies, and
  what `GameChangeWorldDefaultSpawnPoint`'s invalid id resolves to in a world with no mission
  definition, are both untraced.
- **Terrain authoring.** `SdatSector.Encode` exists in JackAll but is called only from tests; cropping
  and re-stitching shipped sectors (`SdatTerrainCrop`) is the proven path.

## A staged plan for testing this

Ordered so that the cheapest step falsifies the biggest assumption first.

1. **Travel to a world the campaign never visits.** Override a Domino graph in `patch.dat` to call
   `GameChangeWorldDefaultSpawnPoint`. Run it against `world2` first as a control — that target is
   known-good, so it isolates the hook — then against `tmpla` or an `mp_*` world, each of which ships
   a complete world *and* level tree. Costs one file edit and answers the load-path question with no
   authoring.
2. **Clone a shipped world under a new name.** Rename the tree per the table above, ship in
   `patch.dat`, travel to it. Separates "a name the engine has never seen" from "content the engine
   has never seen".
3. **Replace the clone's content.** One small map: cropped campaign terrain, hand-placed props, no
   AI.
4. **The portal itself.** A `CProximityTriggerComponent` entity wired to a `ChangeWorld` box, plus the
   return trip from a master graph in the new world.
