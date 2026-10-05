---
title: Faster, flatter crossbow bolts
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons]
match:
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/dlc1weapons/dlc1/arrow.xml#**"
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/weaponproperties/dlc1/crossbow.xml#**/iClipsForSelfDestruct"
exclude: []
requires: []
verified: diff
---

# Faster, flatter crossbow bolts

Crossbow bolts fly faster and drop sooner and harder instead of gliding, and the crossbow wears out
a little sooner.

## How

`downloadcontent/dlc1/generated/entitylibrary.fcb`:

- `DLC1Weapons.DLC1.Arrow`, `CRocket/Stages`: `Fire/fSpeed` `35` -> `45`; `Fall/fTime` `3` -> `0.5`;
  `Fall/fGravity` `-2` -> `-5`.
- `WeaponProperties.DLC1.Crossbow`: `CommonProperties/iClipsForSelfDestruct` `30` -> `25`.

## Depends on

Nothing. The crossbow's scope view is `weapons-scope-fov`; its ammo upgrade (three more bolts with
the grenadier webbing) is `economy-ammo-upgrades`. Realism Plus changes the same three bolt values
further (`70`, `0.2`, `-5`, [`weapons-crossbow`](../../realism-plus/features/weapons-crossbow.md)).

## Uncertain

- The stage semantics (speed in m/s, the fall stage's duration) follow the guide's rocket section
  ([weapons](../../../docs/docs/modding/guide/weapons.md#projectiles---rockets-and-explosive-bolts));
  not traced. Not a line of the readme.
