---
title: Machete weapon-property copies
kind: shared
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/handtohand/machete*.xml"
exclude: []
requires: []
verified: diff
---

# Machete weapon-property copies

A shared piece: the mod copies `WeaponProperties.HandToHand.Machete`, `Machete_HomeMade`,
`Machete_Modern` and `Machete_Primitive` into `generated/entitylibrarypatchoverride.fcb`, which the
base game keeps only in the world libraries, so these copies are the ones the game reads.

## How

Four whole new units. Against the world libraries every copy now has:

| Field (`CWeaponProperties/...`) | Base game | Mod | Page |
|---|---|---|---|
| `FireStrategyProperties/fMaxAttackDistance` | `3` (Machete, HomeMade), `5` (Modern, Primitive) | `6` | `machete-range` |
| `FireStrategyProperties/fAttackFOV` | `45` | `55` | `machete-range` |
| `CommonProperties/IronSight/bCanIronsight` | `False` | `True` | `machete-creeping` |
| `CommonProperties/IronSight/fMoveSpeedFactor` | `1` | `0.5` | `machete-creeping` |
| `CommonProperties/bIsSilent` | `False` | `True` | probably `machete-stealth-kills` |

The variants' `disEntityId` also differ (`5462` -> `5504` and so on), which only identifies the
copy.
