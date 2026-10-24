---
sidebar_position: 8.9
---

# Building a New World

What it takes to load a world Far Cry 2 does not ship, reached by console, portal or menu. This page
lists every file a world is made of, says which ones the tools can read, write and generate, and
lays out the steps to a player standing in a new world, with props and soldiers in it.

A new world has been loaded in game **(proven)**:
[`tools/TestRange`](https://github.com/jbebe/farcry-sdk/tree/main/tools/TestRange) builds
`testrange`, `tmpla`'s flat plain under a new name with cover walls and a generated navmesh, and
soldiers spawned there move and fight. It is the worked example for the sections below.
[`mods/new-world`](https://github.com/jbebe/farcry-sdk/tree/main/mods/new-world) builds two smaller
experiments, a renamed clone and a one-sector tile, which have not been loaded. Both build through
one shared script, `scripts\tmpla-world.ps1`. [The test sheet](#the-test-sheet) has the commands.

Marks: **(proven)** seen in a running game; **(RE-verified)** read in `FarCry2_server`'s code;
**(seen in data)** read in the retail files; **(inferred)**; **(untested)**. The mechanism is in
[entering a world](../engine-internals/world-loading.md).

## What is still missing

No tool or format is missing for a world built from `tmpla`, the editor's template. It is flat,
empty and complete ([`tmpla`](../engine-internals/world-loading.md#tmpla-is-a-flat-empty-world)).
Two questions stood before the first launch:

1. **Does the engine load a single-player world it does not ship?** Nothing enumerates or
   whitelists worlds **(RE-verified)**. Yes **(proven)**: `testrange`, `tmpla`'s 8 × 8 grid under a
   new name, loads by travel and the player arrives standing on its ground
   ([`tools/TestRange`](https://github.com/jbebe/farcry-sdk/tree/main/tools/TestRange)).
2. **Is a one-sector grid enough?** `CWorld::Create` checks no minimum **(RE-verified)**, but
   nothing after it was read.

Where the player lands is settled, and it follows world 2, not world 1
([where the player arrives](../engine-internals/world-loading.md#where-the-player-arrives),
**RE-verified**):

- A new game in world 1 spawns the player at the spawn point marked `bWorldStart`, moved down to
  the ground.
- Travel, which is how world 2 is entered, spawns the player at the named spawn point's stored
  position, with no ground snap.

Both retail start points are stored at ground level: 3 cm and 62 cm above the terrain under them.
So a new world reached by travel needs a spawn point placed on its ground. `newworld_tile`'s is
44 cm above. `tmpla`'s only spawn point sits 16 m below its ground, so travel there puts the player
under the terrain.

Anything beyond a flat world with props and soldiers runs into gaps that do have names: terrain
heights cannot be generated, vegetation cannot be packed, and the generated navmesh has no cover
points and ignores slopes. See [the checklist](#every-file-a-world-is-made-of).

## Every file a world is made of

**Needed** says what `CXGame::LoadWorld` does without the file
([what the loader requires](../engine-internals/world-loading.md#what-the-loader-requires)).

- **Read** and **Write** refer to JackAll codecs.
- **Generate** means making the file for a new world without starting from a retail copy.
- "via XML" means hand-written XML that JackAll encodes. Nothing builds those files from parameters.
- **Tile** is where the one-sector test world, `newworld_tile`, takes the file from.

### The world: `worlds\<w>\`

| File | What it holds | Needed | Read | Write | Generate | Tile |
|---|---|---|---|---|---|---|
| `generated\<w>.game.xml` | the grid, the level windows into it, environment presets, terrain layer table, missions | **yes**: no descriptor, no load | ✓ `rml decode`, or plain text | ✓ | by editing a template, as the editor's own `ExportWorld` does | `tmpla`'s, with the map name and grid edited |
| `generated\<w>.mapsdata.fcb` | spawn points, road, river and path splines, map markers | **yes**: opened with no null check | ✓ `fcb`, `WorldSplines` | ✓ | via XML; spline bounds and arc tables are not recomputed ([splines](../file-formats/splines.md)) | `tmpla`'s: group renamed, spawn point raised to 16.5 m |
| `generated\entitylibrary.fcb` | every archetype the world can spawn, the player included | loaded regardless; an empty library has no archetypes | ✓ | ✓ | ✗ copy only | `tmpla`'s, the same file as `ige_map`'s |
| `generated\<w>.managers.fcb` | world managers | skipped if absent | ✓ | ✓ | ✗ copy only | `tmpla`'s |
| `generated\<w>.omnis.fcb` | omnipresent entities, including the master Domino graph | skipped if absent | ✓ | ✓ | via XML | `tmpla`'s, empty |
| `generated\<w>.sectorsdep.fcb` | per sector: has a descriptor, detail mask, accessible, and [which of the sector's files the game loads](#which-sector-files-the-game-loads) | skipped if absent | ✓ | ✓ | via XML | `tmpla`'s, cut to sector 0 |
| `generated\<w>_depload.dat` | resource dependency tree for streaming | skipped if absent | ✓ | ✓ | ✓ `depload add` | `tmpla`'s |
| `generated\<w>_deploadnewparticles.rml` | particle dependencies | not traced | ✓ | ✓ | ✗ | `tmpla`'s |
| `generated\moviedata.xml` | cinematics | not established | text | text | text | `tmpla`'s |
| `generated\nv\sectors\nv.nvm` | navmesh grid and extent declaration ([`.nvm`](../file-formats/nvm.md)) | not traced | documented, no reader | ✗ | ✓ the header is fixed; the script writes a 1 × 1 one | written for 1 × 1, 108 bytes |
| `mapcompass.xbt`, `_mip0` | the world map texture | not traced | ✓ | ✓ | ✓ `xbt build`; the `_mip0` companion is not made | `tmpla`'s |

### Each level: `levels\<l>\`

A level is one window of the grid named by a `Map` element in the descriptor. A small world has one
level, named after the world.

| File | What it holds | Needed | Read | Write | Generate | Tile |
|---|---|---|---|---|---|---|
| `mapcompass.xbt`, `_mip0` | the area map | not traced | ✓ | ✓ | ✓ `xbt build` | `tmpla`'s |
| `generated\moviedata.xml` | cinematics | not established | text | text | text | `tmpla`'s |

### Each sector: `levels\<l>\generated\`

| File | What it holds | Needed | Read | Write | Generate | Tile |
|---|---|---|---|---|---|---|
| `sdat\sd<n>.sdat` | **terrain**: 65 × 65 heights, normals, holes, surface types, LOD tables ([`.sdat`](../file-formats/sdat.md)) | campaign worlds lack some | ✓ `SdatSectorFile` | ✓ `Encode`, used only by tests | ✗ the mask tables, normals and bounds are not derived | `tmpla`'s `sd0`, flat at 16.06 m, unchanged |
| `sdat\sd<n>_shadow.xbt` | baked sun shadow | not traced | ✓ | ✓ | ✓ from a DDS | `tmpla`'s |
| `sdat\atlas<a>_{diffuse,mask,color}.xbt` | terrain textures for a 2 × 2 block of sectors | falls back to sector-named files | ✓ | ✓ | ✓ from a DDS | `tmpla`'s `atlas0` |
| `worldsectors\sector<n>.desc.fcb` | detail texture mask, the list of other sectors with flags, terrain-detail resources | not traced | ✓ `SectorDetailLayers` | ✓ | via XML | `tmpla`'s, with the other sectors removed from its list |
| `worldsectors\sector<n>.srl` | **sound regions**: 32 × 32 bytes ([`.srl`/`.zsr`](../file-formats/srl-zsr.md)) | not traced | ✗ | ✗ | trivial: fixed-size, no header | `tmpla`'s |
| `worldsectors\zonesector<n>.zsr` | zones, 64 × 64 bytes | not traced | ✗ | ✗ | trivial | `tmpla`'s |
| `worldsectors\worldsector<n>.data.fcb` | **entities** by mission layer | no: `tmpla` ships none. Loaded only where `sectorsdep` sets `HasMainSectorData` | ✓ `WorldLoader` | ✓ Map tab, fragments onto a retail file | via XML ([placing entities](#placing-entities-in-a-new-world)) or the Map tab | none |
| `worldsectors\landmarkfar_<n>.data.fcb` | **vegetation** (`VegetationData`) and far LOD | no: `tmpla` ships none. Loaded only where `sectorsdep` sets `HasLandmarkFar` | ✓ decoded ([vegetation](../engine-internals/terrain-and-vegetation.md)) | ✓ as plain FCB | ✗ no packer for the nine lists | none |
| `worldsectors\landmarknear<n>.data.fcb` | near light-vegetation grid, some mission content | no. Loaded only where `sectorsdep` sets `HasLandmarkNear` | ✓ | ✓ | ✗ | none |
| `nv\sectors\nv_<n>.nvm` | **navmesh** for AI | no, but AI crashes without it. Loaded only where `sectorsdep` sets `HasNavMesh` | ✓ `NvmSector` | ✓ `NvmSector` | ✓ `navmesh build`, flat or gently rolling ground only ([AI in a new world](#ai-in-a-new-world)) | none, so no AI |
| `sector<n>.preload_x.fcb`, beside `generated\` | streaming preload, campaign levels only | no | ✓ | ✓ | ✗ | none |

Other parts of a world live elsewhere:

- **Environment** (sky, fog, light, wind) is a set of preset GUIDs in the descriptor's
  `Environment` element ([environment presets](./environment-presets.md)).
- **Roads** are splines in `mapsdata.fcb`.
- **Missions** are three things: `MissionsDef` in the descriptor, mission layers inside the `.fcb`
  files, and Domino graphs, which are plain Lua in `patch.dat`
  ([authoring a mission](./authoring-a-mission.md)).
- **Streaming** comes from the depload files, `sectorsdep.fcb`, the preload files and the landmark
  LODs. Its rings are set in the engine config, not per world.

## Which sector files the game loads

A sector's own files are not looked for on disk. Each entry of `<w>.sectorsdep.fcb` carries one
boolean per optional file, and the game requests only the files whose boolean is set. The file
names below are under `levels\<l>\generated\`.

| Flag | File it loads | How it is known |
|---|---|---|
| `HasMainSectorData` | `worldsectors\worldsector<n>.data.fcb`, the entities | **seen in data**: in `mp_16_airbase` the 30 flagged sectors are exactly the 30 files. Without it, `testrange`'s walls never appeared **(proven)** |
| `HasLandmarkNear` | `worldsectors\landmarknear<n>.data.fcb` | **seen in data**: 43 flags, the 43 files |
| `HasLandmarkFar` | `worldsectors\landmarkfar_<n>.data.fcb` | **seen in data**: 99 flags, the 99 files |
| `HasNavMesh` | `nv\sectors\nv_<n>.nvm` | **RE-verified** and **proven** ([how the game finds a sector file](../file-formats/nvm.md#how-the-game-finds-a-sector-file)) |

`tmpla` sets none of the four, because it ships none of those files. So every file a new world adds
to a sector needs its flag added to that sector's entry, or the file is silently ignored. A flag is
one more child of the entry, written as retail writes it:

```xml
<object type="CWorldSector">
  <value name="SectorId" type="Int32">28</value>
  <value name="HasDescriptor" type="Bool">True</value>
  <value name="HasMainSectorData" type="Bool">True</value>
  <value name="DetailMask" type="Int32">268435455</value>
  <value name="isSectorAccessible" type="Bool">True</value>
  <value name="HasNavMesh" type="Bool">True</value>
</object>
```

None of these files is listed in a depload. The flags are the only thing that makes the game
stream them.

## Placing entities in a new world

A new world's sectors have no retail `worldsector<n>.data.fcb` to start from, so a mod fragment, the
way [adding an NPC](./adding-an-npc.md) places one, has nothing to merge onto. `mod build` merges a
fragment only into a file some archive already provides. Write the whole file instead, then set
`HasMainSectorData` on its sector.

The smallest file is a sector header and a `main` mission layer, which is always on:

```xml
<object type="WorldSector">
  <value name="Id" type="UInt32">28</value>
  <value name="X" type="UInt32">4</value>
  <value name="Y" type="UInt32">3</value>
  <object type="MissionLayer">
    <value name="text_PathId" type="String">main</value>
    <value name="PathId" type="Hash">BF28CD64</value>
    <!-- Entity nodes go here -->
  </object>
</object>
```

- **Which sector:** an entity belongs to the sector its position falls in. With 64 m sectors and a
  grid `side` sectors wide, `X = floor(x / 64)`, `Y = floor(y / 64)` and `Id = X + side × Y`.
- **The entity:** copy a retail instance of the archetype you want. A placed prop is a
  `tplCreatureType` naming the archetype, plus whatever the instance overrides. Give it:
  - a new `hidName`
  - a new `disEntityId`, unique in the world
  - `hidPos` and `hidPos_precise`, both global, with z on the ground
  - the yaw in degrees as `hidAngles`' z
- **Collision** comes with the archetype. `testrange`'s walls are copies of
  `CoverObjects.WallBarrier01_BK.Multi_0` from `mp_16_airbase` and
  `CoverObjects.WallEroded10_BK.Multi_0` from `mp_07_m_dogon`.
- **Encode** the XML with `jackall-cli fcb encode`.

`tools\TestRange\build-layer.ps1` does all of this for 14 walls, and they stand in the game where they
were placed **(proven)**.

Place entities before generating the navmesh, so the navmesh is cut around them.

## AI in a new world

AI needs a navmesh, and retail `Dunia.dll` carries no generator of its own: the editor's
`CNavmeshGenerator` survives only in `FarCry2_server`. JackAll writes one instead:

```
jackall-cli navmesh build <layer>\mods --level <l>
```

It writes `nv\sectors\nv_<n>.nvm` for every sector of the level, cut around every placed entity
with static collision, and sets `HasNavMesh` on every sector's entry. The world's own `nv.nvm`
index can stay as `tmpla`'s, its grid and extent edited to match. The method and how it compares
with retail are in [generating a navmesh](../file-formats/nvm.md#generating-a-navmesh).

**(proven)** In `testrange`, soldiers spawned at run time move and shoot on it. In editor maps with no
navmesh, AI crashed the engine about 30 s after loading.

What it does not do yet:

- It writes **no cover points**, so soldiers are not expected to take cover behind props.
- It **never drops a slope**, so it is for flat or gently rolling ground only.

Soldiers can be placed as entities like any prop, or spawned at run time with
`SpawnEntityFromArchetype`. DevTools' Spawner tab spawns a squad of either faction, or of Ubisoft's
`enemy_archetypes.MercTest` test soldiers, in front of the player.

## The entity library

A world's archetypes come from its own `generated\entitylibrary.fcb`. `tmpla`'s is the same
file as the editor's `ige_map`'s: a superset of world 1's archetypes. It has every soldier class of
both factions and the `MercTest` soldiers, which world 1's lacks.

A mod's archetype edits target a path, usually `worlds\world1\generated\entitylibrary.fcb`, so they
do not reach a new world's library **(inferred)**. A world built on `tmpla`'s library plays plugin-only mods
unchanged, but not, for example, a mod that ships weapon data as archetype edits.

## The smallest world

`newworld_tile` is one 64 m sector of flat ground at 16.0625 m. It has no entities except
`SpawnPoint_0` at (28, 32, 16.5), and no AI. It takes 23 files: everything is `tmpla`'s, renamed,
and five are edited:

| File | Change |
|---|---|
| `newworld_tile.game.xml` | `GridMapSectors` and the one `Map` set to 1 × 1; the map renamed to `Levels\newworld_tile\newworld_tile.nomad` |
| `newworld_tile.mapsdata.fcb` | the map's group renamed from `tmpla`; the spawn point's z set to 16.5 |
| `newworld_tile.sectorsdep.fcb` | the group renamed; sector 0 kept |
| `nv\sectors\nv.nvm` | extent 64, grid 1 × 1, one sector |
| `sector0.desc.fcb` | the other 63 sectors removed from its list **(untested guess)** |

The engine finds a map's group in `mapsdata` and `sectorsdep` by the map's name, hashed as CRC32
like every FCB name (`newworld_tile` is `D748CFA2`). That is why the groups are renamed.

The layer also holds `newworld_clone`, which is `tmpla` renamed and nothing else: its spawn point
stays at z 0. It tells "a name the engine has never seen" apart from "content it has never seen".

## The plan

### Step 1: travel to `tmpla`

No files. `tmpla` ships a complete world and level tree, so this tests only whether single-player
loads a world the campaign never visits. Use world 2 as the control: it is known to load. Both
commands name a spawn point.

**Success:** a loading screen, and the game still running a minute later. The player arrives at
z 0, 16 m under the flat plain: expect to fall or be stuck under it. DevTools' noclip lifts them out
to see the plain.

### Step 2: the same world under a new name

`newworld_clone`. **Success:** exactly what step 1 showed. A failure here, after step 1 worked,
means the engine knows its worlds by name somewhere this research has not read.

### Step 3: one sector, standing on it

`newworld_tile`. **Success:** the player stands on flat ground with a 64 m square of terrain around
them, and the game keeps running. This is the goal state. If step 2 works and this one does not, the
cause is the grid size or the descriptor edit. Rebuild with `sector0.desc.fcb` left as it was to
tell which.

### Step 4: back to world 1

`#GameChangeWorld("world1", "2056971616016942572")`, which is `OpeningSequence_SpawnPoint`, the only
world 1 spawn point marked as a world start. **Success:** world 1 with the player's state intact.

### Step 5: save and load inside the new world

A save stores the world's name as a string ([savegame](../file-formats/savegame.md)), but `LoadState`
also holds a `"world1"` literal. Save in `newworld_tile`, quit to the menu, load. **(untested)**

### Step 6: reach it without the console

- **A portal** is data and a script. Put a trigger entity in world 1 and add a Domino graph that runs
  the stock `ProximityTrigger` box into a `ChangeWorld` box with `World` and `SpawnPoint` set, as
  `master_world1` does for world 2. The bus stations are the retail pattern for a trigger you
  choose to use ([interactive objects](./interactive-objects.md)). The way back is a Domino omni
  entity in the new world's `omnis.fcb`, the way the campaign attaches its master graph
  ([campaign](../engine-internals/campaign.md#how-a-missions-graph-starts)).
- **A menu entry** needs code. The New Game button hardcodes `"world1"` in
  `CFCXStoryAvatarSelectionPage::LaunchGame`. A plugin could route a new Magma button through the
  same travel call, or DevTools could gain a launch option alongside its `-load`.

### Step 7: own content

In order of what is ready:

- **Entities and props:** whole worldsector files plus their `HasMainSectorData` flag
  ([placing entities](#placing-entities-in-a-new-world)). The Map tab edits them once they exist.
- **Textures and map art:** `xbt build`.
- **Terrain heights:** `SdatSectorFile.Encode` can write a sector. A flat sector's tables can be
  reused, but real relief needs its normals, LOD masks and bounds derived, and nothing does that yet.
  Sector edges are shared, so neighbouring sectors must agree.
- **Vegetation:** needs a `VegetationData` packer.
- **Roads:** need the spline bounds and arc tables recomputed.
- **AI:** `jackall-cli navmesh build` ([AI in a new world](#ai-in-a-new-world)). Soldiers fight on
  it; cover points and slopes are still missing.

## The test sheet

Both scripts read the extracted retail `worlds` archive in `tmp\gamefiles\worlds\worlds`, and
neither layer is committed. `mod build` replaces the whole patch, so build in every layer the
install already carries, with these after them:

```
.\tools\TestRange\build-layer.ps1
.\mods\new-world\build-layer.ps1
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer <installed layers> --layer tools\TestRange\layer --layer mods\new-world\layer
```

The build was checked against a scratch copy of the vanilla patch archive: 406 entries added, none
overridden. The descriptor reads back byte-identical from its path hash. Nothing was built into the
install.

Each run is one launch. Load a world 1 save, press `~`, and enter the line:

| Run | Console line | Look for |
|---|---|---|
| 1, control | `#GameChangeWorld("world2", "2051908267268899174")` | world 2 at its act 2 start |
| 2 | `#GameChangeWorld("tmpla", "2054037628277228842")` | a load, with the player 16 m under a flat plain; noclip up to see it |
| 3 | `#GameChangeWorld("newworld_clone", "2054037628277228842")` | the same as run 2 |
| 4 | `#GameChangeWorld("newworld_tile", "2054037628277228842")` | standing on a 64 m square of ground |
| 5 | in run 4's world: `#GameChangeWorld("world1", "2056971616016942572")` | back in world 1 |
| 6 | `#GameChangeWorld("testrange", "2054037628277228842")` | standing on a plain, cover walls around you; spawned soldiers fight. Seen |

DevTools' Change world command takes the same two arguments.

After each run, `bin\fcse.log` holds
what the plugins saw. A crash or a hang on the loading screen is a result: note at which run it
happened.

If run 1 does nothing, the Lua binding may not take the id as a string from the console, though the
`ChangeWorld` box passes it that way. Try `#GameChangeWorldDefaultSpawnPoint("world2")` to see
whether the call itself works.

To go back, build the installed layers alone.

## Open questions

- What does the engine do with a player spawned under the terrain: fall forever, or stand on
  nothing? Run 2 shows it.
- Is a one-sector grid accepted past `CWorld::Create`? The streaming rings clamp to the world, sound
  sets up a 32 × 32 cell grid per sector, and `CAIEngine::LevelLoaded` was not read.
- Was emptying the sector descriptor's list right, and what is its leading value (123, member
  `56F4F69B`)?
- `tmpla`'s managers come from the editor's template. They are enough for soldiers to fight
  **(proven)**. Does the single-player game need managers they lack, such as weather or the economy?
- JackAll writes `mapsdata.fcb` 47 bytes longer than retail, because it does not share repeated
  objects. It decodes identically. Mod fragments are written the same way.
- Saving and loading inside a new world (step 5).
