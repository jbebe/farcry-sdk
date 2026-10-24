# Test range

A world to fight in while working on the combat mods: `testrange`, `tmpla`'s flat 512 m plain with
a hub of cover walls in the middle and an arena for each kind of fight around it. Nothing else is in
it, so whatever happens there comes from what you spawn into it. Like Sound Bench, it is a helper,
not a mod for players.

| What | Where |
|---|---|
| Ground | flat at 16.06 m, 8 × 8 sectors of 64 m |
| Spawn point | `SpawnPoint_0`, id `2054037628277228842`, at (256, 256), on the ground |
| Hub | 7 barriers (1.2 m, crouch cover) and 7 eroded walls (1.8 m), 12 to 90 m from (256, 240) |
| Arenas | the five in [Scenarios](#scenarios), 19 more walls |
| Navmesh | every sector, cut around the walls; no cover points |

It is built the way any new world is ([new worlds](../../docs/jackall/world/new-worlds.md)):

- `content\` holds the walls, as the fragments JackAll's map editor saves, and the three archetypes
  only the scenarios name, as library fragments. The walls are copies of retail instances,
  `CoverObjects.WallBarrier01_BK.Multi_0` from `mp_16_airbase` and
  `CoverObjects.WallEroded10_BK.Multi_0` from `mp_07_m_dogon`.
- `plugin\` is an FCSE plugin, the scenarios' window in DevTools' overlay.
- `build-layer.ps1` makes the world's files from the installed game's `tmpla` with
  `jackall-cli world new`, builds the plugin, then packs both with the content into `layer\` with
  `jackall-cli world pack`: every sector's file, the entity library cut from `tmpla`'s full set, the
  sector flags and the navmesh. The layer is game files and is not committed.

## Use

Build the layer in with whatever else is installed:

```
.\tools\TestRange\build-layer.ps1
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer --layer mods\aiming-overhaul\layer --layer mods\wildlife-overhaul\layer --layer tools\TestRange\layer
```

Load any save, press `~` and travel, or press **Travel to the range** in the Test range window:

```
#GameChangeWorld("testrange", "2054037628277228842")
```

To go back to world 1: `#GameChangeWorld("world1", "2056971616016942572")`.

To edit the range in JackAll: make `testrange` with the Map tab's **New map**, copy `content\mods`
into the workspace, and copy the workspace's `levels\testrange\` fragments back here when done.

## Scenarios

The **Test range** window, in DevTools' overlay, has a button per scenario. A scenario covers rows
of the Difficulty Overhaul's [finish checklist](../../docs/docs/design/difficulty.md#finish-checklist).
**Start** does the following, all as one line of the game's own Lua:

1. Removes everything the last scenario spawned.
2. Sets the time to noon, so runs compare.
3. Moves you onto its arena's start, facing the enemy. `TeleportEntity` takes you to a spawn point
   spawned there, and copies that spawn point's position and facing.
4. Spawns its soldiers, each on a guard or sniper post when he should hold his ground.

Only one scenario's soldiers are ever alive, so the arenas do not disturb each other. Hovering a
button shows what to do and what to look for; the running scenario's steps stay in the window.
Toggle DevTools' invincibility yourself: it is on unless the scenario tests damage to you.

| Arena | Where | Scenarios | Rows |
|---|---|---|---|
| Duel lane | open ground east of (30, 110) | rifleman at 10, 20 and 50 m; machine gunner at 20 m; two riflemen at 20 m; shotgunner at 10 and 40 m | 2 to 8 |
| Barrier | a barrier at (60, 40), the soldier 30 m north | rifleman over the barrier | 9 |
| Long wall | a 42 m wall at y 440, an 18 m wall 20 m behind it, the squad 40 m south | squad at the long wall | 11, 13, 14, 15 |
| Suppression | two 18 m lines at x 420 and 460 | two riflemen behind cover | 12 |
| Sniper | a 12 m wall 3 m in front of (380, 370), the sniper at (480, 480) | sniper at 150 m | 9, 15 at range |

Not covered yet:

- **Row 10, tall grass:** the range has no grass.
- **Pistols and SMGs in row 8:** no soldier archetype carries one as its main weapon, so only the
  shotgun is tested.

**Seen in game:** travel lands you standing on the plain among the walls, and spawned soldiers move
and shoot on the generated navmesh. The scenarios have not been run yet. Whether a spawned soldier
holds a post spawned beside him is their first open question.
