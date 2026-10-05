---
title: Armory weapons do not come back for hours
kind: component
bundle: gameplay
claims: []
status: located
systems: [economy, weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/pickups/weapons/*/{weaponstorage,storageroom}.xml#**/fRespawnTime"
exclude: []
requires: []
verified: diff
---

# Armory weapons do not come back for hours

A gun taken from an armory stays gone for 9,999 seconds (about 2.8 hours) instead of reappearing at
once.

## How

`CPickupWeapon/fRespawnTime` `0.1` -> `9999` on the 21 `pickups.Weapons.<Weapon>_new.WeaponStorage`
and 7 `.StorageRoom` (`PKM`, `RPG7`, `SilencedMakarov_6P9`, `SPAS12`, `Star45`, `USAS12`, `Uzi`)
archetypes, the mod's copies in `generated/entitylibrarypatchoverride.fcb`.

## Depends on

Nothing. The DLC weapons' armory pickups get `0` instead (`economy-dlc-armory-respawn`). Realism Plus
makes the identical change to these 28 archetypes
([`economy-armory-respawn`](../../realism-plus/features/economy-armory-respawn.md)).

## Uncertain

- Whether placed entities override the archetype's value, and whether the timer runs while the
  player is elsewhere, are not checked. Not a line of the readme.
