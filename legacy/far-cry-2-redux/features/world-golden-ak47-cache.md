---
title: A second golden AK-47, guarded, in Bowa-Seko
kind: component
bundle: gameplay
status: located
systems: [world, weapons, ai]
match:
  - "levels/w2_b_2/generated/worldsectors/worldsector4339.data.fcb/*"
exclude: []
requires: []
verified: diff
---

# A second golden AK-47, guarded, in Bowa-Seko

A golden AK-47 spawn point is added at a new spot in the south map, watched by three riflemen and
a sniper. No readme line names it.

## How

`levels/w2_b_2` sector 4339, 10 new `main`-layer entities, every one a copy of a vanilla entity
from another sector, keeping its name and entity id:

| Entity | Copied from |
|---|---|
| `EntitySpawner_0` (`archArchetypeToSpawn` `pickups.Weapons.AK47_new.AK47_Gold`) | `w2_b_3` sector 3958, the vanilla golden AK-47 spawn |
| `Blue_Faction.Assault_Caucasian_10` | `w2_b_2` sector 4579 |
| `Blue_Faction.Assault_Caucasian_16`, `Assault_Nubian_12` | `w1_b_3` sector 3959 |
| `Blue_Faction.Sniper_Caucasian_1` | `w1_b_3` sector 4040 |
| `Duty.Guard_27`, `_28`, `_37`, `_38`, `Duty.Patrol_24` (smart points) | `w1_b_3` sector 3959 |

The spawner stands at 1271.95, 3504.65, 24.3. The soldiers and points are all set to round
positions a few metres from it, at height 30 (1273-1279, 3505), about 6 m above the spawner, as if
typed in by hand. The vanilla sector holds only a patrol pair there.

The vanilla golden AK-47 spawn in sector 3958 (2457.57, 3154.15) is not touched, so world 2 now
has two.

## Compared with Scubrah's Patch

[`golden-ak47-shop`](../../scubrahs-patch/features/golden-ak47-shop.md) sells the golden AK at the
bazaar after ten buddy quests instead.

## Uncertain

- The new spawner and `Blue_Faction.Assault_Caucasian_10` share their entity ids with the vanilla
  entities they were copied from in the same world. How the engine handles two entities with one
  id - both live, one dropped, or one shared spawned state (`CEntitySpawner` saves
  `bAlreadySpawned`, [savegame](../../../docs/docs/file-formats/savegame.md)) - is not traced or
  checked in game. The world 1 ids do not collide in world 2.
- That a spawner with no event links spawns by itself is inferred from the vanilla one, which has
  none either.
- Whether the soldiers placed 6 m up settle on the ground is not checked.
