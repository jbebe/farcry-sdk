---
title: Star .45 movement clips
kind: component
bundle: fixes
claims:
  - "Fixed Star .45 run animation"
status: located
systems: [weapons, graphics]
match:
  - "graphics/characters/_common/animations/weapons/secondary/star_model_p45_acp/*.mab"
exclude: []
requires: []
verified: diff
---

# Star .45 movement clips

The Star .45's first-person clips are swapped like the other pistols', which the readme (7-28-2020)
calls fixing its run animation.

## How

Four whole files in `graphics/characters/_common/animations/weapons/secondary/star_model_p45_acp/`,
each a byte-identical copy of a base-game clip:

| Replaced | Copy of |
|---|---|
| `1stge_uppb_aim2iron_+000fw_sep45_i1.mab` | the Star's `..._aim2ironcrh_...` |
| `1stge_uppb_aimcycle_+000fw_sep45_i1.mab` | the Star's `..._aimcyclecrh_...` |
| `1stge_uppb_walk_+000fw_sep45_i1.mab` | the Star's `..._walkcrh_...` |
| `1stge_uppb_walkcrh_+000fw_sep45_i1.mab` | the flare gun's `1stge_uppb_wsafewalk_+000fw_nowep_i1_eqflar.mab` |

## Depends on

Nothing. The other weapons are `weapons-lowered-carry-animations`.

## Uncertain

- None of the four is a run clip; which one the readme's "run animation" means (perhaps the walk
  clip, played at a run) is not known.
