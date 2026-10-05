---
title: Critical healing animations on Infamous
kind: component
bundle: fixes
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
the contextual healing animations (pulling out a bullet, setting a bone) come back.

## How

`engine/gamemodes/gamemodesconfig.xml`, single-player `DefaultCountersService`,
`HealthDegenerationLevels/DifficultyLevel[3]@CurveName`:
`Curves.PlayerSicknessCurves.InfamousHealTime` -> `Curves.PlayerSicknessCurves.InfamousDegenerationRate`.
The base game names a heal-time curve where every other difficulty names a degeneration-rate curve.
This is the author's own fix from the guide
([player character](../../../docs/docs/modding/guide/player-character.md#bug-fix---restoring-critical-healing-animations-to-infamous-difficulty)),
and Scubrah's Patch carries the identical change
([`infamous-healing-animations`](../../scubrahs-patch/features/infamous-healing-animations.md)).
