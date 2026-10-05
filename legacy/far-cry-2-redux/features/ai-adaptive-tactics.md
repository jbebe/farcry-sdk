---
title: Enemy tactics re-weighted - grenades and flares from the start, fewer car chases
kind: component
bundle: gameplay
status: located
systems: [ai, vehicles]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/GameplayManagerService/GameplayManagement/AdaptativeBehavior/**"
exclude: []
requires: []
verified: diff
---

# Enemy tactics re-weighted - grenades and flares from the start, fewer car chases

Enemies throw grenades, fire flares for reinforcements and shoot at explosive objects near the
player from the first hours of the game, where vanilla holds these back until later. In exchange
they chase the player by car far less often early on, and less often at any point. This is the
data behind the "more aggressive enemies" of the mod's description; no readme line names it.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService/GameplayManagement/AdaptativeBehavior`:
the percent chance of each optional tactic per progression level `level0`-`level27`, which
`CFCXGameplayManager::GetBehaviorChancePercentage` reads for the level the weapons service reports
([AI: adaptive behaviours](../../../docs/docs/engine-internals/ai.md#adaptive-behaviours)).
264 values in ten of the twelve rows:

| Item | Behaviour | Vanilla, by level | Mod, by level |
|---|---|---|---|
| 0 | `Grenade` | 0 (0-2), 5, 10, 15, 20 (24-27) | 45 (0-7), 40 (8-12), 50, 60, 65, 60, 70, 80 (26-27) |
| 1 | `GrenadeAndBuilding` | 10 rising to 50 | 55 (0-16), 60, 65, 60, 70, 80 (26-27) |
| 2 | `ChaseWithVehicle` | 100 | 1 (0), 10, 20, 40, 50, 60 (16-27) |
| 3 | `ReachSniperWithVehicle` | 100 | 50 |
| 5 | `ShootFlare` | 0 (0-2), 25, 50, 75, 100 (24-27) | 75 (0-24), 80 (25-26), 100 (27) |
| 6 | `ShootInterestingObject` | 10 rising to 50 | 20-30 (0-4), 40, 45, 60, 70 (24-27) |
| 8 | `RangeWeapon` | 100 | 90 |
| 9 | `VehicleChaseLevel2` | 0 (0-4), 50, 75, 100 (17-27) | 0 (0-3), 10, 15, 20, 55 (9-16), 70 (17-27) |
| 10 | `VehicleChaseLevel3` | 0 (0-8), 20, 50, 80 (22-27) | the same curve as `VehicleChaseLevel2` |
| 11 | `LongRangeVehicle` | 100 | 0 (0-2), 10, 20, 55 (9-16), 70 (17-27) |

`MountedWeapon` (4) and `RescueVictim` (7) keep 100 at every level. The readings of the behaviour
names are those of Boggalog's guide
([enemies: AI behaviours](../../../docs/docs/modding/guide/enemies.md#ai-behaviours)).

## Compared with other mods

- Realism Plus caps the certain tactics at 90 % and cuts `ChaseWithVehicle` to 25 %
  ([`ai-tactics-not-certain`](../../realism-plus/features/ai-tactics-not-certain.md),
  [`ai-fewer-vehicle-chases`](../../realism-plus/features/ai-fewer-vehicle-chases.md)); it leaves
  the grenade rows alone.
- Scubrah's Patch raises the grenade rows
  ([`enemy-grenade-probability`](../../scubrahs-patch/features/enemy-grenade-probability.md)) and
  halves `RescueVictim` ([`ai-fewer-rescues`](../../scubrahs-patch/features/ai-fewer-rescues.md)).

## Uncertain

- The level column follows the progression level, not the difficulty setting, so "from the start"
  means early in the campaign, on every difficulty.
- Which plans test `ShootInterestingObject` and the vehicle rows is not traced; the readings are
  the guide's.
