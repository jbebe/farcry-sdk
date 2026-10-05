---
title: M79 walks slower on the sights
kind: component
bundle: weapons
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/secondary/m79{,/mikes_rusty}.xml#**/fMoveSpeedFactor"
exclude: []
requires: []
verified: diff
---

# M79 walks slower on the sights

Aiming the M79 grenade launcher slows the player like other aimed weapons.

## How

`CommonProperties/IronSight/fMoveSpeedFactor` `1` -> `0.5` on `WeaponProperties.Secondary.M79` and
`.Mikes_Rusty`, the mod's copies in `generated/entitylibrarypatchoverride.fcb`. Scubrah's Patch fixes
the same oversight the same way, on the base copy only
([`m79-ironsight-speed`](../../scubrahs-patch/features/m79-ironsight-speed.md)).
