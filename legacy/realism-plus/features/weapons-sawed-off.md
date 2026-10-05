---
title: Sawed-off takes shotgun shells and fires a double load
kind: component
bundle: weapons
status: located
systems: [weapons, economy]
match:
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/weaponproperties/dlc1/sawedoffshotgun.xml#**"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[shotgun_bandolier]/bonus[{+16,+17,+18,+19}]"
exclude:
  - "**#**/fIronsightFOV"
requires: []
verified: diff
---

# Sawed-off takes shotgun shells and fires a double load

The Fortune's Pack sawed-off shotgun draws from the shotgun ammo pool instead of the pistols', throws
twice the pellets per shot, reaches a little further and holds less ammo.

## How

`downloadcontent/dlc1/generated/entitylibrary.fcb`, `WeaponProperties.DLC1.SawedOffShotgun`:

- `CommonProperties/Ammo/ammoAmmoType` `6D6540FA` (`deserteagle`) -> `EEAE53E1` (`shotgun`), with its
  `text_ammoAmmoType` twin
- `iMaxAmmoCasual/Experimented/Hardcore/Infamous` `71/47/47/35` -> `40/25/25/20`
- `FireStrategyProperties/iBulletsShot` `7` -> `14`; the gun's `iAmmoInClip` stays `1`, so each
  trigger pull spends one shell
- spread: `fAngleYawBulletSpread` `3.5` -> `2`, `fAnglePitchBulletSpread` `3.5` -> `3`,
  `fSecondaryAngleYawBulletSpread` `4.5` -> `4`, `fSecondaryAnglePitchBulletSpread` `4.5` -> `6`
- `vectorEffectiveRange` `15/20` -> `20/25`, `vectorEffectiveRangeIS` `20/25` -> `25/30`
- `Stim_ImpactDamage/nLevel` `22` -> `20` per pellet
- `iClipsForSelfDestruct` `120` -> `144` (lasts a fifth longer before it breaks)

`gamemodesconfig.xml` `BonusService/Plan[shotgun_bandolier]` gains four `maxammo` bonuses for
`object="sawedoffshotgun"`, `35/17/10/8` by difficulty (`bonus[+16..+19]`).

## Depends on

Nothing. Scubrah's Patch moves the gun to shotgun shells the same way
([`sawed-off-shotgun-ammo`](../../scubrahs-patch/features/sawed-off-shotgun-ammo.md)) and gives it a
bandolier bonus with the other shotguns' numbers
([`dlc-ammo-upgrades`](../../scubrahs-patch/features/dlc-ammo-upgrades.md)). The mod also redraws
the sawed-off's HUD icon (`weapons-sawedoff-hud-icon`). Its iron-sight view is
`weapons-ironsight-fov`.

## Uncertain

- The paraphrased v2.1 list says the sawed-off "fires per barrel". The Final build doubles the
  pellets of a single shell rather than splitting the shot; how the gun's two-barrel animation
  relates to `iAmmoInClip` `1` is not traced.
