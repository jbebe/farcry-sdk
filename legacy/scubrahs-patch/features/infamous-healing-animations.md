---
title: Critical healing on Infamous
kind: component
bundle: gameplay
claims:
  - "Added contextual healing animations to Infamous difficulty"
status: located
systems: [player]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/DefaultCountersService/HealthDegenerationLevels/**"
exclude: []
requires: []
verified: diff
---

# Critical healing on Infamous

On Infamous the player's health degenerates into the critical zone as on the other difficulties, so
the contextual healing animations (pulling out a bullet, setting a bone) come back.

## How

`engine/gamemodes/gamemodesconfig.xml`, single-player `DefaultCountersService`,
`HealthDegenerationLevels/DifficultyLevel[3]@CurveName`:
`Curves.PlayerSicknessCurves.InfamousHealTime` -> `Curves.PlayerSicknessCurves.InfamousDegenerationRate`.
The base game names a heal-time curve where every other difficulty names a degeneration-rate curve.
This is the fix in Boggalog's guide
([player character](../../../docs/docs/modding/guide/player-character.md)).

## Uncertain

- The mod also binds a held `H` to a new signal `criticalheal`; nothing reads that signal (see
  `noise-player-unused-criticalheal`), so it is not part of this fix.
