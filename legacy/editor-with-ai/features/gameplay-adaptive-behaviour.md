---
title: Optional enemy tactics at full odds from the start
kind: component
bundle: gameplay
status: located
systems: [ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/GameplayManagerService/GameplayManagement/AdaptativeBehavior/**"
exclude: []
requires: []
verified: re
---

# Optional enemy tactics at full odds from the start

Enemies throw grenades, grenade buildings, chase in vehicles and shoot flares from the first
mission, instead of picking these tactics up as the player progresses.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService` → `AdaptativeBehavior`. Each `Item`
gives one optional tactic a percent chance for each of the 28 progression levels. The engine reads
it in `CFCXGameplayManager::GetBehaviorChancePercentage` (see
[AI](../../../docs/docs/engine-internals/ai.md#adaptive-behaviours)). The mod's table for the twelve
behaviours the engine asks for:

| Behaviour | Vanilla, level 0 to 27 | Mod |
|---|---|---|
| `Grenade` | 0, rising to 20 | 100 at every level |
| `GrenadeAndBuilding` | 10, rising to 50 | 100 |
| `ShootFlare` | 0, rising to 100 | 100 |
| `ShootInterestingObject` | 10, rising to 50 | 100 |
| `VehicleChaseLevel2` | 0, rising to 100 | 70 and 80, alternating by level |
| `VehicleChaseLevel3` | 0, rising to 80 | 75 and 85, alternating |
| `ReachSniperWithVehicle` | 100 | 85 and 95, alternating |
| `MountedWeapon` | 100 | 75 and 85, alternating |
| `ChaseWithVehicle`, `RescueVictim`, `RangeWeapon`, `LongRangeVehicle` | 100 | 100 |

Grenades, flares and vehicle chases are near-certain from the first level. Mounted weapons and
reaching a sniper by vehicle become slightly less likely than vanilla's 100.

The mod's table has 22 rows. Ten of them carry names the engine never asks for:
`GrenadeAndVenicle` (sic), `WalkRoundInterestingObject`, `VehicleChaseLevel1`,
`VehicleChaseLevel4`, and `ToUseCoverBox`, `ToUseCoverBuilding`, `ToUseCoverStone`,
`ToUseCoverPebble`, `ToUseCoverWood` and `ToUseCoverMetal`. None of those names is a string in
`Dunia.dll`, so these rows do nothing. The diff lines rows up by position, so some of its changes
read as edits of one row and the addition of another. The table above is the end result by name.

## Depends on

Nothing. The block belongs to `CFCXGameplayManager`, which runs in the campaign and, with this
mod, in the editor mode ([`ai-editor-mode-services`](ai-editor-mode-services.md)).
