---
title: Shotgun tweaks
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/ithaca.xml#**/CommonProperties/{fRange,IronSight/fLookSensitivityFactor,IronSight/fIronsightPrepareDelay}"
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/weaponproperties/dlc1/{silencedshotgun,sawedoffshotgun}{,/multi}.xml#**/{iMaxAmmoInfamous,iBulletsShot,ammoAmmoType,text_ammoAmmoType}"
exclude: []
requires: []
verified: diff
---

# Shotgun tweaks

The Ithaca reaches further but takes longer to aim; the two DLC shotguns fire one more pellet,
carry less spare ammo on Infamous, and the sawed-off loads ordinary shotgun shells.

## How

- `WeaponProperties.Primary.Ithaca` (`generated/entitylibrarypatchoverride.fcb`): `fRange` `100` ->
  `140`; `IronSight/fIronsightPrepareDelay` `0.2` -> `0.5`; `IronSight/fLookSensitivityFactor`
  `0.31` -> `0.29`.
- `WeaponProperties.DLC1.SilencedShotgun` (`downloadcontent/dlc1/generated/entitylibrary.fcb`):
  `FireStrategyProperties/iBulletsShot` `7` -> `8`; `Ammo/iMaxAmmoInfamous` `42` -> `28`.
- `WeaponProperties.DLC1.SawedOffShotgun`: `iBulletsShot` `7` -> `8`; `iMaxAmmoInfamous` `35` -> `28`;
  `Ammo/ammoAmmoType` `6D6540FA` (`deserteagle`) -> `EEAE53E1` (`shotgun`), with the `text_` twin.
- The two `.Multi` copies change the same way (their Infamous ammo from `18` and `36`).

## Depends on

Nothing. The magazine sizes (SPAS-12 9, silenced shotgun 4, sawed-off 2) are
`weapons-magazine-sizes`. Realism Plus switches the sawed-off to shotgun ammo too
([`weapons-sawed-off`](../../realism-plus/features/weapons-sawed-off.md)).

## Uncertain

- Not a line of the readme.
