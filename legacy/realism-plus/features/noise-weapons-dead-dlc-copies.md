---
title: Dead copies of the DLC weapons in the override library
kind: noise
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/dlc1/**"
exclude: []
requires: []
verified: diff
---

# Dead copies of the DLC weapons in the override library

194 edits that do nothing: the game reads the DLC library after the override library, so its own
declarations of these three archetypes win.

## How

The mod's `generated/entitylibrarypatchoverride.fcb` declares `WeaponProperties.DLC1.Crossbow`,
`DLC1.SawedOffShotgun` and `DLC1.SilencedShotgun` again. These copies are almost empty templates, not
tuned weapons: compared with `downloadcontent/dlc1/generated/entitylibrary.fcb`, their pickups,
holster handles, crosshair areas, sounds, particles, shell and impact effects are blank
(`FFFFFFFF`), shooting angles and spread are `0`, damage `nLevel` is `0`, `iBulletsShot` is `1`,
`bIsSilent` is `False`, the shotguns use `assaultrifle` ammo with `iMaxAmmo*` `21`, and their impact
and muzzle stims, range multipliers and secondary damage are removed. `legacy changes` marks every one
`(dead: the game reads downloadcontent/dlc1/generated/entitylibrary.fcb)`.

The mod's real DLC weapon changes are made in the DLC library itself: `weapons-shotguns` (silenced
shotgun), `weapons-sawed-off`, `weapons-crossbow`, `weapons-ironsight-fov`.

## Uncertain

- If the DLC library were missing (no Fortune's Pack content), these stubs would become the live
  weapons and break them (inference).
