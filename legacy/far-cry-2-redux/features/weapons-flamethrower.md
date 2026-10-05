---
title: Flamethrower reaches further
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/lpo50{,/persistent}.xml#**"
exclude:
  - "**#**/{bAutoReload,fIronsightFOV,fIronsightTransitionTime}"
requires: []
verified: diff
---

# Flamethrower reaches further

The player's LPO-50 throws a longer, faster, denser flame.

## How

The mod's copies in `generated/entitylibrarypatchoverride.fcb`.

`WeaponProperties.Special.LPO50` (the armory flamethrower):

- `CommonProperties/fRange` `100` -> `200`; `vectorEffectiveRange` `15/20` -> `26/26`;
  `vectorEffectiveRangeIS` `20/25` -> `28/28`
- `FireStrategyProperties/FlameMesh`: `fSize` `10` -> `30`, `fSpeed` `15` -> `35`, `fSegmentLength`
  `0.8` -> `0.6`, `fRingStartAngle` `0` -> `16`
- `Collision/fCollisionDivisions` `8` -> `16`, `fCollisionOffset` `0.1` -> `0.08`
- `AIShootingSystem`: `archTargetDistanceCurve` and `archSuccessfulHitCurve` set (empty ->
  `Curves.ShootingSystem.DistanceAccuracy`, `Curves.ShootingSystem.SuccessfulHit`), bursts of
  `94`-`99` s with no wait between them (from `0.3`-`1.2` s bursts and waits)

`WeaponProperties.Special.LPO50.Persistent` (the open-world one): `fRange` `100` -> `80`;
`iClipsForSelfDestruct` `6` -> `2`; `FlameMesh/fSize` `10` -> `30`; `restitutionInterpolation`
`fValueA` `20` -> `40`, `fValueB` `15` -> `60`, `fParamB` `3` -> `8`.

## Depends on

Nothing. The enemies' flamethrower is another archetype, `LPO50.Multi`
(`weapons-enemy-flamethrower`). Realism Plus triples `fSize` the same way and doubles `fSpeed`
([`weapons-flamethrower`](../../realism-plus/features/weapons-flamethrower.md)).

## Uncertain

- The `AIShootingSystem` values only matter for an AI holding this archetype; no pack or library
  archetype gives it to one (enemies get `LPO50.Multi`), so they likely do nothing.
- `fSize` as the flame's reach follows the guide
  ([weapons](../../../docs/docs/modding/guide/weapons.md#flamethrower-range)); not traced. The
  open-world copy wearing out after 2 tanks instead of 6 may be a slip. Not a line of the readme.
