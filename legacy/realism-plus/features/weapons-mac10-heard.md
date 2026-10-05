---
title: MAC-10 is heard
kind: component
bundle: weapons
status: located
systems: [weapons, ai]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/secondary/mac10{,/mikes_rusty}.xml#**/MuzzleStims/**"
exclude: []
requires: []
verified: diff
---

# MAC-10 is heard

Enemies hear the MAC-10 like any other unsuppressed gun; in the base game it is as quiet as a
silenced pistol.

## How

`FireStrategyProperties/MuzzleStims/Stims/Stim[0]/fRadius` `3` -> `75` on
`WeaponProperties.Secondary.MAC10` and `.Mikes_Rusty`, the mod's copies in
`generated/entitylibrarypatchoverride.fcb`. Scubrah's Patch makes the same fix the same way
([`mac10-silent-radius`](../../scubrahs-patch/features/mac10-silent-radius.md)), on the base copy only.
