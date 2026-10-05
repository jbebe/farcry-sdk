---
title: More and longer-lasting decals
kind: component
bundle: improved-graphics
claims: []
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#Geometry/quality[*]@MaxDecalCount*"
  - "databases/generic/decal.xml#**"
exclude: []
requires: []
verified: diff
---

# More and longer-lasting decals

On Ultra High up to ten times as many decals (bullet holes, scorch marks) stay in the world at once,
and bullet holes in glass no longer fade. Not a line of the feature list; part of Improved Graphics' "and more" by the
analysis's reading.

## How

- `engine/settings/defaultrenderconfig.xml`, `Geometry/quality[ultrahigh]`: `MaxDecalCount`
  200 -> 2000, `MaxDecalCountPerType` 50 -> 1000.
- `databases/generic/decal.xml`, `Base.Glass` (`Generic[3]`): `fLifeTime` 20 -> 9999 seconds.
