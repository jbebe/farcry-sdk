---
title: M79 walks slower on the sights
kind: component
bundle: fixes
claims:
  - "Fixed an oversight with the M79's walking speed while using iron sights"
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/secondary/m79.xml{,#**}"
exclude: []
requires: []
verified: diff
---

# M79 walks slower on the sights

Aiming the M79 grenade launcher slows the player like other aimed weapons.

## How

`generated/entitylibrarypatchoverride.fcb` gains a copy of `WeaponProperties.Secondary.M79`, which
the base game keeps only in the world libraries; its one difference is
`CommonProperties/IronSight/fMoveSpeedFactor` `1` -> `0.5`, the value 51 of the 71 single-player
weapon properties in the base game's `world1` library use.
