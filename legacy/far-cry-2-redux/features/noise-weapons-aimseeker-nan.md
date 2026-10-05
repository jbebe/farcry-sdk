---
title: MK19 aim seeker speed re-encoded as NaN
kind: noise
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/**#**/AimSeeker/fAimSeekerAngularSpeed"
exclude: []
requires: []
verified: diff
---

# MK19 aim seeker speed re-encoded as NaN

`FireStrategyProperties/AimSeeker/fAimSeekerAngularSpeed` `3.39615E+38` -> `NaN` on
`WeaponProperties.Special.MK19`, `MountedWeapons.MK19_Mounted` and their two `.Multi` copies, the
mod's copies in `generated/entitylibrarypatchoverride.fcb`.

## How

The base value is near the largest float, a placeholder for "no limit"; the mod's export tool wrote
it back as NaN when round-tripping the library. No other field changes on these archetypes apart from
the iron-sight values of `weapons-ironsight-fov`.

## Uncertain

- How the engine treats a NaN turn speed for the grenade launcher's aim seeker (the player's mounted
  MK19 and the vehicle one) is not checked; a pick keeps the base value.
