---
title: Optional enemy tactics capped at 90 %
kind: component
bundle: gameplay
status: located
systems: [ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/GameplayManagerService/GameplayManagement/AdaptativeBehavior/Item[{3,4,5,7,8,9,11}]@*"
exclude: []
requires: []
verified: diff
---

# Optional enemy tactics capped at 90 %

Enemy tactics that were certain - rescuing a downed comrade, manning a mounted gun, ranging a mortar,
driving at a distant sniper - now happen nine times in ten, so a fight is less predictable.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService/GameplayManagement/AdaptativeBehavior`,
the percent chance of each optional tactic per progression level
([AI: adaptive behaviours](../../../docs/docs/engine-internals/ai.md#adaptive-behaviours)). Every
`100` in these rows becomes `90`; lower values are untouched:

| Item | Behaviour (Boggalog's reading) | Levels changed |
|---|---|---|
| 3 | `ReachSniperWithVehicle` (drive at a distant attacker) | all 28 |
| 4 | `MountedWeapon` | all 28 |
| 5 | `ShootFlare` (call reinforcements) | 24-27 (the 0-75 ramp below is kept) |
| 7 | `RescueVictim` (drag a downed comrade to cover) | all 28 |
| 8 | `RangeWeapon` (range a mortar with a smoke shell first) | all 28 |
| 9 | `VehicleChaseLevel2` | 17-27 (the 0-75 ramp below is kept) |
| 11 | `LongRangeVehicle` | all 28 |

The readings are those of Boggalog's guide
([enemies: AI behaviours](../../../docs/docs/modding/guide/enemies.md#ai-behaviours)).
`Grenade`, `GrenadeAndBuilding`, `ShootInterestingObject` and `VehicleChaseLevel3` are unchanged, and
`ChaseWithVehicle` is `ai-fewer-vehicle-chases`.

## Compared with Scubrah's Patch

[`ai-fewer-rescues`](../../scubrahs-patch/features/ai-fewer-rescues.md) halves `RescueVictim` and
[`enemy-grenade-probability`](../../scubrahs-patch/features/enemy-grenade-probability.md) raises the
grenade rows; this mod leaves the grenade rows alone.

## Uncertain

- The detailed feature list speaks of enemies more likely to call reinforcements and throw grenades;
  in this table `ShootFlare` only goes down (at the top four levels) and the grenade rows are
  unchanged. Grenade counts, if anything, live in the weapon packs, not on this page.
