# Test range

A world to fight in while working on the combat mods: `testrange`, `tmpla`'s flat 512 m plain with
14 cover walls around the spawn point. Nothing else is in it, so whatever happens there comes from
what you spawn into it. Like Sound Bench, it is a helper, not a mod for players.

| What | Where |
|---|---|
| Ground | flat at 16.06 m, 8 × 8 sectors of 64 m |
| Spawn point | `SpawnPoint_0`, id `2054037628277228842`, at (256, 256), on the ground |
| Walls | 7 barriers (1.2 m, crouch cover) and 7 eroded walls (1.8 m), 12 to 90 m from (256, 240), ids from `2099100000000000000` |
| Navmesh | every sector, cut around the walls; no cover points |

It is built the way any new world is ([new worlds](../../docs/jackall/world/new-worlds.md)):

- `content\` is the only part committed: the walls, as the fragments JackAll's map editor saves.
  They are copies of retail instances, `CoverObjects.WallBarrier01_BK.Multi_0` from `mp_16_airbase`
  and `CoverObjects.WallEroded10_BK.Multi_0` from `mp_07_m_dogon`.
- `build-layer.ps1` makes the world's files from the installed game's `tmpla` with
  `jackall-cli world new`, then builds them and the walls into `layer\` with `jackall-cli world pack`:
  every sector's file, the entity library cut from `tmpla`'s full set, the sector flags and the
  navmesh. The layer is game files and is not committed.

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

To edit the range in JackAll: make `testrange` with the Map tab's **New map**, copy `content\mods`
into the workspace, and copy the workspace's `levels\testrange\` fragments back here when done.

## Soldiers

DevTools' Spawner tab spawns a squad of 1 to 6 soldiers of either faction, or one of Ubisoft's test
soldiers, 25 m ahead and facing you. The library keeps every soldier and vehicle archetype for that,
since the Spawner names them only at run time.

**Seen in game:** travel lands you standing on the plain among the walls, and spawned soldiers move
and shoot on the generated navmesh. The navmesh has no cover points, so soldiers are not expected to
use the walls as cover yet.
