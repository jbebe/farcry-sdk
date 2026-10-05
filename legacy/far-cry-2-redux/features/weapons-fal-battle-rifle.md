---
title: FAL hits harder and reaches further
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons, audio]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/fnfal{,/persistent}.xml#**"
exclude:
  - "**#**/{bAutoReload,fIronsightFOV,fIronsightTransitionTime,iAmmoInClip}"
requires: []
verified: diff
---

# FAL hits harder and reaches further

The FAL becomes a heavier battle rifle: more damage and range, stronger recoil, and the copy found
lying in the world fires single shots.

## How

The mod's copies in `generated/entitylibrarypatchoverride.fcb`.

`WeaponProperties.Primary.FNFAL` (the armory and enemy FAL):

- `CommonProperties/fRange` `300` -> `400`; `iClipsForSelfDestruct` `45` -> `35`
- `Recoil/fRecoilAchieveTime` `0.2` -> `0.3`, `fRecoilMax` `45` -> `50`
- `FireStrategyProperties/Stim_ImpactDamage/nLevel` `14` -> `18`, `fPhysImpulse` `28.5` -> `30`;
  `ImpactStims/Stims/Stim[0]/nLevel` `5` -> `8`

`WeaponProperties.Primary.FNFAL.Persistent` (the open-world FAL):

- `iClipsForSelfDestruct` `45` -> `35`
- `FireRate/iFireRate` `650` -> `370`, `selFireRateMode` `1` (full auto) -> `0` (single shot)
- `Stim_ImpactDamage/nLevel` `14` -> `16`; `MuzzleStims/Stims/Stim[0]/nLevel` `9` -> `10`;
  `ImpactStims/Stims/Stim[0]/nLevel` `5` -> `7`
- sounds follow the fire mode: `Sounds/sndSingleBulletShot` `0xFFFFFFFF` -> `0x00448DE2` and
  `sndtpSingleBulletShotType` `-1` -> `8`; the seven automatic-fire sound fields
  (`sndStartAutoBulletShot` `0x00448E61`, `sndStopAutoBulletShot` `0x004B291F`, their echoes, type and
  fade-outs) are removed; third person `sndSingleBulletShot` -> `0x0045533A` (type `9`),
  `sndStartAutoBulletShot`/`sndStopAutoBulletShot` -> `0xFFFFFFFF`, `sndtpAutoBulletShotType` `9` -> `-1`.

A weapon's `selFireRateMode` picks between its single-shot and automatic sound pair
([audio runtime](../../../docs/docs/engine-internals/audio-runtime.md)), hence the sound edits.

## Depends on

Nothing. The 20-round magazine is `weapons-magazine-sizes`; the shop's stat bars for the FAL
(`economy-shop-stat-bars`) show the higher damage.

## Uncertain

- The armory FAL (`Primary.FNFAL`) keeps full auto at 650 while the open-world one becomes
  semi-automatic; whether that split is intended is not known.
- `nLevel` values are stim levels, mapped to damage by the engine's curves; the on-screen effect is
  not measured. Not a line of the readme ("better ballistics" of the description).
