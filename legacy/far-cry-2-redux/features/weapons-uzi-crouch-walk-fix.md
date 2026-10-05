---
title: Uzi crouch-walk after aiming
kind: component
bundle: fixes
claims:
  - "Fixed Uzi visual glitch when crouch walking after aiming"
status: located
systems: [weapons, graphics]
match:
  - "graphics/characters/_common/animations/weapons/secondary/imi_uzi/*.mab"
exclude: []
requires: []
verified: diff
---

# Uzi crouch-walk after aiming

The Uzi's first-person clips follow the same lowered-carry scheme as the other weapons, which the
readme (8-10-2020) lists as the fix for a glitch when crouch-walking after aiming.

## How

Four whole files in `graphics/characters/_common/animations/weapons/secondary/imi_uzi/`, each a
byte-identical copy of another base-game Uzi clip:

| Replaced | Copy of |
|---|---|
| `1stge_uppb_aim2iron_+000fw_seuzi_i1.mab` | `..._aim2ironcrh_...` |
| `1stge_uppb_aimcycle_+000fw_seuzi_i1.mab` | `..._aimcyclecrh_...` |
| `1stge_uppb_walk_+000fw_seuzi_i1.mab` | `..._walkcrh_...` |
| `1stge_uppb_walkcrh_+000fw_seuzi_i1.mab` | `..._wsafewalk_...` |

## Depends on

Nothing. The same scheme for every other weapon is `weapons-lowered-carry-animations`; the glitch
presumably came from mixing swapped and unswapped clips on the Uzi.

## Uncertain

- Which of the four clips cures the glitch is not known; the readme names no file.
