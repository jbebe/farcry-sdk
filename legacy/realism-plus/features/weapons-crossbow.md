---
title: Crossbow bolts fly faster and drop in an arc
kind: component
bundle: weapons
status: located
systems: [weapons, economy]
match:
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/weaponproperties/dlc1/crossbow.xml#**"
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/dlc1weapons/dlc1/arrow.xml#**"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[rocketeer_satchel]/bonus[{+12,+13,+14,+15}]"
exclude: []
requires: []
verified: diff
---

# Crossbow bolts fly faster and drop in an arc

The Fortune's Pack explosive crossbow's bolts leave at twice the speed but start falling almost at
once and fall harder, so they fly in an arc. The rocketeer satchel now carries extra bolts.

## How

`downloadcontent/dlc1/generated/entitylibrary.fcb`:

- `DLC1Weapons.DLC1.Arrow`, `CRocket/Stages`: `Fire/fSpeed` `35` -> `70`, `Fall/fTime` `3` -> `0.2`,
  `Fall/fGravity` `-2` -> `-5`.
- `WeaponProperties.DLC1.Crossbow`: `iMaxAmmoCasual` `7` -> `6`, `iMaxAmmoExperimented` `4` -> `3`
  (Hardcore and Infamous unchanged); `iClipsForSelfDestruct` `30` -> `36`.

`gamemodesconfig.xml` `BonusService/Plan[rocketeer_satchel]` gains four `maxammo` bonuses for
`object="crossbow"`, `4/3/2/1` by difficulty (`bonus[+12..+15]`); with them a full satchel carries
`10/6` bolts on Casual/Experienced against the vanilla `7/4`.

## Depends on

Nothing. The guide documents both projectile fields
([weapons](../../../docs/docs/modding/guide/weapons.md#projectiles---rockets-and-explosive-bolts)).
Scubrah's Patch adds a crossbow satchel bonus of `3/2/1/1`
([`dlc-ammo-upgrades`](../../scubrahs-patch/features/dlc-ammo-upgrades.md)).

## Uncertain

- Reading `Fall/fTime` as the delay before gravity takes over is the guide's; not traced.
