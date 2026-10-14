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
| `GameChangeWorldDefaultSpawnPoint(CryStringBase<char>)` | `0x088a0890` | Copies the string and calls `GameChangeWorld` with an invalid (`0xFFFFFFFF`) spawn-point id, which no spawn point matches: see [where the player arrives](#where-the-player-arrives). |

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
[Entities](./entity-instancing.md); whether `vectorSize` is a full or half extent is open there.

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
world needs at least one enabled layer for anything to appear. That holds for the world-scope
`<world>.mapsdata.fcb` as well as the sector files — see
[which containers carry mission layers](entity-instancing.md#which-containers-carry-mission-layers).

### The per-level side

Per sector, a level supplies `worldsector<n>.data.fcb`, `sector<n>.desc.fcb`,
[`sector<n>.srl`](../file-formats/srl-zsr.md), `zonesector<n>.zsr`, and an `sdat/` group. Retail
single-player levels add `nv_<n>.nvm` ([navmesh](../file-formats/nvm.md)),
`sector<n>.preload_x.fcb`, and the `landmarkfar`/`landmarknear` LOD files, none of which the editor's
cooker emits.

Two of those are trivial to synthesize: `.srl` is exactly 1024 bytes and `.zsr` exactly 4096 bytes,
both raw fixed-size memory dumps with no header.

### How far around the player each kind loads

`engine\settings\defaultengineconfig.xml` sets the streaming rings in sectors. The file's own comments
name each value:

```xml
RingData="4,5,10,20,1"   <!-- entities, landmarkNear, landmarkFar, terrain, preload -->
RingLod="2,4"            <!-- Hires, physic -->
```

The PS3 and Xbox 360 configs use `4,4,10,20,1`. On the server, `CLoadRings::Init` takes `RingData`
only with exactly five values and `RingLod` only with exactly two. A ring is a square, not a circle:
`CWorldRegion::SubRegion` builds `[x − r, x + r] × [y − r, y + r]` around the player's sector, clamped
to the world. So entities load within a 9 × 9 sector box, ±256 m at 64 m per sector **(RE-verified on
the server; values seen in data)**.

### The map textures

The in-game map draws from three sets of `.xbt` textures (seen in data, with the UV code
**RE-verified on GOG**):

| Set | Files | Scale |
|---|---|---|
| The world map | `worlds\<world>\mapcompass.xbt` and `_mip0`, 1024² at full size | 3 m per pixel over X and Y from 1024 to 4096 |
| Area maps, one per level cell | `levels\w<n>_<row a–e>_<column 1–5>\mapcompass.xbt` and `_mip0`, 25 per world | 1 m per pixel |
| Detail maps | 45 `majorlocation_*.xbt` (21 in world 1, 24 in world 2), mostly with a `_mip0` | the entity's own `fWidth` × `fHeight` |

Row `a` is the band with the highest Y, and columns run from low X to high X. The world map's UV
(`0x106DF7E0`) is `(X − M) / (W − 2M)`, with the margin and the width taken from the world
descriptor. A detail map is centred on its entity (`0x106D7D80`). Whether +Y is north on screen
depends on the map gadget's quad, which was not read.

`CGameConfig`'s `UseCompassMiniMap` and `UseRoadSignHilight` are registered but read by no code
found, so they do nothing.

## What the loader requires

`CXGame::LoadWorld` (`0x08888f90`) loads the world-level files in this order. Its last argument is a
flag set that switches some steps on.

| Step | File | If it is missing |
|---|---|---|
| 1 | `Worlds\<name>\generated\<name>.game.xml` | `LoadWorld` returns false: `CFileManager::FileExists` gates everything else |
| 2 | `<name>_depload.dat`, else `<name>_depload.xml`, then a localised pass (`CWorldDescriptorImpl::LoadDep`, `0x09c2b0c0`) | skipped |
| 3 | `<name>.sectorsdep.fcb`, one group per map, found by the map's name | skipped |
| 4 | the descriptor's `Grids`, `Environment` (`CWorld::Create`, `0x095e18c0`) | — |
| 5 | `moviedata.xml`, the world's and each map's (flag 4) | not established |
| 6 | `entitylibrary.fcb` (flag 8, `CXGame::LoadArchetypes`, `0x08888750`), then the DLC libraries as overrides | loaded anyway: an empty library, so no archetypes |
| 7 | `<name>.managers.fcb`, `<name>.omnis.fcb` (flag 2) | skipped |
| 8 | `<name>.mapsdata.fcb`, then each map's group in it, found by the map's name | opened with no null check, so treat it as required |
| 9 | the `main` layer spawns; `MissionsDef` goes to `CGameMissionMgr::FromXML` | — |
| 10 | `CAIEngine::LevelLoaded` (flag `0x20`) | not traced |

`entitylibrary_full.fcb` is never opened. `XmlParser::parse` reads the descriptor whether it is
plain text (`tmpla`) or binary (the retail worlds). The world name never picks a script:
`CXSystemBase::LoadLuaScripts` (`0x088c34d0`) runs only `SCRIPTS\MissionTools.lua`. A world's master
graph is an omni entity in its `omnis.fcb` ([campaign](./campaign.md#how-a-missions-graph-starts)).

`CWorld::Create` sizes everything from `GridMapSectors`: `CBaseGrid::Init(CountX, CountY, 64)` and a
terrain of `CountX × 64` by `CountY × 64` metres. It checks no minimum, so a one-sector grid is not
rejected there.

### Per-sector paths

`CSector::GetFilePaths` (`0x097e95c0`) builds each sector's terrain paths from its map's level
folder:

- `sdat\sd<id>.sdat` and `sdat\sd<id>_shadow.xbt`, one of each per sector
- `sdat\atlas<a>_diffuse.xbt`, `_mask.xbt` and `_color.xbt`, shared by a 2 × 2 block of sectors
  (`CTerrain::kAtlasTextureSectorSideCount` is 2). For a world `w` sectors wide,
  `a = 2w × ⌊row / 2⌋ + 2 × ⌊column / 2⌋`, the id of the block's first sector.
- If neither an atlas `.xbt` nor a `.png` of the same name exists, a sector-named
  `sd<id>_diffuse.xbt` (and `_mask`, `_color`) is tried instead.

Campaign levels are missing some `.sdat` files and most `worldsector<n>.data.fcb` files, so a
missing sector file is tolerated. `tmpla` ships no `worldsector` file at all **(seen in data)**.

## Where the player arrives

The single-player game spawns the player when they join, in
`CFCXGRStateSinglePreGame::ProcessGenericEventOnPlayer` (`0x08983860`). It takes one of three
branches:

| Entry | Branch | Position |
|---|---|---|
| Loading a save | the player entity comes back from the save | the saved one |
| New game (world 1) | `CSpawnPointService::GetDefaultSpawnPoint` (`0x0940ac80`) | the spawn point with `bWorldStart` set, else the first one, moved to the ground by `GetPositionOnGround`. With no spawn point at all the call fails and the player is never spawned. |
| Travel (world 2) | `CWorldTravel::SpawnPlayer` (`0x092630b0`) | the named spawn point's stored position, unchanged |

`bWorldStart` is `CSpawnPoint`'s member at `+0xbc` (`CSpawnPoint::RegisterProperties`,
`0x096cef70`). Both branches that spawn a player end in `CPlayer::DoSpawnPlayer` (`0x0919c650`),
which sets the pawn's position as given and does not touch the ground.

**After travel.** `CWorldTravel::RetrieveTargetPos` (`0x092627d0`) returns an explicit target
position if the travel set one (same-world travel does). Otherwise it returns the position of the
`CSpawnPoint` whose entity id equals the travel's spawn-point id. If no spawn point matches, the
position is never written, and the player is spawned at whatever was on the stack. The invalid id
from `GameChangeWorldDefaultSpawnPoint` matches nothing, so **default-spawn travel places the player
at an undefined position**. Travel never snaps to the ground, so a travel target has to be stored at
ground height.

Retail never travels without a spawn point. `master_world1` sets `SpawnPoint` to
`"2051908267268899174"`, which is `SpawnPoint_W2A2_A03_MapStart` in `world2.mapsdata.fcb`. The
string goes straight into `GameChangeWorld`, so the binding must accept an entity id written as a
string **(inferred from retail use)**.

Each campaign world has exactly one `bWorldStart` spawn point, and both are stored just above the
terrain under them, sampled from the sector's `.sdat` **(seen in data)**:

| World | Spawn point | Stored z | Ground |
|---|---|---|---|
| world 1 | `OpeningSequence_SpawnPoint`, `2056971616016942572`, at (2509.36, 1190.10) | 20.887 | 20.857 |
| world 2 | `SpawnPoint_W2A2_A03_MapStart`, at (1169.25, 4636.81) | 25.056 | 24.435 |

**Streaming.** `CEntitySystemService::OnLoadWorld` (`0x091be350`) centres the streaming loader on
one of these:

- the spawn-point service's default: `CSpawnPointService::GetDefaultSpawnPoint` (`0x0940ac80`), which
  snaps to the ground and fails when the world has no spawn point
- with no such service, the first `CSpawnPoint` found
- otherwise (0, 0, 0)

A loaded save's spawn position overrides all three.

## `tmpla` is a flat, empty world

Seen in data:

- All 64 terrain sectors are flat at 16.0625 m. The `.sdat` files differ only in header fields:
  sector id, X and Y, and the environment snapshot. Every sector's `.srl`, `.zsr` and
  `_shadow.xbt` are byte-identical.
- No entity sectors and no landmark files. `tmpla.omnis.fcb` is empty (22 bytes).
- `tmpla.mapsdata.fcb` holds an empty spline collection and one spawn point, `SpawnPoint_0`
  (id `2054037628277228842`), at (28, 32, 0), 16 m below its own ground.
- Its six mission layers are the multiplayer modes, all off by default.
- `sector<n>.desc.fcb` lists every other sector of the map with a flags byte (53 in all of them).

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

- **No world outside the shipped set is known to load.** Travel is proven only for
  `world1` → `world2`, both of which ship complete trees. Whether the single-player boot path,
  mission manager or save state assume a known world is untested — note that `LoadState` carries the
  `"world1"` literal too.
- **AI is blocked without a navmesh.** The editor's cooker emits none, and AI entities in a
  navmesh-less world crash the engine roughly 30 s after load, deterministically. Animals
  (`CAnimalAgent`) and interactables are unaffected. The generator (`CNavmeshGenerator`,
  `BuildNavMeshLevel0`, ~226 symbols) is compiled in but has no `FCE_Nav*` export.
- **A one-sector grid** passes `CWorld::Create`, but nothing downstream of it was read for a
  minimum.

The file-by-file checklist, the plan to a player standing in a new world, and a prepared test layer
are in [building a new world](../modding/building-a-new-world.md).
