---
title: Checkpoint guards give chase by car a quarter of the time
kind: component
bundle: gameplay
status: located
systems: [ai, vehicles]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/GameplayManagerService/GameplayManagement/AdaptativeBehavior/Item[2]@*"
exclude: []
requires: []
verified: diff
---

# Checkpoint guards give chase by car a quarter of the time

Driving through or past a guard post no longer guarantees a vehicle chase.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService/GameplayManagement/AdaptativeBehavior`:
the item `behavior="ChaseWithVehicle"` (index 2) goes from `100` to `25` at all 28 levels
(`level0`-`level27`). `CFCXGameplayManager::GetBehaviorChancePercentage` reads the column of the
current progression level as a percent chance
([AI: adaptive behaviours](../../../docs/docs/engine-internals/ai.md#adaptive-behaviours)).
Boggalog's guide reads `ChaseWithVehicle` as "chasing the player when they drive through
checkpoints" ([enemies: vehicle use](../../../docs/docs/modding/guide/enemies.md#vehicle-use)).

The rest of the table is `ai-tactics-not-certain`.

## Compared with Scubrah's Patch

[`fewer-vehicle-chases`](../../scubrahs-patch/features/fewer-vehicle-chases.md) gets fewer chases by
rewriting the vehicle brain's seat priorities and capping chase speed; this mod only lowers the odds.

## Uncertain

- The guide's reading of the behaviour is the guide's; which plans test `ChaseWithVehicle` is not
  traced.
