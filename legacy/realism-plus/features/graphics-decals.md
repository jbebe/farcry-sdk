---
title: More and longer-lasting decals
kind: component
bundle: graphics
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

Up to eight (Very High) or ten (Ultra High geometry) times as many decals - bullet holes, scorch
marks - stay in the world at once, and bullet holes in glass no longer fade.

## How

- `engine/settings/defaultrenderconfig.xml`, `Geometry`:
  - `quality[veryhigh]`: `MaxDecalCount` 200 -> 1600, `MaxDecalCountPerType` 50 -> 800;
  - `quality[ultrahigh]`: `MaxDecalCount` 200 -> 2000, `MaxDecalCountPerType` 50 -> 1000.
- `databases/generic/decal.xml`, `Generic[3]` (`Base.Glass` in the base game's file): `fLifeTime`
  20 -> 9999 seconds.

The Ultra High preset selects the `veryhigh` geometry level in this mod (`graphics-lod-distances`),
so with the preset it is the 1600 / 800 pair that applies.

The ultrahigh values and the glass lifetime are the same as `graphics-decals` in
`legacy/scubrahs-patch`; the veryhigh row is this mod's own.

## Uncertain

- The mod's notes speak of decals lasting "until you leave the area". Only the glass decal's
  lifetime changes; for the rest that reading rests on the higher counts, which make old decals
  wait longer before they are recycled (inference).
