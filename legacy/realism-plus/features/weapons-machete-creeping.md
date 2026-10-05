---
title: Machete creeping
kind: component
bundle: gameplay
status: located
systems: [weapons, player]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/handtohand/*.xml#**/IronSight/{bCanIronsight,fMoveSpeedFactor,fIronsightFOV}"
exclude: []
requires: []
verified: diff
---

# Machete creeping

Holding the aim button with a machete out halves walking speed and zooms in slightly, for sneaking
up behind an enemy with mouse and keyboard.

## How

The machetes get an iron-sight mode. On `WeaponProperties.HandToHand.Machete`, `Machete_HomeMade`
and `Machete_Primitive`, the mod's copies in `generated/entitylibrarypatchoverride.fcb`:

- `CommonProperties/IronSight/bCanIronsight` `False` -> `True`
- `fMoveSpeedFactor` `1` -> `0.5`
- `fIronsightFOV` `1.308` -> `1.3` (radians: from 74.9 to 74.5 degrees, a barely visible zoom)

This is the creeping recipe from the mod author's own guide, all three values
([weapons](../../../docs/docs/modding/guide/weapons.md#guide---silent-machete-assassinations)).
Scubrah's Patch makes the same change minus the zoom
([`machete-creeping`](../../scubrahs-patch/features/machete-creeping.md)), on all four machetes.

## Uncertain

- No footstep or noise value changes; the quieter approach is only the slower walk (inferred, as
  in Scubrah's page). `Machete_Modern` is not copied, so it cannot creep.
