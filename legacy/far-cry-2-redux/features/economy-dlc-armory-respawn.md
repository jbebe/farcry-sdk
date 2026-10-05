---
title: DLC weapons do not respawn in the armory
kind: component
bundle: fixes
claims:
  - "DLC weapons no longer respawn while you're in the armory"
status: located
systems: [economy, weapons]
match:
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/dlc1weapons/dlc1/pickup_*.xml#**/fRespawnTime"
exclude: []
requires: []
verified: diff
---

# DLC weapons do not respawn in the armory

The crossbow, sawed-off and silenced shotgun in the armories no longer reappear a tenth of a second
after being taken, so they cannot be picked up again and again.

## How

`CPickupWeapon/fRespawnTime` `0.1` -> `0` on `DLC1Weapons.DLC1.Pickup_Crossbow`,
`Pickup_SawedOffShotgun` and `Pickup_SilencedShotgun` in
`downloadcontent/dlc1/generated/entitylibrary.fcb`.

## Depends on

Nothing. The base weapons' armory pickups get `9999` instead (`economy-armory-respawn`); Realism
Plus gives the DLC pickups `9999` too
([`economy-armory-respawn`](../../realism-plus/features/economy-armory-respawn.md)).

## Uncertain

- That `0` disables the respawn (rather than making it immediate) follows from the readme's line and
  from Scubrah's Patch using `0` with a script-driven cooldown
  ([`armory-respawn-cooldown`](../../scubrahs-patch/features/armory-respawn-cooldown.md)); not traced.
