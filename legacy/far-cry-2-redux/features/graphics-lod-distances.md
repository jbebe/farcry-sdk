---
title: Ultra High draw distances
kind: component
bundle: graphics
claims: []
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#{Geometry,Terrain}/quality[ultrahigh]@*"
exclude:
  - "**@MaxDecalCount*"
requires: []
verified: diff
---

# Ultra High draw distances

On Ultra High geometry and terrain, trees and object clusters keep their detailed versions at any
distance, detailed objects are culled later, and the terrain's detail texture reaches sixteen times
as far. Not a line of the readme.

## How

`engine/settings/defaultrenderconfig.xml`, used by the Ultra High presets (`ultrahigh` and
`ultrahighd3d10` select both levels). For the `*LodScale` values lower means further:

- `Geometry/quality[ultrahigh]`: `KillLodScale` 1.0 -> 0.7, `ClustersLodScale` 0.8 -> 0.0,
  `RealTreesLodScale` 1.0 -> 0.0. `LodScale` itself is unchanged.
- `Terrain/quality[ultrahigh]`: `TerrainDetailViewDistance` 512 -> 8192,
  `TerrainDetailBlendViewDistance` 64 -> 1024.

The same section's decal counts are `graphics-decals`.

## Compared with other mods

`legacy/scubrahs-patch` `lod-distances` and `legacy/realism-plus` `graphics-lod-distances` also
lower the ultrahigh scales (to 0.2 or 0.1, `KillLodScale` 0.7 in Realism Plus) and touch the lower
levels too; this mod changes only ultrahigh, and goes to zero.

## Uncertain

- What a scale of exactly 0 does (no LOD switch at all, or a degenerate distance) is not traced;
  the cost of never dropping tree and cluster detail is not measured.
