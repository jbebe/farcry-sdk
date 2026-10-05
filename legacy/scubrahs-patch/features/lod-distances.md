---
title: LOD and draw distances
kind: component
bundle: visuals
claims:
  - "Increased LOD distances (Ultra High)"
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#Geometry/**"
  - "engine/settings/defaultrenderconfig.xml#Terrain/**"
  - "engine/settings/defaultrenderconfig.xml#AntiPortal/**"
  - "engine/settings/defaultrenderconfig.xml#TextureResolution/**"
  - "worlds/*/generated/world*.game.xml/_environment.xml#FakeTerrain@{Radius,Tesselation}"
exclude:
  - "**@UnSupportedPlatforms"
  - "**@*MinSizeShadowScale"
  - "**@MaxDecalCount*"
requires: [road-texture-lod]
verified: diff
---

# LOD and draw distances

Models, trees, small scattered objects and terrain keep their detailed versions much further away,
mostly on Ultra High; the lower levels are nudged one step up.

## How

`engine/settings/defaultrenderconfig.xml`. For the `*LodScale` values lower means further. The
Ultra High preset uses `Geometry`, `Terrain` and `TextureResolution` `ultrahigh`; High uses
`Geometry` `medium`.

`Geometry`, old -> new:

| Level | Changes |
|---|---|
| `low` | `MinZoomFactor` 0.60 -> 0.45 |
| `medium` | `KillLodScale` 1.25 -> 1.1, `LodScale` 1.2 -> 1.1, `ClustersLodScale` 1.25 -> 1.1, `LeavesRatio` 0.5 -> 1.0, `RealTreeCapsMaxDistance` 90 -> 100, `RealTreeLeafMinSize` 0.04 -> 0.02, `RealTreeHLeafMinSize` 0.04 -> 0.015, `RealTreeNodeMinSize` 0.005 -> 0.002, `ClusterObjectMinSize` 0.04 -> 0.02, `SceneObjectMinSize` 0.025 -> 0.01, `MinZoomFactor` 0.45 -> 0.35 |
| `high` | `KillLodScale` 1.1 -> 1.0, `LodScale` 1.1 -> 1.0, `ClustersLodScale` 1.1 -> 1.0, `MinZoomFactor` 0.35 -> 0.225 |
| `veryhigh` | `ClustersLodScale` 1.0 -> 0.8, `MinZoomFactor` 0.225 -> 0.10 |
| `ultrahigh` | `KillLodScale` 1.0 -> 0.9, `LodScale` 1.0 -> 0.2, `ClustersLodScale` 0.8 -> 0.1, `RealTreesLodScale` 1.0 -> 0.1, `TerrainLodScale` 1.0 -> 0.1, `RealTreeCapsMaxDistance` 100 -> 100000, `RealTreeLeafMinSize`, `RealTreeHLeafMinSize`, `RealTreeNodeMinSize`, `ClusterObjectMinSize` and `SceneObjectMinSize` (0.02, 0.015, 0.002, 0.02, 0.01) all -> 0.0001, `SceneObjectDepthPassMinSize` unset -> 0.0001, `RealtreeReflectionMinLODLeaf` 3 -> 4, `DensityLodScale` for `Logic.LightJungle`, `Logic.Savannah` and `Logic.Woodland` 1.0 -> 0.5 |

Other sections:

- `Terrain/quality[ultrahigh]`: `TerrainLodScale` 1.0 -> 0.1, `TerrainDetailViewDistance` 512 -> 4096,
  `TerrainDetailBlendViewDistance` 64 -> 4096.
- `AntiPortal/quality[high]` (the only level): `AntiPortalKillDistanceScale` 2.0 -> 10.0.
- `TextureResolution/quality[ultrahigh]`: `AlwaysMip0Loading` 0 -> 1, so textures load their
  full-resolution mip.

The `Geometry` edits that are not here: the shadow size scales are `small-object-shadows`, the
decal counts `graphics-decals`, `UnSupportedPlatforms` `dx10-compatibility`.

## Depends on

`road-texture-lod`. A low `LodScale` shortens the distance at which roads switch to their low
detail, and roads look broken at Ultra High's 0.2 without that page.

## Uncertain

- What `AntiPortalKillDistanceScale` does is not traced; by name it scales the distance up to which
  occluders are used.
