---
title: LOD and draw distances
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#Geometry/**"
  - "engine/settings/defaultrenderconfig.xml#Terrain/quality[*]@TerrainDetail*"
  - "engine/settings/defaultrenderconfig.xml#RenderQuality/quality[*]@GeometryQuality"
exclude:
  - "**@UnSupportedPlatforms"
  - "**@*MinSizeShadowScale"
  - "**@MaxDecalCount*"
requires: [graphics-road-spline-lod]
verified: diff
---

# LOD and draw distances

Models, trees, small scattered objects and terrain keep their detailed versions much further away.
Very High and Ultra High geometry are pushed out by roughly ten times; the lower levels move one
step up. The Ultra High preset now selects the Very High geometry level.

## How

`engine/settings/defaultrenderconfig.xml`. For the `*LodScale` values lower means further.

`Geometry`, old -> new:

| Level | Changes |
|---|---|
| `low` | `MinZoomFactor` 0.60 -> 0.45 |
| `medium` | `KillLodScale` 1.25 -> 1.1, `LodScale` 1.2 -> 1.1, `ClustersLodScale` 1.25 -> 1.1, `LeavesRatio` 0.5 -> 1.0, `RealTreeCapsMaxDistance` 90 -> 100, `RealTreeLeafMinSize` 0.04 -> 0.02, `RealTreeHLeafMinSize` 0.04 -> 0.015, `RealTreeNodeMinSize` 0.005 -> 0.002, `ClusterObjectMinSize` 0.04 -> 0.02, `SceneObjectMinSize` 0.025 -> 0.01, `MinZoomFactor` 0.45 -> 0.35 |
| `high` | `KillLodScale` 1.1 -> 1.0, `LodScale` 1.1 -> 1.0, `ClustersLodScale` 1.1 -> 1.0, `MinZoomFactor` 0.35 -> 0.225 |
| `veryhigh` | `KillLodScale` 1.0 -> 0.7, `LodScale` 1.0 -> 0.1, `ClustersLodScale` 1.0 -> 0.1, `RealTreesLodScale` 1.0 -> 0.1, `TerrainLodScale` 1.0 -> 0.1, `RealTreeCapsMaxDistance` 100 -> 1000, `MinZoomFactor` 0.225 -> 0.10 |
| `ultrahigh` | `KillLodScale` 1.0 -> 0.7, `LodScale` 1.0 -> 0.1, `ClustersLodScale` 0.8 -> 0.1, `RealTreesLodScale` 1.0 -> 0.1, `TerrainLodScale` 1.0 -> 0.1, `RealTreeCapsMaxDistance` 100 -> 1000, `RealTreeLeafMinSize` 0.02, `RealTreeHLeafMinSize` 0.015, `RealTreeNodeMinSize` 0.002, `ClusterObjectMinSize` 0.02 and `SceneObjectMinSize` 0.01 all -> 0 |

Other values:

- `RenderQuality/quality[ultrahigh]` and `[ultrahighd3d10]`: `GeometryQuality` `ultrahigh` ->
  `veryhigh`. Choosing the Ultra High preset now gives the `veryhigh` row above. The `ultrahigh`
  row, with its zero minimum sizes, applies only when the geometry setting itself is set to Ultra
  High.
- `Terrain/quality[medium]`: `TerrainDetailViewDistance` 200 -> 256.
- `Terrain/quality[ultrahigh]`: `TerrainDetailBlendViewDistance` 64 -> 128. This is the "higher LOD
  blend distance" of terrain Ultra High.

The other edits in these sections are on other pages: the shadow size scales on `graphics-shadows`,
the decal counts on `graphics-decals`, `UnSupportedPlatforms` on `graphics-dx10-medium-levels`, and
`TerrainAffectedByMuzzleFlash` on `graphics-terrain-muzzle-flash`.

## Compared with Scubrah's Patch

`legacy/scubrahs-patch` `lod-distances` makes the same `low`, `medium` and `high` edits. This mod
goes further at `veryhigh` (`LodScale` 0.1 instead of untouched) and differs at `ultrahigh`
(`LodScale` 0.1 against 0.2, minimum sizes 0 against 0.0001). It leaves `AntiPortal`,
`TextureResolution` and the `DensityLodScale` entries alone.

## Depends on

`graphics-road-spline-lod`. With a low `LodScale`, roads switch to their low-detail version close to
the player unless their splines are given a longer LOD distance.

## Uncertain

- That the options menu's geometry setting can still select `ultrahigh` on its own is inferred from
  the preset layout; not checked in game.
