---
title: Armory weapons do not come back for hours
kind: component
status: located
systems: [economy, weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/pickups/weapons/*/{weaponstorage,storageroom}.xml#**/fRespawnTime"
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/dlc1weapons/dlc1/pickup_*.xml#**/fRespawnTime"
exclude: []
requires: []
verified: diff
---

# Armory weapons do not come back for hours

A gun taken from an armory stays gone for 9,999 seconds (about 2.8 hours) instead of reappearing at
once.

## How

`CPickupWeapon/fRespawnTime` `0.1` -> `9999` on 31 pickup archetypes:

- the 21 `pickups.Weapons.<Weapon>_new.WeaponStorage` and 7 `.StorageRoom` (`PKM`, `RPG7`,
  `SilencedMakarov_6P9`, `SPAS12`, `Star45`, `USAS12`, `Uzi`) archetypes, the mod's copies in
  `generated/entitylibrarypatchoverride.fcb`;
- `DLC1Weapons.DLC1.Pickup_Crossbow`, `Pickup_SawedOffShotgun` and `Pickup_SilencedShotgun` in
  `downloadcontent/dlc1/generated/entitylibrary.fcb`.

## Depends on

Nothing. Scubrah's Patch sets the same field to `0` and drives the cooldown from a script instead
([`armory-respawn-cooldown`](../../scubrahs-patch/features/armory-respawn-cooldown.md)).

## Uncertain

- Whether placed entities override the archetype's `fRespawnTime`, and whether the timer runs while
  the player is elsewhere, are not checked. The published list does not mention this.
