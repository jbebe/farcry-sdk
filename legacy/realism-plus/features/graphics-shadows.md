---
title: Shadow range, resolution and foliage shadows
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#Shadow/quality[*]@*"
  - "engine/settings/defaultrenderconfig.xml#Ambient/quality[*]@*"
  - "engine/settings/defaultrenderconfig.xml#Geometry/quality[*]@*MinSizeShadowScale"
exclude:
  - "**@UnSupportedPlatforms"
requires: []
verified: diff
---

# Shadow range, resolution and foliage shadows

Sun shadows get twice the resolution at every level, their near cascades reach further (the sharp
band now ends about 20 m out instead of 8-14 m), trees and leaves cast their full share of shadows,
and smaller objects cast shadows at the lower levels. The static ambient shadow map reaches further
on Medium and High.

## How

`engine/settings/defaultrenderconfig.xml`. The Ultra High preset uses `Shadow` `ultrahigh` and
`Ambient` `high`; Very High uses `Shadow` `veryhigh` and `Ambient` `high`; High and Medium use
`Shadow` `medium` (High with `Ambient` `high`); the Low preset turns shadows off, so the `low` row
applies only when the shadow setting itself is Low.

`Shadow` (dynamic sun shadows), old -> new:

| Level | Changes |
|---|---|
| `low` | `SunShadowRange0` 3 -> 6, `SunShadowRange1` 7 -> 20, `SunShadowRange2` 13 -> 40, `CascadedShadowMapSize` 341 -> 682, `LeavesShadowRatio` 0.25 -> 1 |
| `medium` | `SunShadowRange0` 4 -> 6, `SunShadowRange1` 8 -> 20, `SunShadowRange2` 20 -> 80, `CascadedShadowMapSize` 680 -> 1360, `LeavesShadowRatio` 0.35 -> 1 |
| `high` | `SunShadowFadeRange` 6 -> 10, `SunShadowRange0` 4 -> 6, `SunShadowRange1` 14 -> 20, `CascadedShadowMapSize` 1364 -> 2728, `LeavesShadowRatio` 0.5 -> 1 |
| `veryhigh` | `SunShadowFadeRange` 6 -> 10, `SunShadowRange0` 4 -> 6, `SunShadowRange1` 14 -> 20, `SunShadowRange2` 80 -> 135, `CascadedShadowMapSize` 1364 -> 2728, `RainShadowMapSize` 256 -> 512, `LeavesShadowRatio` 0.5 -> 1 |
| `ultrahigh` | `SunShadowRange0` 4 -> 6, `SunShadowRange2` 140 -> 135, `CascadedShadowMapSize` 2048 -> 4096, `LeavesShadowRatio` 0.5 -> 1 |

`Ambient` (the static sector shadow and hemisphere maps), old -> new:

| Level | Changes |
|---|---|
| `low` | `ShadowMapSize` 256 -> 512, `SectorTextureSize` 64 -> 128 |
| `medium` | `MaxHemiMapDistance` 128 -> 160, `SectorCountX` and `SectorCountY` 8 -> 12 |
| `high` | `MaxHemiMapDistance` 160 -> 512 |

`Geometry`, the minimum size an object must have on screen to cast a shadow, relative to its draw
threshold (lower means smaller objects cast shadows):

| Level | `RealTreeMinSizeShadowScale` | `ClusterObjectMinSizeShadowScale` | `SceneObjectMinSizeShadowScale` |
|---|---|---|---|
| `low` | 3.0 -> 2.0 | 3.0 -> 2.0 | 3.0 -> 2.0 |
| `medium` | 2.0 -> 1.25 | 2.0 -> 1.50 | 2.0 -> 1.50 |
| `high` | 1.25 -> 1.0 | 1.50 -> 1.25 | 1.50 -> 1.25 |

Read against the mod's notes: `LeavesShadowRatio` -> 1 is "trees and vegetation cast more
shadows"; the pushed-out `SunShadowRange0`/`SunShadowRange1` move the "~10 m shadow quality line"
(the edge of the sharpest cascade); `SunShadowFadeRange` 6 -> 10 widens the blend between cascades,
plausibly the "softer shadows"; `Ambient` `high` `MaxHemiMapDistance` 512 is "ambient High: further
static shadow distance". `SoftShadows` itself is not touched.

## Compared with Scubrah's Patch

`legacy/scubrahs-patch` `shadow-quality` and `small-object-shadows` touch the same keys with other
numbers (for example `ShadowMapSize` rather than only the cascaded size, `LeavesShadowRatio` 1.0 only
at the top two levels, `MaxHemiMapDistance` 320 rather than 512). The `Geometry` shadow-scale values
here match `small-object-shadows` exactly.

## Uncertain

- What "softer shadows" in the mod's notes maps to is an inference; no softness key changes.
