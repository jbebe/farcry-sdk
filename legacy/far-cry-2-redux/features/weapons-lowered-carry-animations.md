---
title: Weapons carried lower, crouched with the weapon down
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons, graphics]
match:
  - "graphics/characters/_common/animations/weapons/**/*.mab"
  - "graphics/characters/_common/animations/locomotion/stand/walk/**/*.mab"
exclude:
  - "graphics/characters/_common/animations/weapons/secondary/imi_uzi/*"
  - "graphics/characters/_common/animations/weapons/secondary/star_model_p45_acp/*"
requires: []
verified: diff
---

# Weapons carried lower, crouched with the weapon down

In first person the player holds every weapon lower when standing, walking and raising it to the
sights, and walks crouched with the weapon held down in the "safe zone" pose.

## How

105 first-person upper-body animations are replaced, each by a byte-identical copy of another base
game animation of the same weapon. This is the author's guide, "Alternate animations", options 1
and 2 together ([weapons](../../../docs/docs/modding/guide/weapons.md#guide---alternate-animations)):

- **Standing gets the crouched clip** (option 1, "carrying weapons lower"): `..._aim2iron_...` <-
  `..._aim2ironcrh_...`, `..._aimcycle_...` (or `aimingcycle`) <- `..._aimcyclecrh_...`,
  `..._aimoffset_±090..._` <- `..._aimoffsetcrh_...`, `..._walk_...` <- `..._walkcrh_...`.
- **Crouch-walking gets the safe-zone walk** (option 2): `..._walkcrh_...` <- `..._wsafewalk_...`.
  The Desert Eagle, 6P9, MAC-10 and Makarov take the flare gun's `wsafewalk` instead of their own.

Under `graphics/characters/_common/animations/weapons/`: the AK-47 (its crouch clips are named
`walkc`/`aimcyclec`), FAL, G3KA4, M16, MGL140, SPAS-12, USAS-12, Ithaca, AS50, Dragunov, dart rifle,
M1903, PKM, M249, RPG-7, Carl Gustaf, LPO-50, mortar, MP5, M79, flare gun, 6P9, Desert Eagle,
MAC-10, Makarov, and the DLC crossbow, sawed-off and silenced shotgun (`dlc1_` clips). The Ithaca's
and M1903's standing walks live in `locomotion/stand/walk/{ithaca37,m1903_a4}/` and are replaced
there. All whole files; take them from the archive's `patch.dat`.

## Depends on

Nothing. The Uzi's and Star .45's four clips each, done the same way, are on their own pages for the
readme's fixes (`weapons-uzi-crouch-walk-fix`, `weapons-star45-run-fix`). Eleven more copies the
mod adds under names nothing plays are `noise-weapons-unused-files`.

## Uncertain

- Not a line of the readme; the guide says this is "what Redux does". How each clip looks was not
  checked beyond the byte-identical source.
