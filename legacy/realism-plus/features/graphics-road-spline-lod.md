---
title: Road splines keep full detail
kind: component
bundle: graphics
status: located
systems: [graphics, world]
match:
  - "levels/*/generated/worldsectors/landmarknear*.data.fcb/**#Components/CSplinePrimitiveComponent/{DistanceMax,LOD0Distance}"
exclude: []
requires: []
verified: diff
---

# Road splines keep full detail

Roads, tracks and river splines keep their full-detail version out to 1000 m and are never culled by
distance, so the low `LodScale` of `graphics-lod-distances` does not make them pop to their
low-detail look close to the player.

## How

3,952 spline primitives in 1,575 `landmarknear<id>.data.fcb` sector files, across all 20 campaign
levels of both worlds, `CSplinePrimitiveComponent`:

- `DistanceMax` 300 -> -1 (3,952 splines);
- `LOD0Distance` 92 -> 1000 (3,791 splines) and 96 -> 1000 (161).

This is the same edit, spline for spline and value for value, as `road-texture-lod` in
`legacy/scubrahs-patch`.

## Uncertain

- That -1 means "no maximum distance" is inferred from the value.
