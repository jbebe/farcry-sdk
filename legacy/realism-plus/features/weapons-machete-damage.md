---
title: Machete hits harder
kind: component
bundle: gameplay
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/handtohand/*.xml#**/Stim_ImpactDamage/nLevel"
exclude: []
requires: []
verified: diff
---

# Machete hits harder

A machete blow does about half as much damage again, which the paraphrased v2.1 list calls one-hit
kills.

## How

`FireStrategyProperties/Stim_ImpactDamage/nLevel` `24` -> `35` on `WeaponProperties.HandToHand.Machete`,
`Machete_HomeMade` and `Machete_Primitive`, the mod's copies in
`generated/entitylibrarypatchoverride.fcb`. The fourth machete, `Machete_Modern`, is not copied and
keeps `24`.

## Depends on

Nothing. The guaranteed kill from behind is `weapons-machete-silent-kills`.

## Uncertain

- Whether `35` kills in one hit depends on the stim-to-damage curve and the target's health; not
  checked.
