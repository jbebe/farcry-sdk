---
title: Shadow resolution and range
kind: component
bundle: visuals
claims:
  - "Increased shadow resolution & range (Ultra High)"
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#Shadow/quality[*]@*"
  - "engine/settings/defaultrenderconfig.xml#Ambient/quality[*]@*"
  - "worlds/*/generated/world*.game.xml/_environment.xml#Shadow@DynamicShadowRadius"
exclude:
  - "**@UnSupportedPlatforms"
requires: []
verified: diff
---

# Shadow resolution and range

Sharper sun shadows that reach further, and a larger, further-reaching static ambient shadow map.
The claim names Ultra High, but every level except `high` is retuned.

## How

`engine/settings/defaultrenderconfig.xml`. The Ultra High preset uses `Shadow` `ultrahigh` and
`Ambient` `high`; Very High uses `Shadow` `veryhigh` and `Ambient` `high`; High uses `Shadow` `medium`.

`Shadow` (dynamic sun shadows), old -> new:

| Level | Changes |
|---|---|
| `low` | `SunShadowFadeRange` 1 -> 2, `SunShadowRange0` 3 -> 4, `SunShadowRange1` 7 -> 8, `SunShadowRange2` 13 -> 20, `ShadowMapSize` and `CascadedShadowMapSize` 341 -> 680, `RainShadowMapSize` 32 -> 64, `DisableShadowGenTerrain` 1 -> 0, `LeavesShadowRatio` 0.25 -> 0.35, `SpotsCastShadows` 0 -> 1 |
| `medium` | `SunShadowFadeRange` 2 -> 6, `SunShadowRange1` 8 -> 14, `SunShadowRange2` 20 -> 80, `ShadowMapSize` and `CascadedShadowMapSize` 680 -> 1364, `RainShadowMapSize` 64 -> 256, `SoftShadows` 0 -> 1, `LeavesShadowRatio` 0.35 -> 0.5, `ForceLeafSingleSlice` 1 -> 0 |
| `veryhigh` | `SunShadowFadeRange` 6 -> 20, `SunShadowRange0` 4 -> 10, `SunShadowRange1` 14 -> 32, `SunShadowRange2` 80 -> 120, `ShadowMapSize` and `CascadedShadowMapSize` 1364 -> 2560, `RainShadowMapSize` 256 -> 2560, `DisableShadowGenTerrain` 0 -> 1, `LeavesShadowRatio` 0.5 -> 1.0 |
| `ultrahigh` | `SunShadowFadeRange` 10 -> 20, `SunShadowRange0` 4 -> 10, `SunShadowRange1` 20 -> 32, `SunShadowRange2` 140 -> 120, `ShadowMapSize` and `CascadedShadowMapSize` 2048 -> 4096, `RainShadowMapSize` 512 -> 4096, `DisableShadowGenTerrain` 0 -> 1, `LeavesShadowRatio` 0.5 -> 1.0 |

`Ambient` (the static sector shadow and hemisphere maps), old -> new:

| Level | Changes |
|---|---|
| `low` | `ShadowMapSize` 256 -> 512, `SectorTextureSize` 64 -> 128 |
| `medium` | `MaxHemiMapDistance` 128 -> 320, `ShadowMapSize` 512 -> 2560, `SectorCountX` and `SectorCountY` 8 -> 12 |
| `high` | `MaxHemiMapDistance` 160 -> 320, `ShadowMapSize` 512 -> 4096 |

Ultra High's last cascade is pulled in (`SunShadowRange2` 140 -> 120) while the near cascades are
pushed out. The `UnSupportedPlatforms` edits in the same sections belong to `dx10-compatibility`.

## Uncertain

- `DisableShadowGenTerrain` is turned on for Very High and Ultra High and off for Low. Its effect is
  not traced; by name it stops the terrain casting dynamic shadows.
