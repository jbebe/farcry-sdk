---
title: MAC-10 is heard
kind: component
bundle: fixes
claims:
  - "Fixed the MAC-10 silent radius bug"
status: located
systems: [weapons, ai]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/secondary/mac10.xml{,#**}"
exclude: []
requires: []
verified: diff
---

# MAC-10 is heard

Enemies now hear the MAC-10 like any other unsuppressed SMG; in the base game it was as quiet as a
silenced pistol.

## How

`generated/entitylibrarypatchoverride.fcb` gains a copy of `WeaponProperties.Secondary.MAC10`, which
the base game keeps only in the world libraries; its one difference is
`CWeaponProperties/FireStrategyProperties/MuzzleStims/Stims/Stim[0]/fRadius` `3` -> `75`. For
comparison the base game's Uzi, AK-47 and Star .45 use `75`, the silenced Makarov `2.5`.
