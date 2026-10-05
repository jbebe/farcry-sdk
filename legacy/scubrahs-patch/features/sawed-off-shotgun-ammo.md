---
title: Sawed-off takes shotgun shells
kind: component
bundle: fixes
claims:
  - "The Sawed-Off Shotgun now uses shotgun ammo instead of pistol ammo"
status: located
systems: [weapons]
match:
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/weaponproperties/dlc1/sawedoffshotgun.xml#**/{ammoAmmoType,text_ammoAmmoType}"
exclude: []
requires: []
verified: diff
---

# Sawed-off takes shotgun shells

The DLC Sawed-Off Shotgun draws from the shotgun ammo pool instead of the pistols'.

## How

`downloadcontent/dlc1/generated/entitylibrary.fcb`, `WeaponProperties.DLC1.SawedOffShotgun`:
`CommonProperties/Ammo/ammoAmmoType` `6D6540FA` (`deserteagle`, the pistol pool) -> `EEAE53E1`
(`shotgun`), with its `text_ammoAmmoType` twin.

## Depends on

The gun's carry limits (`iMaxAmmo*` `71/47/47/35` -> `60/36/36/24`, the shotguns' numbers) change in
the same archetype and belong to `dlc-ammo-upgrades`; without them the sawed-off keeps pistol-sized
limits on the shared shotgun pool.
