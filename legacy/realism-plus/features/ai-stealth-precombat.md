---
title: Alerted soldiers see less far
kind: component
bundle: gameplay
status: located
systems: [ai]
match:
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/**#**/FOVMultipliers/*"
exclude: []
requires: []
verified: diff
---

# Alerted soldiers see less far

Soldiers who are alerted but not yet fighting spot the player from a shorter distance, so breaking
contact and slipping away is easier. Assassination targets lose the long sight they had at every
alert level.

## How

`CFCXAIComponent/AIObject/CPawnAgent/SensorySystem/FOVParameters/FOVMultipliers` in the soldier
archetypes the mod copies into `generated/entitylibrarypatchoverride.fcb` (they redeclare
`worlds/world1` or `worlds/world2`, so each value is its own change):

- `fPreCombatMultiplier` `0.75` -> `0.6` on the riflemen, machine gunners and shotgunners of both
  factions (`Assault_*`, `LightMachineGunner_*`, `ShotgunMan_*`) and the two A1LM04 special forces
  archetypes (`Special.SpecOps_Assault`, `SpecOps_Shotgun`).
- `fPreCombatMultiplier` `4` -> `3` on the snipers, rocket men, mortar men and the Carl Gustaf gunner
  (`Sniper_*`, `RocketMan_*`, `MortarMan_*`, `Blue_Faction.CarlGustaf_Nubian`).
- `Missions.Assassination_Target`: `fPreCombatMultiplier`, `fCombatMultiplier` and
  `fPostCombatMultiplier` all `4` -> `0.6`, `1` and `1.25`, the ordinary rifleman's values.

The engine multiplies the sight cone's length by `fPreCombatMultiplier` while a soldier is alerted
before or after a fight ([AI: seeing](../../../docs/docs/engine-internals/ai.md#seeing)), so a 20 %
cut is 20 % less distance in that state; idle soldiers are unaffected. This is the "Perception
pre-combat" recipe of Boggalog's guide
([enemies](../../../docs/docs/modding/guide/enemies.md#perception-pre-combat)).

The new archetypes the mod adds (`patrols-drivers`, `patrols-convoy-smugglers`) are written with
`0.6` from the start.

## Compared with Scubrah's Patch

[`ai-stealth-senses`](../../scubrahs-patch/features/ai-stealth-senses.md) lowers the same value to
`0.6` on the 12 riflemen, machine gunners and shotgunners, but also `fCombatMultiplier`,
`fPostCombatMultiplier` and `fPlayerInVehicleMultiplier`, and leaves the 4x classes alone. This mod
changes only the pre-combat value, and on the long-range classes too.
