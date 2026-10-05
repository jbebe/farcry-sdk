---
title: M16 fires full auto and holds tighter on the sights
kind: component
bundle: weapons
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/m16{,/persistent}.xml#**/{iBurstLength,fAmplitude}"
exclude: []
requires: []
verified: diff
---

# M16 fires full auto and holds tighter on the sights

The M16 (AR-16) no longer fires three-round bursts, and its aimed spread shrinks.

## How

On `WeaponProperties.Primary.M16` and `.Persistent` in `generated/entitylibrarypatchoverride.fcb`:

- `FireStrategyProperties/iBurstLength` `3` -> `0`
- `FireStrategyProperties/FirstPerson/BulletSpread_IronSight/fAmplitude` `0.4` -> `0.34` and
  `BulletSpreadCrouch_IronSight/fAmplitude` `0.38` -> `0.32`

## Uncertain

- That `iBurstLength` `0` means fully automatic fire is read from the name; not traced.
