---
title: Road textures at high LOD scales
kind: component
bundle: fixes
claims:
  - "Fixed an issue with road textures where they looked abnormal with a higher LOD scale setting"
status: located
systems: [graphics, world]
match:
  - "levels/*/generated/worldsectors/landmarknear*.data.fcb/**#Components/CSplinePrimitiveComponent/{DistanceMax,LOD0Distance}"
exclude: []
requires: []
verified: diff
---

# Road textures at high LOD scales

Roads, tracks and river splines keep their full-detail version out to 1000 m and are never culled by
distance, so a low `LodScale` (as `lod-distances` sets for Ultra High) no longer makes them pop to
their low-detail look close to the player.

## How

3,952 spline primitives in the `landmarknear<id>.data.fcb` sector files (1,991 of them, in 20
levels of both worlds), `CSplinePrimitiveComponent`:

- `DistanceMax` 300 -> -1 (3,952 splines);
- `LOD0Distance` 92 -> 1000 (3,791 splines) and 96 -> 1000 (161).

`LOD0Distance` is the spline's own value from `spline_inventory.xml`, baked into each placement.

## Uncertain

- That -1 means "no maximum distance" is inferred from the value.
