---
title: Sprint stamina drain on the wrong curve
kind: component
bundle: gameplay
status: located
systems: [player]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/{DefaultCountersService,MPCountersService}/PlayerSickness/Stamina@SprintDrain"
exclude: []
requires: []
verified: diff
---

# Sprint stamina drain on the wrong curve

`engine/gamemodes/gamemodesconfig.xml`: in both `DefaultCountersService` and `MPCountersService`,
`PlayerSickness/Stamina@SprintDrain` changes from `Curves.PlayerSicknessCurves.StaminaSprintDrain`
to `Curves.PlayerSicknessCurves.HealthMax_Infamous`. That curve is the one the game uses for
maximum health at the Infamous reputation tier.

## Uncertain

- What sprinting costs now depends on that curve's values read as a drain rate. Neither the curve
  nor its effect has been checked. It may be a deliberate way to sprint longer or a slip.
