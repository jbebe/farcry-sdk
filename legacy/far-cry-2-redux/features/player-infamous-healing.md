---
title: Critical healing animations on Infamous
kind: component
bundle: fixes
claims: []
status: located
systems: [player]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/DefaultCountersService/HealthDegenerationLevels/**"
exclude: []
requires: []
verified: diff
---

# Critical healing animations on Infamous

On Infamous the player's health degenerates into the critical zone as on the other difficulties, so
the contextual healing animations (pulling out a bullet, setting a bone) come back. This is the
"infamous healing fix" of the mod's description.

## How

`engine/gamemodes/gamemodesconfig.xml`, single-player `DefaultCountersService`,
`HealthDegenerationLevels/DifficultyLevel[3]@CurveName`:
`Curves.PlayerSicknessCurves.InfamousHealTime` -> `Curves.PlayerSicknessCurves.InfamousDegenerationRate`.
The base game names a heal-time curve where every other difficulty names a degeneration-rate curve
([player character guide](../../../docs/docs/modding/guide/player-character.md#bug-fix---restoring-critical-healing-animations-to-infamous-difficulty)).

## Depends on

Nothing. Realism Plus and Scubrah's Patch carry the identical change
([`player-infamous-healing`](../../realism-plus/features/player-infamous-healing.md),
[`infamous-healing-animations`](../../scubrahs-patch/features/infamous-healing-animations.md)).
