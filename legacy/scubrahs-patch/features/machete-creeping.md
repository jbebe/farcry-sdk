---
title: Machete creeping
kind: component
bundle: gameplay
claims:
  - "Added machete \"creeping\" (hold right mouse button) to avoid enemies hearing your footsteps when close behind them (thanks Boggalog)"
status: located
systems: [weapons, player]
match:
  - "**/entitylibrary*.fcb/weaponproperties/handtohand/**#**/IronSight/{bCanIronsight,fMoveSpeedFactor}"
exclude: []
requires: []
verified: diff
---

# Machete creeping

Holding the right mouse button with the machete out halves walking speed, for sneaking up behind
an enemy with mouse and keyboard.

## How

The machetes get an iron-sight mode: `CommonProperties/IronSight/bCanIronsight` `False` -> `True`
and `fMoveSpeedFactor` `1` -> `0.5` on all four machete weapon properties. "Aiming" with the
machete is the slow walk. This is the creeping recipe from Boggalog's guide
([weapons](../../../docs/docs/modding/guide/weapons.md)), minus its `fIronsightFOV` zoom - the mod
keeps the base game's `1.308` (75 degrees, no zoom).

## Depends on

The values sit in the four machete copies in `generated/entitylibrarypatchoverride.fcb`. The mod adds these copies to the override library, which outranks the world libraries. Each copy is compared with the declaration it overrides, so its values are separate changes and this page can be picked alone; the copy it writes keeps the base game's other values.

## Uncertain

- No footstep or noise value changes: the quieter approach is only the slower walk (the engine's
  walk-speed noise is inferred, not traced).
