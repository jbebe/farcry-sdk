---
title: Enemy flamethrower that never runs dry
kind: component
bundle: gameplay
claims:
  - "Enemy flamethrowers have more ammo now"
status: located
systems: [weapons, ai]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/lpo50/multi.xml#Entity/Components/**"
exclude: []
requires: []
verified: diff
---

# Enemy flamethrower that never runs dry

The flamethrower the mod gives shotgun mercenaries burns without consuming fuel, fires in very long
bursts, cannot jam or break, and throws a long, wide sheet of fire.

## How

`WeaponProperties.Special.LPO50.Multi` (in the base game's override library, the multiplayer
flamethrower) is retuned in the mod's `generated/entitylibrarypatchoverride.fcb` (103 changes):

- **Fuel.** `FireStrategyProperties/fConsumeAmmoRate` `0.1` -> `0`, so firing never drains the tank;
  `NoFuel/fRetriggerDelay` `1` -> `0`. `Ammo/iAmmoInClip` `200` -> `7` and `iMaxAmmo*` `300` -> `1`
  then do not matter while firing.
- **Bursts.** `AIShootingSystem/fBurstLength*` -> `999` s, `fBurstWait*` -> `0`;
  `archSuccessfulHitCurve` -> `Curves.ShootingSystem.SuccessfulHit_Mounted_M249`;
  `AICurve/archSecondaryBulletChanceOfSuccessCurve` -> `Curves.AIWeapon.ShotgunSecondaryBulletHitChance`;
  `FireRate/iFireRate` `600` -> `9998`.
- **Reliability.** `bIsIndestructible` `True`, `bIsBreakable` `False`, `selJamType` `2` -> `0`,
  `fUnjamTime` `0`, `iClipsForSelfDestruct` `0`, `Malfunction/fTriggerPulls` `0`,
  `bSingleHitHealthFailure` `False`, hit-location severities `0`; `Reliability/MuzzleDirtStim/nLevel`
  `0` -> `8`.
- **Flame.** `FlameMesh/fSize` `6` -> `86`, `fSpeed` `15` -> `56`, gravity, segment, growth and
  restitution interpolations retuned; the three `FireballEffects` spawn every `0.001` s at speed `56`
  (the third now sends stims and draws `weapons.flamthrower.fireball`); `Collision` particle scales
  flattened to `1`; `Damage/Radius` `1` -> `6.9`, `Distance` `50` -> `79`, `Level` `10` -> `2`,
  `SendStimTime` `0.5` -> `0`; a `Stim_ImpactDamageSecondary` block is added.
- **Handling.** `fRange` `100` -> `47`; effective ranges `30` -> `31`/`35`; `fMoveSpeedFactor` `1` ->
  `1.3`; recoil zeroed; `IronSight/fIronsightFOV` `1.30833` -> `0.6`, transition `0`;
  `archPickupArchetype` `pickups.Weapons.LPO50_new.Multi.Dropped` -> `pickups.Weapons.LPO50_new.Persistent`
  (a dead carrier drops a single-player flamethrower).

## Depends on

- `weapons-enemy-loadouts`: the `shotgun` pack hands `weapons.Special.LPO50.Multi` out with
  probability `0.05`; nothing else in single player uses it.
- Realism Plus retunes the same archetype for the same purpose, differently (fuel kept, `fSize` `30`,
  [`weapons-enemy-loadouts`](../../realism-plus/features/weapons-enemy-loadouts.md)).

## Uncertain

- `fConsumeAmmoRate` `0` as "infinite fuel" is inferred from the name and the readme's line. Its id
  renumbering (`disEntityId`) is on `noise-weapons-multiplayer`.
