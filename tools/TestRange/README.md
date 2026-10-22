# Test range

A world to fight in while working on the combat mods: `testrange`, `tmpla`'s flat 512 m plain with
14 cover walls around the spawn point. Nothing else is in it, so whatever happens there comes from
what you spawn into it. Like Sound Bench, it is a helper, not a mod for players.

| What | Where |
|---|---|
| Ground | flat at 16.06 m, 8 × 8 sectors of 64 m |
| Spawn point | `SpawnPoint_0`, id `2054037628277228842`, at (256, 240), on the ground |
| Walls | 7 barriers (1.2 m, crouch cover) and 7 eroded walls (1.8 m), 12 to 90 m from the spawn, ids from `2099100000000000000` |
| Navmesh | every sector, generated around the walls by `jackall-cli navmesh build`; no cover points |

`build-layer.ps1` writes `layer\` from the extracted retail `worlds` archive in
`tmp\gamefiles\worlds\worlds`. It copies `tmpla`'s files, so the layer is not committed. The walls
are retail instances, `CoverObjects.WallBarrier01_BK.Multi_0` from `mp_16_airbase` and
`CoverObjects.WallEroded10_BK.Multi_0` from `mp_07_m_dogon`, placed with a fixed seed: every build
puts them in the same places. The script then generates the navmesh and marks each sector
`HasNavMesh` in `sectorsdep`, which is what makes the game load it. Building the world itself is `scripts\tmpla-world.ps1`, shared with
[`mods/new-world`](../../mods/new-world).

## Use

Build the layer in with whatever else is installed:

```
.\tools\TestRange\build-layer.ps1
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer --layer mods\aiming-overhaul\layer --layer mods\wildlife-overhaul\layer --layer tools\TestRange\layer
```

Load any save, press `~` and travel:

```
#GameChangeWorld("testrange", "2054037628277228842")
```

DevTools' Change world command takes the same two arguments. To go back to world 1:
`#GameChangeWorld("world1", "2056971616016942572")`.

## Soldiers

DevTools' Spawner tab spawns a squad of 1 to 6 soldiers of either faction, or one of Ubisoft's test
soldiers, 25 m ahead and facing you.

The navmesh has no cover points, so soldiers are not expected to use the walls as cover yet. Nothing
here has been seen in game: in editor maps without a navmesh, AI crashed the engine about 30 s after
loading, and whether this navmesh prevents that is the first thing to check.
